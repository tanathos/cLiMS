using Clims.Lms;

namespace Clims.Catalog.Tests;

public class CatalogServiceTests
{
    private sealed class Probe
    {
        public List<SyncStatus> Statuses { get; } = new();

        public List<SyncPrompt> Prompts { get; } = new();

        public List<Catalog?> Catalogs { get; } = new();

        public List<int> Refreshed { get; } = new();

        public Probe(ICatalogService service)
        {
            service.StatusChanged += Statuses.Add;
            service.PromptRequested += Prompts.Add;
            service.CatalogChanged += Catalogs.Add;
            service.AlbumRefreshed += Refreshed.Add;
        }
    }

    private static async Task<(FakeLms Lms, MemoryStore Store)> SyncedWorld()
    {
        var lms = FakeLms.WithLibrary();
        var store = new MemoryStore();
        using var seed = Build.Service(lms, store);
        await seed.StartAsync();
        await seed.SyncAsync();
        return (lms, store);
    }

    [Fact]
    public async Task First_run_asks_but_never_syncs_on_its_own()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        var probe = new Probe(service);

        await service.StartAsync();

        Assert.Equal(SyncState.NotSynced, service.Status.State);
        Assert.False(service.Status.HasIndex);
        Assert.Null(service.Current);
        var prompt = Assert.Single(probe.Prompts);
        Assert.Equal(SyncPromptReason.FirstBuild, prompt.Reason);
        Assert.Equal(0, lms.ArtistCalls + lms.AlbumCalls + lms.TrackCalls); // nothing was downloaded
    }

    [Fact]
    public async Task Sync_builds_persists_and_publishes_the_catalogue()
    {
        var lms = FakeLms.WithLibrary(artists: 3, albumsPerArtist: 2, tracksPerAlbum: 4);
        var store = new MemoryStore();
        using var service = Build.Service(lms, store);
        var probe = new Probe(service);
        await service.StartAsync();

        await service.SyncAsync();

        Assert.Equal(SyncState.Synced, service.Status.State);
        Assert.True(service.Status.HasIndex);
        var catalog = service.Current!;
        Assert.Equal(3, catalog.Artists.Count);
        Assert.Equal(6, catalog.Albums.Count);
        Assert.Equal(24, catalog.TrackCount);
        Assert.Equal(2, catalog.Genres.Count);
        Assert.True(catalog.TracksLoaded);

        Assert.Equal(1, store.SaveCount);
        Assert.Same(catalog, store.Saved);
        Assert.Equal(lms.Status().TrackCount, catalog.Meta!.TrackCount);
        Assert.Equal(1_800_000_000, catalog.Meta.SyncedAt.ToUnixTimeSeconds());
        Assert.Equal(lms.LastScan!.Value.ToUnixTimeSeconds(), catalog.Meta.LastScan);
    }

    [Fact]
    public async Task A_first_build_is_browsable_before_tracks_arrive()
    {
        // Artists and albums are published as soon as they are in; tracks stream in after (page size 5 in tests).
        var lms = FakeLms.WithLibrary();
        var store = new MemoryStore();
        using var service = Build.Service(lms, store);
        var probe = new Probe(service);
        Catalog? duringTracks = null;
        Catalog? savedDuring = null;
        lms.OnTracksPage = (start, _) =>
        {
            if (start == 0)
            {
                duringTracks = service.Current;
                savedDuring = store.Saved;
            }

            return Task.CompletedTask;
        };
        await service.StartAsync();

        await service.SyncAsync();

        Assert.NotNull(duringTracks);
        Assert.Equal(6, duringTracks!.Albums.Count);
        Assert.False(duringTracks.TracksLoaded);
        Assert.Equal(0, duringTracks.TrackCount);
        Assert.Null(savedDuring); // nothing is persisted while the catalogue is partial
        Assert.Contains(probe.Catalogs, c => c is { TracksLoaded: false });
        Assert.True(probe.Catalogs[^1]!.TracksLoaded);
    }

    [Fact]
    public async Task Progress_names_the_stages_and_ends_with_all_done()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        var probe = new Probe(service);
        await service.StartAsync();
        await service.SyncAsync();

        var texts = probe.Statuses.Where(s => s.State == SyncState.Syncing).Select(s => s.Progress!).ToList();
        Assert.Contains(texts, t => t.Contains("artists ✓") && t.Contains("albums ✓") && t.Contains("tracks 20%"));
        Assert.Contains(texts, t => t.Contains("tracks ✓"));
        Assert.Equal(SyncState.Syncing, probe.Statuses[^2].State);
        Assert.Equal(SyncState.Synced, probe.Statuses[^1].State);
    }

    [Fact]
    public async Task The_bulk_track_query_asks_for_genres_with_the_one_sort_the_server_accepts()
    {
        // Found against the real server: genre tag plus sort:title (or no sort) makes LMS drop the connection. The fake
        // behaves the same way, so this fails if the sync ever goes back to sort:title.
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        await service.SyncAsync();

        Assert.Equal(SyncState.Synced, service.Status.State);
        Assert.Equal(TrackSort.AlbumTrack, lms.LastBulkTrackQuery!.Sort);
        Assert.True(lms.LastBulkTrackQuery.Tags.HasFlag(TrackTags.Genre));
    }

    [Fact]
    public async Task Tracks_are_fetched_in_pages_and_albumless_tracks_are_skipped()
    {
        var lms = FakeLms.WithLibrary(artists: 1, albumsPerArtist: 1, tracksPerAlbum: 3);
        lms.Tracks.Add(new Track(5000, "Stray", null, null, null, null, null, null, TimeSpan.Zero, null, null, null, null, null, null, null, null, null, null, null));
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        await service.SyncAsync();

        Assert.Equal(3, service.Current!.TrackCount);
        Assert.Equal(1, lms.TrackCalls); // 4 tracks fit in one page of 5
    }

    [Fact]
    public async Task A_matching_saved_index_is_synced_with_no_prompt_and_no_download()
    {
        var (lms, store) = await SyncedWorld();
        var before = lms.AlbumCalls;
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);

        await service.StartAsync();

        Assert.Equal(SyncState.Synced, service.Status.State);
        Assert.NotNull(service.Current);
        Assert.Empty(probe.Prompts);
        Assert.Equal(before, lms.AlbumCalls);
    }

    [Fact]
    public async Task Changed_counts_mark_the_server_changed_and_ask_but_keep_serving_the_old_index()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.Add(FakeLms.MakeTrack(9999, lms.Albums[0].Id, 9, "Added later"));
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);

        await service.StartAsync();

        Assert.Equal(SyncState.ServerChanged, service.Status.State);
        Assert.True(service.Status.HasIndex);
        Assert.Contains("tracks 24 to 25", service.Status.Detail);
        Assert.Equal(24, service.Current!.TrackCount); // old data still browsable
        var prompt = Assert.Single(probe.Prompts);
        Assert.Equal(SyncPromptReason.ServerChanged, prompt.Reason);
        Assert.Equal(1, store.SaveCount); // and nothing was synced
    }

    [Fact]
    public async Task A_moved_lastscan_alone_also_counts()
    {
        var (lms, store) = await SyncedWorld();
        lms.LastScan = lms.LastScan!.Value.AddHours(1);
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();
        Assert.Equal(SyncState.ServerChanged, service.Status.State);
    }

    [Fact]
    public async Task A_finished_scan_that_changed_nothing_does_not_nag()
    {
        // The server sends "rescan done" twice even when nothing changed.
        var (lms, store) = await SyncedWorld();
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);
        await service.StartAsync();
        var events = new FakeEvents();
        var run = service.RunEventsAsync(events);

        events.Push(new ScanFinishedEvent());
        events.Push(new ScanFinishedEvent());
        events.Complete();
        await run;

        Assert.Equal(SyncState.Synced, service.Status.State);
        Assert.Empty(probe.Prompts);
    }

    [Fact]
    public async Task A_finished_scan_that_did_change_things_asks_once()
    {
        var (lms, store) = await SyncedWorld();
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);
        await service.StartAsync();
        var events = new FakeEvents();
        var run = service.RunEventsAsync(events);

        lms.Tracks.RemoveAt(0);
        events.Push(new ScanFinishedEvent());
        events.Push(new ScanFinishedEvent()); // second "rescan done"
        events.Complete();
        await run;

        Assert.Equal(SyncState.ServerChanged, service.Status.State);
        Assert.Single(probe.Prompts);
    }

    [Fact]
    public async Task Later_means_no_second_prompt_this_session_but_the_status_stays_pending()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.RemoveAt(0);
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);
        await service.StartAsync();
        Assert.Single(probe.Prompts);

        service.DeferSync();
        await service.CheckServerAsync();
        await service.CheckServerAsync();

        Assert.Single(probe.Prompts);
        Assert.Equal(SyncState.ServerChanged, service.Status.State);
    }

    [Fact]
    public async Task After_a_later_the_next_launch_asks_again()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.RemoveAt(0);
        using (var first = Build.Service(lms, store, knownUuid: "uuid-1"))
        {
            await first.StartAsync();
            first.DeferSync();
        }

        using var second = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(second);
        await second.StartAsync();
        Assert.Single(probe.Prompts);
    }

    [Fact]
    public async Task A_prompt_left_unanswered_is_not_repeated()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        var probe = new Probe(service);
        await service.StartAsync();
        await service.CheckServerAsync();
        await service.CheckServerAsync();
        Assert.Single(probe.Prompts);
    }

    [Fact]
    public async Task A_new_change_after_being_up_to_date_prompts_again_even_after_a_later()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.RemoveAt(0);
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);
        await service.StartAsync();
        service.DeferSync();

        await service.SyncAsync();                       // user later changes their mind and syncs
        Assert.Equal(SyncState.Synced, service.Status.State);

        lms.Tracks.RemoveAt(0);
        await service.CheckServerAsync();
        Assert.Equal(2, probe.Prompts.Count);
    }

    [Fact]
    public async Task A_resync_replaces_the_old_index_atomically()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.Add(FakeLms.MakeTrack(9999, lms.Albums[0].Id, 9, "Added later"));
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();
        var old = service.Current;

        await service.SyncAsync();

        Assert.NotSame(old, service.Current);
        Assert.Equal(25, service.Current!.TrackCount);
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(SyncState.Synced, service.Status.State);
    }

    [Fact]
    public async Task Offline_with_a_saved_index_keeps_browsing()
    {
        var (lms, store) = await SyncedWorld();
        lms.Outage = new LmsConnectionException("refused");
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);

        await service.StartAsync();

        Assert.Equal(SyncState.Offline, service.Status.State);
        Assert.True(service.Status.HasIndex);
        Assert.NotNull(service.Current);
        Assert.Equal(24, service.Current!.TrackCount);
        Assert.Empty(probe.Prompts);
    }

    [Fact]
    public async Task Offline_without_an_index_is_offline_with_nothing_to_browse()
    {
        var lms = FakeLms.WithLibrary();
        lms.Outage = new LmsConnectionException("refused");
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        Assert.Equal(SyncState.Offline, service.Status.State);
        Assert.False(service.Status.HasIndex);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task The_server_coming_back_is_noticed_through_the_event_stream()
    {
        var (lms, store) = await SyncedWorld();
        lms.Outage = new LmsConnectionException("refused");
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();
        Assert.Equal(SyncState.Offline, service.Status.State);

        var events = new FakeEvents();
        var run = service.RunEventsAsync(events);
        lms.Outage = null;
        events.Push(new EventStreamStateEvent(true));
        events.Complete();
        await run;

        Assert.Equal(SyncState.Synced, service.Status.State);
    }

    [Fact]
    public async Task A_dropped_event_stream_is_checked_against_http_and_flags_offline()
    {
        var (lms, store) = await SyncedWorld();
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();

        var events = new FakeEvents();
        var run = service.RunEventsAsync(events);
        lms.Outage = new LmsConnectionException("gone");
        events.Push(new EventStreamStateEvent(false, "closed"));
        events.Complete();
        await run;

        Assert.Equal(SyncState.Offline, service.Status.State);
    }

    [Fact]
    public async Task Cancelling_a_resync_keeps_the_old_index_and_leaves_the_store_untouched()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.Add(FakeLms.MakeTrack(9999, lms.Albums[0].Id, 9, "Added later"));
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();
        var old = service.Current;
        var savedBefore = store.Saved;
        lms.OnTracksPage = (start, _) =>
        {
            service.CancelSync();
            return Task.CompletedTask;
        };

        await service.SyncAsync();

        Assert.Same(old, service.Current);
        Assert.Same(savedBefore, store.Saved);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(SyncState.ServerChanged, service.Status.State);
        Assert.Contains("cancelled", service.Status.Detail);
    }

    [Fact]
    public async Task Cancelling_a_first_build_drops_the_partial_catalogue()
    {
        var lms = FakeLms.WithLibrary();
        var store = new MemoryStore();
        using var service = Build.Service(lms, store);
        var probe = new Probe(service);
        await service.StartAsync();
        lms.OnTracksPage = (_, _) =>
        {
            service.CancelSync();
            return Task.CompletedTask;
        };

        await service.SyncAsync();

        Assert.Null(service.Current);
        Assert.Null(store.Saved);
        Assert.Equal(SyncState.NotSynced, service.Status.State);
        Assert.Null(probe.Catalogs[^1]);
    }

    [Fact]
    public async Task The_caller_token_cancels_the_sync_too()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        using var cts = new CancellationTokenSource();
        lms.OnTracksPage = (_, _) =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };

        await service.SyncAsync(cts.Token);
        Assert.Equal(SyncState.NotSynced, service.Status.State);
    }

    [Fact]
    public async Task A_connection_failure_during_sync_goes_offline_and_keeps_the_old_data()
    {
        var (lms, store) = await SyncedWorld();
        lms.Tracks.Add(FakeLms.MakeTrack(9999, lms.Albums[0].Id, 9, "Added later"));
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();
        lms.OnTracksPage = (_, _) => throw new LmsConnectionException("unplugged");

        await service.SyncAsync();

        Assert.Equal(SyncState.Offline, service.Status.State);
        Assert.Equal(24, service.Current!.TrackCount);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task A_protocol_failure_during_sync_is_reported_and_retryable()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        var fail = true;
        lms.OnTracksPage = (_, _) => fail ? throw new LmsProtocolException("empty reply") : Task.CompletedTask;

        await service.SyncAsync();
        Assert.Equal(SyncState.NotSynced, service.Status.State);
        Assert.Contains("empty reply", service.Status.Detail);

        fail = false;
        await service.SyncAsync();
        Assert.Equal(SyncState.Synced, service.Status.State);
    }

    [Fact]
    public async Task Sync_waits_while_the_server_is_scanning()
    {
        var lms = FakeLms.WithLibrary();
        lms.ScanPollsRemaining = 3;
        using var service = Build.Service(lms, new MemoryStore());
        var probe = new Probe(service);
        await service.StartAsync();

        await service.SyncAsync();

        Assert.Contains(probe.Statuses, s => s.Progress?.Contains("waiting for the server's scan") == true);
        Assert.Equal(SyncState.Synced, service.Status.State);
        Assert.Equal(0, lms.ScanPollsRemaining);
    }

    [Fact]
    public async Task Cancelling_while_waiting_for_a_scan_stops_cleanly()
    {
        var lms = FakeLms.WithLibrary();
        lms.ScanPollsRemaining = int.MaxValue;
        using var service = Build.Service(lms, new MemoryStore(), scanPoll: TimeSpan.FromMilliseconds(5));
        await service.StartAsync();

        var sync = service.SyncAsync();
        await Task.Delay(50);
        service.CancelSync();
        await sync;

        Assert.Equal(SyncState.NotSynced, service.Status.State);
    }

    [Fact]
    public async Task Two_sync_calls_share_one_run()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        var gate = new TaskCompletionSource();
        lms.OnTracksPage = async (_, _) => await gate.Task;

        var first = service.SyncAsync();
        var second = service.SyncAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, lms.ArtistCalls);
    }

    [Fact]
    public async Task Checking_the_server_during_a_sync_does_not_disturb_it()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        var gate = new TaskCompletionSource();
        lms.OnTracksPage = async (_, _) => await gate.Task;
        var sync = service.SyncAsync();
        await Task.Delay(20);

        await service.CheckServerAsync();
        Assert.Equal(SyncState.Syncing, service.Status.State);

        gate.SetResult();
        await sync;
        Assert.Equal(SyncState.Synced, service.Status.State);
    }

    [Fact]
    public async Task Refreshing_an_album_updates_memory_disk_and_raises_an_event()
    {
        var (lms, store) = await SyncedWorld();
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);
        await service.StartAsync();
        var album = lms.Albums[0].Id;
        lms.Tracks.RemoveAll(t => t.AlbumId == album);
        lms.Tracks.Add(FakeLms.MakeTrack(8001, album, 1, "Edited elsewhere", url: "file:///a.flac"));
        lms.Tracks.Add(FakeLms.MakeTrack(8002, album, 2, "Second", url: "file:///b.flac"));

        var changed = await service.RefreshAlbumAsync(album);

        Assert.True(changed);
        Assert.Equal(new[] { "Edited elsewhere", "Second" }, service.Current!.TracksOf(album).Select(t => t.Title));
        Assert.Equal("file:///a.flac", service.Current.TracksOf(album)[0].Url);
        Assert.Equal(2, store.ReplacedAlbums[album].Count);
        Assert.Equal(new[] { album }, probe.Refreshed);
    }

    [Fact]
    public async Task Refreshing_an_unchanged_album_is_silent()
    {
        var (lms, store) = await SyncedWorld();
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        var probe = new Probe(service);
        await service.StartAsync();
        var album = lms.Albums[0].Id;
        await service.RefreshAlbumAsync(album); // first refresh adds file URLs
        probe.Refreshed.Clear();

        Assert.False(await service.RefreshAlbumAsync(album));
        Assert.Empty(probe.Refreshed);
    }

    [Fact]
    public async Task Refresh_while_offline_keeps_what_the_index_has()
    {
        var (lms, store) = await SyncedWorld();
        using var service = Build.Service(lms, store, knownUuid: "uuid-1");
        await service.StartAsync();
        lms.Outage = new LmsConnectionException("refused");

        Assert.False(await service.RefreshAlbumAsync(lms.Albums[0].Id));
        Assert.Equal(4, service.Current!.TracksOf(lms.Albums[0].Id).Count);
    }

    [Fact]
    public async Task Refresh_ignores_unknown_albums_and_a_missing_catalogue()
    {
        var lms = FakeLms.WithLibrary();
        using var empty = Build.Service(lms, new MemoryStore());
        Assert.False(await empty.RefreshAlbumAsync(100));

        var (lms2, store) = await SyncedWorld();
        using var service = Build.Service(lms2, store, knownUuid: "uuid-1");
        await service.StartAsync();
        Assert.False(await service.RefreshAlbumAsync(424242));
        Assert.Empty(lms2.TrackAlbumQueries);
    }

    [Fact]
    public async Task During_a_first_build_a_refresh_fills_the_visible_album_straight_away()
    {
        var lms = FakeLms.WithLibrary();
        var store = new MemoryStore();
        using var service = Build.Service(lms, store);
        await service.StartAsync();
        var album = lms.Albums[0].Id;
        var refreshedDuring = false;
        lms.OnTracksPage = async (start, ct) =>
        {
            if (start == 0)
            {
                refreshedDuring = await service.RefreshAlbumAsync(album, ct);
            }
        };

        await service.SyncAsync();

        Assert.True(refreshedDuring);
        Assert.Equal(4, service.Current!.TracksOf(album).Count);
    }

    [Fact]
    public async Task Every_status_change_is_a_real_change()
    {
        var lms = FakeLms.WithLibrary();
        using var service = Build.Service(lms, new MemoryStore());
        var probe = new Probe(service);
        await service.StartAsync();
        await service.CheckServerAsync();
        await service.CheckServerAsync();

        Assert.Single(probe.Statuses); // identical statuses are not re-announced
    }

    [Fact]
    public async Task The_sync_asks_the_server_for_the_tracks_of_every_genre_and_keeps_all_the_links()
    {
        // Track 1000 is a "Metal" track by its own genre; the server also files it under Jazz.
        var lms = FakeLms.WithLibrary();
        lms.ExtraGenreTracks[2] = new List<int> { 1000 };
        var store = new MemoryStore();
        using var service = Build.Service(lms, store);
        await service.StartAsync();
        await service.SyncAsync();

        Assert.True(lms.GenreTrackCalls >= lms.Genres.Count); // one request per page of each genre
        var links = service.Current!.GenreTracks!;
        Assert.Contains(1000, links[1]);
        Assert.Contains(1000, links[2]);
        Assert.Same(service.Current, store.Saved);
        Assert.NotNull(store.Saved!.GenreTracks);
    }

    [Fact]
    public async Task The_genre_link_pass_asks_for_ids_only_in_the_cheap_sort()
    {
        var lms = FakeLms.WithLibrary();
        TrackQuery? seen = null;
        lms.OnGenreQuery = q => seen = q;
        using var service = Build.Service(lms, new MemoryStore());
        await service.StartAsync();
        await service.SyncAsync();

        Assert.Equal(TrackTags.None, seen!.Tags);
        Assert.Equal(TrackSort.New, seen.Sort);
    }
}
