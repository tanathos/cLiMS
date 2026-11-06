namespace Clims.Lms.Tests;

/// <summary>
/// Read-only checks against a real server. Skipped unless <c>CLIMS_LMS_URL</c> is set, for example
/// <c>$env:CLIMS_LMS_URL = "http://192.168.1.20:9000/"</c>. Nothing here changes server or player state.
/// </summary>
public class ReadOnlyIntegrationTests
{
    [IntegrationFact]
    public async Task Server_reports_a_version_and_a_populated_library()
    {
        using var client = Integration.CreateClient();
        var status = await client.Server.GetStatusAsync();

        Assert.False(string.IsNullOrEmpty(status.Version));
        Assert.True(status.TrackCount > 0);
        Assert.True(status.AlbumCount > 0);
        Assert.Equal(status.Version, await client.Server.GetVersionAsync());
        Assert.Equal(status.TrackCount, await client.Server.GetTrackCountAsync());
        Assert.True(await client.Server.CanAsync(new[] { "playlistcontrol" }));
        Assert.NotNull((await client.Server.GetScanStateAsync()).Details); // idle or scanning, it must parse
    }

    [IntegrationFact]
    public async Task Albums_page_has_titles_artists_and_mostly_artwork()
    {
        using var client = Integration.CreateClient();
        var status = await client.Server.GetStatusAsync();
        var page = await client.Library.GetAlbumsAsync(count: 50);

        Assert.Equal(status.AlbumCount, page.Total);
        Assert.Equal(Math.Min(50, status.AlbumCount), page.Items.Count);
        Assert.All(page.Items, a => Assert.False(string.IsNullOrEmpty(a.Title)));
        Assert.All(page.Items, a => Assert.False(string.IsNullOrEmpty(a.ArtistName)));
        Assert.Contains(page.Items, a => a.ArtworkTrackId is not null);
    }

    [IntegrationFact]
    public async Task All_artists_can_be_streamed_with_paging()
    {
        using var client = Integration.CreateClient();
        var status = await client.Server.GetStatusAsync();
        var count = 0;
        await foreach (var _ in Paging.EnumerateAsync((s, c, t) => client.Library.GetArtistsAsync(null, s, c, t), 300))
        {
            count++;
        }

        Assert.Equal(status.ArtistCount, count);
    }

    [IntegrationFact]
    public async Task A_large_track_page_works()
    {
        using var client = Integration.CreateClient();
        var page = await client.Library.GetTracksAsync(new TrackQuery { Sort = TrackSort.Title, Tags = TrackTags.Url | TrackTags.AlbumId }, 0, 2000);

        Assert.Equal(Math.Min(2000, page.Total), page.Items.Count);
        Assert.All(page.Items, t => Assert.False(string.IsNullOrEmpty(t.Url)));
    }

    [IntegrationFact]
    public async Task Tracks_of_an_album_and_a_lookup_by_url_agree()
    {
        using var client = Integration.CreateClient();
        var album = (await client.Library.GetAlbumsAsync(count: 1)).Items[0];
        var tracks = await client.Library.GetTracksAsync(new TrackQuery { AlbumId = album.Id, Tags = TrackTags.All, Sort = TrackSort.TrackNumber });

        Assert.NotEmpty(tracks.Items);
        var first = tracks.Items[0];
        var byUrl = await client.Library.GetTrackByUrlAsync(first.Url!);
        var byId = await client.Library.GetTrackAsync(first.Id);

        Assert.Equal(first.Id, byUrl!.Id);
        Assert.Equal(first.Title, byUrl.Title);
        Assert.Equal(first.Id, byId!.Id);
    }

    [IntegrationFact]
    public async Task Search_finds_a_word_prefix()
    {
        using var client = Integration.CreateClient();
        var first = (await client.Library.GetArtistsAsync(count: 1)).Items[0];
        var word = first.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).First(w => w.Length >= 3);
        var results = await client.Library.SearchAsync(word[..3]);

        Assert.True(results.ArtistCount + results.AlbumCount + results.TrackCount > 0);
    }

    [IntegrationFact]
    public async Task No_match_returns_empty_pages_rather_than_errors()
    {
        using var client = Integration.CreateClient();
        Assert.Empty((await client.Library.GetAlbumsAsync(new AlbumQuery { Search = "zzzqqqxxx" })).Items);
        Assert.Empty((await client.Library.GetTracksAsync(new TrackQuery { Search = "zzzqqqxxx" })).Items);
        Assert.Equal(0, (await client.Library.SearchAsync("zzzqqqxxx")).TrackCount);
        Assert.True((await client.Library.GetPlaylistsAsync()).Total >= 0); // none saved answers {} and must still parse
    }

    [IntegrationFact]
    public async Task Cover_art_url_serves_a_jpeg()
    {
        using var client = Integration.CreateClient();
        var album = (await client.Library.GetAlbumsAsync(count: 50)).Items.First(a => a.ArtworkTrackId is not null);
        using var http = new HttpClient();
        using var response = await http.GetAsync(ArtworkUrl.Cover(client.BaseUri, album.ArtworkTrackId!, 96, 96));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 500);
    }

    [IntegrationFact]
    public async Task Players_and_status_of_the_first_hardware_player()
    {
        using var client = Integration.CreateClient();
        var players = await client.Server.GetPlayersAsync();
        var player = players.FirstOrDefault(p => p.IsHardwarePlayer && p.IsConnected);
        if (player is null)
        {
            return; // nothing connected right now; not a failure of the library
        }

        var status = await client.Player.GetStatusAsync(player.PlayerId, 0, 3);
        Assert.Equal(player.Name, status.Name);
        Assert.InRange(status.Volume, 0, 100);
        Assert.True(status.QueueLength >= status.Items.Count);
    }

    [IntegrationFact]
    public async Task An_unknown_player_surfaces_as_a_protocol_error()
    {
        // LMS answers a bad player id by closing the connection; the library must turn that into a typed error.
        using var client = Integration.CreateClient();
        var ex = await Assert.ThrowsAsync<LmsProtocolException>(() => client.Player.GetStatusAsync("00:00:00:00:00:00"));
        Assert.Equal("00:00:00:00:00:00", ex.PlayerId);
    }

    [IntegrationFact]
    public async Task The_cli_event_stream_connects()
    {
        var uri = new Uri(Integration.Url!);
        var stream = new CliEventStream(new CliEventStreamOptions { Host = uri.Host });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await foreach (var e in stream.ReadAllAsync(cts.Token))
        {
            Assert.True(Assert.IsType<EventStreamStateEvent>(e).Connected);
            break;
        }
    }
}

/// <summary>Remembers a player's state and puts it back, for tests that must touch the real player.</summary>
public sealed class PlayerSnapshot
{
    private PlayerSnapshot(string playerId, PlayerStatus status, IReadOnlyList<int> queue)
    {
        PlayerId = playerId;
        Status = status;
        Queue = queue;
    }

    public string PlayerId { get; }

    public PlayerStatus Status { get; }

    public IReadOnlyList<int> Queue { get; }

    public static async Task<PlayerSnapshot> TakeAsync(ILmsClient client, string playerId)
    {
        var status = await client.Player.GetStatusAsync(playerId, 0, 0, TrackTags.None);
        var ids = new List<int>();
        for (var start = 0; start < status.QueueLength; start += 200)
        {
            var page = await client.Queue.GetQueueAsync(playerId, start, 200, TrackTags.None);
            ids.AddRange(page.Items.Select(i => i.Track.Id));
        }

        return new PlayerSnapshot(playerId, status, ids);
    }

    /// <summary>Restores queue (only if it changed), position, volume, modes and play/pause state.</summary>
    public async Task RestoreAsync(ILmsClient client)
    {
        var now = await TakeAsync(client, PlayerId);
        if (!now.Queue.SequenceEqual(Queue))
        {
            if (Queue.Count == 0)
            {
                await client.Queue.ClearAsync(PlayerId);
            }
            else
            {
                await client.Queue.LoadTracksAsync(PlayerId, Queue, Status.CurrentIndex ?? 0);
            }
        }
        else if (now.Status.CurrentIndex != Status.CurrentIndex && Status.CurrentIndex is { } index)
        {
            await client.Player.JumpToAsync(PlayerId, index);
        }

        if (Status.QueueLength > 0)
        {
            await client.Player.SeekAsync(PlayerId, Status.Elapsed);
        }

        await client.Player.SetVolumeAsync(PlayerId, Status.Volume);
        await client.Player.SetRepeatAsync(PlayerId, Status.Repeat);
        await client.Player.SetShuffleAsync(PlayerId, Status.Shuffle);
        await client.Player.PauseAsync(PlayerId, Status.Mode != PlayMode.Play);
    }
}

/// <summary>Opt-in tests that change the real player. They restore it afterwards, but never run them while someone is listening.</summary>
public class MutatingIntegrationTests
{
    [MutatingFact]
    public async Task Volume_can_be_changed_and_is_restored()
    {
        using var client = Integration.CreateClient();
        var id = Integration.PlayerId!;
        var snapshot = await PlayerSnapshot.TakeAsync(client, id);
        try
        {
            var target = snapshot.Status.Volume > 20 ? snapshot.Status.Volume - 5 : snapshot.Status.Volume + 5;
            await client.Player.SetVolumeAsync(id, target);
            Assert.Equal(target, (await client.Player.GetStatusAsync(id, 0, 0, TrackTags.None)).Volume);
        }
        finally
        {
            await snapshot.RestoreAsync(client);
        }

        Assert.Equal(snapshot.Status.Volume, (await client.Player.GetStatusAsync(id, 0, 0, TrackTags.None)).Volume);
    }
}
