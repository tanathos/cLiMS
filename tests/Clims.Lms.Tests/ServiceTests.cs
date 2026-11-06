namespace Clims.Lms.Tests;

public class LibraryApiTests
{
    [Fact]
    public async Task Albums_are_mapped_from_the_recorded_reply()
    {
        var (client, handler) = TestClient.FromFixture("albums");
        var page = await client.Library.GetAlbumsAsync(count: 5);

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(2008, page.Total);
        Assert.True(page.HasMore);

        var first = page.Items[0];
        Assert.Equal(853519, first.Id);
        Assert.Equal("+-", first.Title);
        Assert.Equal("Mew", first.ArtistName);
        Assert.Equal(437329, first.ArtistId);
        Assert.Equal(2015, first.Year);
        Assert.Equal("2836a7e5", first.ArtworkTrackId);
        Assert.Equal("+", first.TextKey);
        Assert.False(first.IsCompilation);
        Assert.Equal("ALBUM", first.ReleaseType);

        Assert.Equal(2, page.Items[1].DiscCount);
        Assert.Equal(0, page.Items[2].Year);
        Assert.Null(page.Items[4].ArtworkTrackId); // album without artwork
        Assert.Equal("10 Años Un Panteón Muy Vivo", page.Items[4].Title); // accents survive

        Assert.Equal("albums 0 5000".Split(' ').Length + 2, handler.Requests[0].Command.Length);
    }

    [Fact]
    public async Task Album_query_becomes_filters_tags_and_sort()
    {
        var (client, handler) = TestClient.FromFixture("albums_empty");
        await client.Library.GetAlbumsAsync(
            new AlbumQuery { ArtistId = 12, GenreId = 3, Year = 2015, Compilation = false, Search = "iron", Sort = AlbumSort.YearAlbum, Tags = AlbumTags.Album | AlbumTags.ArtworkTrackId },
            start: 100,
            count: 50);

        var c = handler.Requests[0].Command;
        Assert.Equal(new[] { "albums", "100", "50", "tags:lj", "sort:yearalbum" }, c.Take(5));
        Assert.Contains("artist_id:12", c);
        Assert.Contains("genre_id:3", c);
        Assert.Contains("year:2015", c);
        Assert.Contains("compilation:0", c);
        Assert.Contains("search:iron", c);
        Assert.Equal(string.Empty, handler.Requests[0].PlayerId);
    }

    [Fact]
    public async Task No_matches_is_an_empty_page_not_an_error()
    {
        var (client, _) = TestClient.FromFixture("albums_empty");
        var page = await client.Library.GetAlbumsAsync();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Tracks_cope_with_numbers_typed_as_strings()
    {
        var (client, _) = TestClient.FromFixture("titles");
        var page = await client.Library.GetTracksAsync(new TrackQuery { AlbumId = 853519 }, count: 6);

        Assert.Equal(6, page.Items.Count);
        Assert.Equal(10, page.Total);
        var t = page.Items[0];
        Assert.Equal(8547903, t.Id);
        Assert.Equal("Satellites", t.Title);
        Assert.Equal("Mew", t.Artist);
        Assert.Equal(1, t.TrackNumber);            // "tracknum":"1"
        Assert.Equal(2015, t.Year);                // "year":"2015"
        Assert.Equal(853519, t.AlbumId);           // "album_id":"853519"
        Assert.Equal(437329, t.ArtistId);
        Assert.Equal(TimeSpan.FromSeconds(369.373), t.Duration);
        Assert.Equal(false, t.IsLossless);         // "lossless":"0"
        Assert.Equal(false, t.IsCompilation);
        Assert.Equal(44100, t.SampleRate);
        Assert.Equal("320kbps CBR", t.Bitrate);
        Assert.Equal("Alternative Rock", t.Genre);
        Assert.Equal(85292, t.GenreId);
        Assert.Equal("2836a7e5", t.ArtworkTrackId);
        Assert.Equal("852b145a", t.CoverId);
        Assert.EndsWith("01%20-%20Satellites.mp3", t.Url);
        Assert.Null(t.DiscNumber);                 // not requested/returned
    }

    [Fact]
    public async Task Track_query_asks_for_the_album_and_the_requested_tags()
    {
        var (client, handler) = TestClient.FromFixture("titles");
        await client.Library.GetTracksAsync(new TrackQuery { AlbumId = 5, Tags = TrackTags.Artist | TrackTags.Url, Sort = TrackSort.TrackNumber }, 10, 20);
        Assert.Equal(new[] { "titles", "10", "20", "tags:au", "sort:tracknum", "album_id:5" }, handler.Requests[0].Command);
    }

    [Fact]
    public async Task Empty_titles_reply_is_an_empty_page()
    {
        var (client, _) = TestClient.FromFixture("titles_empty");
        var page = await client.Library.GetTracksAsync(new TrackQuery { Search = "zzz" });
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Artists_and_genres_are_mapped()
    {
        var (artists, _) = TestClient.FromFixture("artists");
        var a = await artists.Library.GetArtistsAsync();
        Assert.Equal(1004, a.Total);
        Assert.Equal("The 13th Floor Elevators", a.Items[0].Name);
        Assert.Equal(437617, a.Items[0].Id);

        var (genres, _) = TestClient.FromFixture("genres");
        var g = await genres.Library.GetGenresAsync();
        Assert.Equal(195, g.Total);
        Assert.Equal("Abstract", g.Items[0].Name);
        Assert.Equal(85397, g.Items[0].Id);
    }

    [Fact]
    public async Task Search_reads_the_four_groups_and_their_totals()
    {
        var (client, handler) = TestClient.FromFixture("search");
        var r = await client.Library.SearchAsync("iron", 5);

        Assert.Equal(new[] { "search", "0", "5", "term:iron" }, handler.Requests[0].Command);
        Assert.Equal(2, r.ArtistCount);
        Assert.Equal("Iron Maiden", r.Artists[0].Name);
        Assert.Equal(437214, r.Artists[0].Id);
        Assert.Equal(4, r.AlbumCount);
        Assert.Equal(4, r.Albums.Count);
        Assert.Equal(29, r.TrackCount);
        Assert.Equal(5, r.Tracks.Count);
        Assert.Equal(8537238, r.Tracks[0].Id);
        Assert.Empty(r.Genres);
        Assert.Equal(0, r.GenreCount);
    }

    [Fact]
    public async Task Search_without_hits_is_empty()
    {
        var (client, _) = TestClient.FromFixture("search_empty");
        var r = await client.Library.SearchAsync("zzzqqqxxx");
        Assert.Empty(r.Artists);
        Assert.Empty(r.Albums);
        Assert.Empty(r.Tracks);
        Assert.Equal(0, r.TrackCount);
    }

    [Fact]
    public async Task Songinfo_fields_are_merged_into_one_track()
    {
        var (client, handler) = TestClient.FromFixture("songinfo");
        var t = await client.Library.GetTrackByUrlAsync("file:///mnt/Music/Mew/+%20-/01%20-%20Satellites.mp3");

        Assert.NotNull(t);
        Assert.Equal(8547903, t!.Id);
        Assert.Equal("Satellites", t.Title);
        Assert.Equal(2015, t.Year);
        Assert.Equal(false, t.IsLossless);
        Assert.Contains(handler.Requests[0].Command, w => w.StartsWith("url:file:///mnt/Music/Mew/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_track_by_url_is_null()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        var client = TestClient.Create(handler);
        Assert.Null(await client.Library.GetTrackByUrlAsync("file:///nope.flac"));
    }

    [Fact]
    public async Task Track_by_id_uses_a_one_item_titles_query_and_returns_null_when_missing()
    {
        var (client, handler) = TestClient.FromFixture("titles_empty");
        Assert.Null(await client.Library.GetTrackAsync(42));
        var c = handler.Requests[0].Command;
        Assert.Equal(new[] { "titles", "0", "1" }, c.Take(3));
        Assert.Contains("track_id:42", c);
    }

    [Fact]
    public async Task No_saved_playlists_gives_an_empty_page()
    {
        // LMS answers {} (not even a count) when none exist.
        var (client, _) = TestClient.FromFixture("playlists");
        var page = await client.Library.GetPlaylistsAsync();
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Library_reads_are_flagged_retryable()
    {
        var attempts = 0;
        var handler = new FakeHandler(_ =>
        {
            if (++attempts == 1)
            {
                throw new HttpRequestException(HttpRequestError.ConnectionError, "blip");
            }

            return FakeHandler.Json(Fixtures.Load("genres"));
        });
        var client = TestClient.Create(handler);
        var page = await client.Library.GetGenresAsync();
        Assert.Equal(2, attempts);
        Assert.Equal(195, page.Total);
    }
}

public class PlayerApiTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";

    [Fact]
    public async Task Status_is_mapped_including_loosely_typed_fields()
    {
        var (client, handler) = TestClient.FromFixture("status");
        var before = DateTimeOffset.UtcNow;
        var s = await client.Player.GetStatusAsync(Mac, 0, 4);

        Assert.Equal(Mac, handler.Requests[0].PlayerId);
        Assert.Equal("piCorePlayer", s.Name);
        Assert.True(s.IsConnected);
        Assert.True(s.IsPoweredOn);
        Assert.Equal(PlayMode.Play, s.Mode);
        Assert.Equal(78.9679701118469, s.Elapsed.TotalSeconds, 6);
        Assert.Equal(TimeSpan.FromSeconds(421.906), s.Duration);
        Assert.Equal(70, s.Volume);
        Assert.Equal(RepeatMode.Off, s.Repeat);
        Assert.Equal(ShuffleMode.Off, s.Shuffle);
        Assert.True(s.CanSeek);
        Assert.Equal(3, s.CurrentIndex);            // "playlist_cur_index":"3"
        Assert.Equal(9, s.QueueLength);
        Assert.Equal(1793975126.07075, s.QueueTimestamp);
        Assert.True(s.ReceivedAt >= before);

        Assert.Equal(4, s.Items.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, s.Items.Select(i => i.Index));
        Assert.Equal("This Is Where It Falls Apart", s.CurrentTrack!.Title);
        Assert.Equal(true, s.Items[0].Track.IsLossless);
        Assert.Equal(16, s.Items[0].Track.SampleSize);
    }

    [Fact]
    public async Task Status_of_the_current_track_starts_the_window_with_a_dash()
    {
        var (client, handler) = TestClient.FromFixture("status_current");
        var s = await client.Player.GetStatusAsync(Mac);
        Assert.Equal(new[] { "status", "-", "1" }, handler.Requests[0].Command.Take(3));
        Assert.Single(s.Items);
        Assert.Equal(3, s.Items[0].Index);
    }

    [Fact]
    public void Elapsed_is_interpolated_while_playing_and_frozen_otherwise()
    {
        var at = DateTimeOffset.UtcNow;
        PlayerStatus Make(PlayMode mode) => new("p", true, true, mode, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100), 50,
            RepeatMode.Off, ShuffleMode.Off, true, 0, 1, 1, Array.Empty<QueueItem>(), at);

        Assert.Equal(TimeSpan.FromSeconds(12.5), Make(PlayMode.Play).ElapsedAt(at.AddSeconds(2.5)));
        Assert.Equal(TimeSpan.FromSeconds(10), Make(PlayMode.Pause).ElapsedAt(at.AddSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(10), Make(PlayMode.Stop).ElapsedAt(at.AddSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(100), Make(PlayMode.Play).ElapsedAt(at.AddSeconds(500))); // clamped to the duration
        Assert.Equal(TimeSpan.FromSeconds(10), Make(PlayMode.Play).ElapsedAt(at.AddSeconds(-5)));    // clock skew
    }

    [Fact]
    public async Task Stopped_player_with_an_empty_queue_has_no_current_index()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"player_name\":\"x\",\"mode\":\"stop\",\"playlist_tracks\":0,\"playlist_cur_index\":\"0\",\"time\":0}"));
        var s = await TestClient.Create(handler).Player.GetStatusAsync(Mac);
        Assert.Equal(PlayMode.Stop, s.Mode);
        Assert.Null(s.CurrentIndex);
        Assert.Null(s.CurrentTrack);
        Assert.Empty(s.Items);
    }

    public static IEnumerable<object[]> Commands() => new[]
    {
        new object[] { "play", (Func<IPlayerApi, Task>)(p => p.PlayAsync(Mac)), "play" },
        new object[] { "pause toggle", (Func<IPlayerApi, Task>)(p => p.PauseAsync(Mac)), "pause" },
        new object[] { "pause on", (Func<IPlayerApi, Task>)(p => p.PauseAsync(Mac, true)), "pause 1" },
        new object[] { "pause off", (Func<IPlayerApi, Task>)(p => p.PauseAsync(Mac, false)), "pause 0" },
        new object[] { "stop", (Func<IPlayerApi, Task>)(p => p.StopAsync(Mac)), "stop" },
        new object[] { "seek", (Func<IPlayerApi, Task>)(p => p.SeekAsync(Mac, TimeSpan.FromSeconds(83.25))), "time 83.25" },
        new object[] { "seek negative clamps", (Func<IPlayerApi, Task>)(p => p.SeekAsync(Mac, TimeSpan.FromSeconds(-4))), "time 0" },
        new object[] { "seek forward", (Func<IPlayerApi, Task>)(p => p.SeekByAsync(Mac, TimeSpan.FromSeconds(10))), "time +10" },
        new object[] { "seek back", (Func<IPlayerApi, Task>)(p => p.SeekByAsync(Mac, TimeSpan.FromSeconds(-10))), "time -10" },
        new object[] { "volume", (Func<IPlayerApi, Task>)(p => p.SetVolumeAsync(Mac, 35)), "mixer volume 35" },
        new object[] { "volume clamps high", (Func<IPlayerApi, Task>)(p => p.SetVolumeAsync(Mac, 140)), "mixer volume 100" },
        new object[] { "volume clamps low", (Func<IPlayerApi, Task>)(p => p.SetVolumeAsync(Mac, -3)), "mixer volume 0" },
        new object[] { "volume up", (Func<IPlayerApi, Task>)(p => p.AdjustVolumeAsync(Mac, 5)), "mixer volume +5" },
        new object[] { "volume down", (Func<IPlayerApi, Task>)(p => p.AdjustVolumeAsync(Mac, -5)), "mixer volume -5" },
        new object[] { "mute", (Func<IPlayerApi, Task>)(p => p.SetMutedAsync(Mac, true)), "mixer muting 1" },
        new object[] { "next", (Func<IPlayerApi, Task>)(p => p.NextAsync(Mac)), "playlist index +1" },
        new object[] { "previous", (Func<IPlayerApi, Task>)(p => p.PreviousAsync(Mac)), "playlist index -1" },
        new object[] { "jump", (Func<IPlayerApi, Task>)(p => p.JumpToAsync(Mac, 7)), "playlist index 7" },
        new object[] { "repeat queue", (Func<IPlayerApi, Task>)(p => p.SetRepeatAsync(Mac, RepeatMode.Queue)), "playlist repeat 2" },
        new object[] { "shuffle albums", (Func<IPlayerApi, Task>)(p => p.SetShuffleAsync(Mac, ShuffleMode.Albums)), "playlist shuffle 2" },
        new object[] { "power on", (Func<IPlayerApi, Task>)(p => p.SetPowerAsync(Mac, true)), "power 1" },
        new object[] { "power off", (Func<IPlayerApi, Task>)(p => p.SetPowerAsync(Mac, false)), "power 0" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Transport_commands_send_the_expected_words_to_the_right_player(string name, Func<IPlayerApi, Task> call, string expected)
    {
        _ = name;
        var (client, handler) = TestClient.Silent();
        await call(client.Player);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expected, request.Text);
        Assert.Equal(Mac, request.PlayerId);
    }

    [Fact]
    public async Task Seek_uses_a_dot_decimal_separator_whatever_the_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            var (client, handler) = TestClient.Silent();
            await client.Player.SeekAsync(Mac, TimeSpan.FromSeconds(1.5));
            Assert.Equal("time 1.5", handler.Requests[0].Text);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = old;
        }
    }
}

public class QueueApiTests
{
    private const string Mac = "b8:27:eb:a9:77:8f";

    private static IReadOnlyList<int> Ids(int n, int from = 1) => Enumerable.Range(from, n).ToList();

    private static int[] ParseIds(RecordedRequest r) =>
        r.Command.Single(w => w.StartsWith("track_id:", StringComparison.Ordinal))["track_id:".Length..].Split(',').Select(int.Parse).ToArray();

    [Fact]
    public async Task Add_sends_one_request_for_a_small_list()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, new[] { 5, 6, 7 });
        var r = Assert.Single(handler.Requests);
        Assert.Equal("playlistcontrol cmd:add track_id:5,6,7", r.Text);
        Assert.Equal(Mac, r.PlayerId);
    }

    [Fact]
    public async Task Add_splits_long_lists_into_chunks_of_at_most_400_in_order()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, Ids(950));

        Assert.Equal(new[] { 400, 400, 150 }, handler.Requests.Select(r => ParseIds(r).Length));
        Assert.Equal(Ids(950), handler.Requests.SelectMany(ParseIds));
        Assert.All(handler.Requests, r => Assert.Contains("cmd:add", r.Command));
    }

    [Fact]
    public async Task Request_bodies_of_a_full_chunk_stay_well_under_the_8kb_server_limit()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, Ids(400, 8_500_000)); // realistic 7-digit ids
        Assert.True(handler.Requests[0].Body.Length < 6000, $"body was {handler.Requests[0].Body.Length} bytes");
    }

    [Fact]
    public async Task The_chunk_size_comes_from_the_options()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{}"));
        var client = TestClient.Create(handler, o => o.MaxIdsPerRequest = 100);
        await client.Queue.AddTracksAsync(Mac, Ids(250));
        Assert.Equal(new[] { 100, 100, 50 }, handler.Requests.Select(r => ParseIds(r).Length));
    }

    [Fact]
    public async Task Load_replaces_the_queue_with_the_first_chunk_then_appends_the_rest()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.LoadTracksAsync(Mac, Ids(500), playIndex: 12);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("cmd:load", handler.Requests[0].Command);
        Assert.Contains("play_index:12", handler.Requests[0].Command);
        Assert.Contains("cmd:add", handler.Requests[1].Command);
        Assert.DoesNotContain(handler.Requests[1].Command, w => w.StartsWith("play_index", StringComparison.Ordinal));
        Assert.Equal(Ids(500), handler.Requests.SelectMany(ParseIds));
    }

    [Fact]
    public async Task Insert_next_sends_chunks_last_to_first_so_the_final_order_is_kept()
    {
        // Each insert lands right after the current track; sending chunk 2 first leaves chunk 1 in front of it.
        var (client, handler) = TestClient.Silent();
        await client.Queue.InsertTracksNextAsync(Mac, Ids(900));

        var sent = handler.Requests.Select(ParseIds).ToList();
        Assert.Equal(new[] { 100, 400, 400 }, sent.Select(s => s.Length));
        Assert.Equal(801, sent[0][0]);
        Assert.Equal(1, sent[^1][0]);
        Assert.All(handler.Requests, r => Assert.Contains("cmd:insert", r.Command));
    }

    [Fact]
    public async Task Album_commands_use_album_id()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.LoadAlbumAsync(Mac, 853519, 2);
        await client.Queue.AddAlbumAsync(Mac, 853519);
        await client.Queue.InsertAlbumNextAsync(Mac, 853519);

        Assert.Equal("playlistcontrol cmd:load album_id:853519 play_index:2", handler.Requests[0].Text);
        Assert.Equal("playlistcontrol cmd:add album_id:853519", handler.Requests[1].Text);
        Assert.Equal("playlistcontrol cmd:insert album_id:853519", handler.Requests[2].Text);
    }

    [Fact]
    public async Task Remove_deletes_from_the_highest_index_down_and_ignores_duplicates()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.RemoveAsync(Mac, new[] { 3, 9, 3, 1 });
        Assert.Equal(new[] { "playlist delete 9", "playlist delete 3", "playlist delete 1" }, handler.Requests.Select(r => r.Text));
    }

    [Fact]
    public async Task Move_and_clear()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.MoveAsync(Mac, 4, 1);
        await client.Queue.ClearAsync(Mac);
        Assert.Equal(new[] { "playlist move 4 1", "playlist clear" }, handler.Requests.Select(r => r.Text));
    }

    [Fact]
    public async Task Empty_id_lists_send_nothing()
    {
        var (client, handler) = TestClient.Silent();
        await client.Queue.AddTracksAsync(Mac, Array.Empty<int>());
        await client.Queue.InsertTracksNextAsync(Mac, Array.Empty<int>());
        await client.Queue.LoadTracksAsync(Mac, Array.Empty<int>());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Queue_window_comes_from_status_with_the_server_total()
    {
        var (client, handler) = TestClient.FromFixture("status");
        var page = await client.Queue.GetQueueAsync(Mac, 0, 4);
        Assert.Equal(4, page.Items.Count);
        Assert.Equal(9, page.Total);
        Assert.True(page.HasMore);
        Assert.Equal("status 0 4", string.Join(' ', handler.Requests[0].Command.Take(3)));
    }

    [Fact]
    public async Task A_failing_chunk_stops_the_remaining_chunks()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 2 ? FakeHandler.Json("") : FakeHandler.Result("{}"));
        var client = TestClient.Create(handler);
        await Assert.ThrowsAsync<LmsProtocolException>(() => client.Queue.AddTracksAsync(Mac, Ids(1000)));
        Assert.Equal(2, calls);
    }
}

public class ServerApiTests
{
    [Fact]
    public async Task Server_status_is_mapped()
    {
        var (client, _) = TestClient.FromFixture("serverstatus");
        var s = await client.Server.GetStatusAsync();

        Assert.Equal("9.1.1", s.Version);
        Assert.Equal("7c1f32ca-018f-41d3-b018-c4587df043f7", s.Uuid);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793939356), s.LastScan);
        Assert.Equal(2008, s.AlbumCount);
        Assert.Equal(1004, s.ArtistCount);
        Assert.Equal(195, s.GenreCount);
        Assert.Equal(19948, s.TrackCount);
        Assert.Equal(6116359.01, s.TotalDuration.TotalSeconds, 1);

        Assert.Equal(2, s.Players.Count);
        var pi = s.Players[0];
        Assert.Equal("b8:27:eb:a9:77:8f", pi.PlayerId);
        Assert.Equal("piCorePlayer", pi.Name);
        Assert.Equal("squeezelite", pi.Model);
        Assert.True(pi.IsConnected);
        Assert.True(pi.IsPlaying);
        Assert.True(pi.IsPoweredOn);
        Assert.True(pi.IsHardwarePlayer);

        var web = s.Players[1];
        Assert.Equal("192.168.1.21", web.PlayerId);
        Assert.False(web.IsHardwarePlayer);
        Assert.False(web.IsConnected);
    }

    [Fact]
    public async Task Version_total_count_and_scan_state()
    {
        var (v, _) = TestClient.FromFixture("version");
        Assert.Equal("9.1.1", await v.Server.GetVersionAsync());

        var (t, _) = TestClient.FromFixture("info_total_songs");
        Assert.Equal(19948, await t.Server.GetTrackCountAsync());

        var (s, _) = TestClient.FromFixture("rescanprogress");
        var scan = await s.Server.GetScanStateAsync();
        Assert.False(scan.IsScanning);
        Assert.Empty(scan.Details);
    }

    [Fact]
    public async Task Scan_in_progress_exposes_its_details()
    {
        var handler = new FakeHandler(_ => FakeHandler.Result("{\"rescan\":1,\"steps\":\"directory,mp3tags\",\"directory\":\"100\",\"mp3tags\":\"43\"}"));
        var scan = await TestClient.Create(handler).Server.GetScanStateAsync();
        Assert.True(scan.IsScanning);
        Assert.Equal("43", scan.Details["mp3tags"]);
        Assert.False(scan.Details.ContainsKey("rescan"));
    }

    [Fact]
    public async Task Can_asks_whether_a_command_exists()
    {
        var (client, handler) = TestClient.FromFixture("can_playlistcontrol");
        Assert.True(await client.Server.CanAsync(new[] { "playlistcontrol" }));
        Assert.Equal("can playlistcontrol ?", handler.Requests[0].Text);

        var no = new FakeHandler(_ => FakeHandler.Result("{\"_can\":0}"));
        Assert.False(await TestClient.Create(no).Server.CanAsync(new[] { "playlist", "bogus" }));
    }

    [Fact]
    public async Task Players_are_listed()
    {
        var (client, handler) = TestClient.FromFixture("players");
        var players = await client.Server.GetPlayersAsync();
        Assert.Equal(2, players.Count);
        Assert.Equal("players", handler.Requests[0].Command[0]);
    }
}
