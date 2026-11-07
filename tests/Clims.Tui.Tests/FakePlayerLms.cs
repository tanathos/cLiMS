using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Catalog;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

/// <summary>An in-memory LMS player: state to poll, a log of every command, switches for outages and failures.</summary>
internal sealed class FakePlayerLms : ILmsClient, IPlayerApi, IQueueApi, IServerApi, ILibraryApi
{
    private readonly object _gate = new();

    public List<PlayerInfo> Players { get; } = new()
    {
        new PlayerInfo("aa:bb", "Pi", "squeezelite", "1.2.3.4", true, false, true, true),
    };

    public PlayMode Mode { get; set; } = PlayMode.Play;

    public TimeSpan Elapsed { get; set; } = TimeSpan.FromSeconds(30);

    public int Volume { get; set; } = 50;

    public int CurrentIndex { get; set; } = 1;

    public double QueueStamp { get; set; } = 1;

    public List<Track> Queue { get; } = new();

    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public Exception? Outage { get; set; }

    public Func<string, Exception?>? FailCommand { get; set; }

    /// <summary>Every command, as text: "Pause(True)", "Seek(40)", "Add(1,2)".</summary>
    public List<string> Commands { get; } = new();

    public int StatusCalls { get; private set; }

    public int QueueReads { get; private set; }

    public Dictionary<string, int> UrlToId { get; } = new();

    public Uri BaseUri { get; } = new("http://fake/");

    public ILmsRpcClient Rpc => throw new NotSupportedException();

    public ILibraryApi Library => this;

    public IPlayerApi Player => this;

    IQueueApi ILmsClient.Queue => this;

    public IServerApi Server => this;

    public static Track MakeTrack(int id, string title = "Song", int albumId = 7, string? url = null) =>
        new(id, title, "Artist", 3, "Album", albumId, id, 1, TimeSpan.FromMinutes(4), 1999, url ?? $"file:///music/{id}.flac", null, null, "art" + id, null, null, null, null, null, null);

    public void Fill(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            Queue.Add(MakeTrack(i, $"Song {i}"));
        }
    }

    private void Check(string command)
    {
        if (Outage is not null)
        {
            throw Outage;
        }

        lock (_gate)
        {
            Commands.Add(command);
        }

        if (FailCommand?.Invoke(command) is { } failure)
        {
            throw failure;
        }
    }

    // IServerApi
    public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("9.1.1");

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync(CancellationToken cancellationToken = default)
    {
        if (Outage is not null)
        {
            throw Outage;
        }

        return Task.FromResult<IReadOnlyList<PlayerInfo>>(Players.ToList());
    }

    public Task<ScanState> GetScanStateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> GetTrackCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> CanAsync(IReadOnlyList<string> command, CancellationToken cancellationToken = default) => Task.FromResult(true);

    // IPlayerApi
    public Task<PlayerStatus> GetStatusAsync(string playerId, int? start = null, int count = 1, TrackTags tags = TrackTags.List, CancellationToken cancellationToken = default)
    {
        if (Outage is not null)
        {
            throw Outage;
        }

        StatusCalls++;
        var items = Queue.Select((t, i) => new QueueItem(i, t)).Where(q => q.Index == CurrentIndex).ToList();
        return Task.FromResult(new PlayerStatus(
            "Pi", true, true, Mode, Elapsed, TimeSpan.FromMinutes(4), Volume, RepeatMode.Off, ShuffleMode.Off, true,
            Queue.Count == 0 ? null : CurrentIndex, Queue.Count, QueueStamp, items, Now()));
    }

    public Task PlayAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Play");
        Mode = PlayMode.Play;
        return Task.CompletedTask;
    }

    public Task PauseAsync(string playerId, bool? pause = null, CancellationToken cancellationToken = default)
    {
        Check($"Pause({pause})");
        Mode = pause == true ? PlayMode.Pause : PlayMode.Play;
        return Task.CompletedTask;
    }

    public Task StopAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Stop");
        return Task.CompletedTask;
    }

    public Task SeekAsync(string playerId, TimeSpan position, CancellationToken cancellationToken = default)
    {
        Check($"Seek({position.TotalSeconds:0})");
        Elapsed = position;
        return Task.CompletedTask;
    }

    public Task SeekByAsync(string playerId, TimeSpan delta, CancellationToken cancellationToken = default)
    {
        Check($"SeekBy({delta.TotalSeconds:0})");
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(string playerId, int volume, CancellationToken cancellationToken = default)
    {
        Check($"Volume({volume})");
        Volume = volume;
        return Task.CompletedTask;
    }

    public Task AdjustVolumeAsync(string playerId, int delta, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetMutedAsync(string playerId, bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task NextAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Next");
        return Task.CompletedTask;
    }

    public Task PreviousAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Previous");
        return Task.CompletedTask;
    }

    public Task JumpToAsync(string playerId, int index, CancellationToken cancellationToken = default)
    {
        Check($"Jump({index})");
        CurrentIndex = index;
        return Task.CompletedTask;
    }

    public Task SetRepeatAsync(string playerId, RepeatMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetShuffleAsync(string playerId, ShuffleMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetPowerAsync(string playerId, bool on, CancellationToken cancellationToken = default) => Task.CompletedTask;

    // IQueueApi
    public Task<Page<QueueItem>> GetQueueAsync(string playerId, int start = 0, int count = 100, TrackTags tags = TrackTags.List, CancellationToken cancellationToken = default)
    {
        QueueReads++;
        var items = Queue.Select((t, i) => new QueueItem(i, t)).Skip(start).Take(count).ToList();
        return Task.FromResult(new Page<QueueItem>(items, Queue.Count, start));
    }

    public Task LoadTracksAsync(string playerId, IReadOnlyList<int> trackIds, int playIndex = 0, CancellationToken cancellationToken = default)
    {
        Check($"Load({string.Join(',', trackIds)}@{playIndex})");
        return Task.CompletedTask;
    }

    public Task AddTracksAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken = default)
    {
        Check($"Add({string.Join(',', trackIds)})");
        OnAdd?.Invoke();
        return Task.CompletedTask;
    }

    public Action? OnAdd { get; set; }

    public Task InsertTracksNextAsync(string playerId, IReadOnlyList<int> trackIds, CancellationToken cancellationToken = default)
    {
        Check($"Next({string.Join(',', trackIds)})");
        return Task.CompletedTask;
    }

    public Task LoadAlbumAsync(string playerId, int albumId, int playIndex = 0, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AddAlbumAsync(string playerId, int albumId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task InsertAlbumNextAsync(string playerId, int albumId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string playerId, IReadOnlyCollection<int> indices, CancellationToken cancellationToken = default)
    {
        Check($"Remove({string.Join(',', indices)})");
        return Task.CompletedTask;
    }

    public Task MoveAsync(string playerId, int fromIndex, int toIndex, CancellationToken cancellationToken = default)
    {
        Check($"Move({fromIndex}->{toIndex})");
        return Task.CompletedTask;
    }

    public Task ClearAsync(string playerId, CancellationToken cancellationToken = default)
    {
        Check("Clear");
        return Task.CompletedTask;
    }

    // ILibraryApi
    public Task<Page<Artist>> GetArtistsAsync(ArtistQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<Album>> GetAlbumsAsync(AlbumQuery? query = null, int start = 0, int count = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<Track>> GetTracksAsync(TrackQuery query, int start = 0, int count = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Page<Genre>> GetGenresAsync(int start = 0, int count = 5000, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<SearchResults> SearchAsync(string term, int count = 10, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Track?> GetTrackAsync(int trackId, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Track?> GetTrackByUrlAsync(string url, TrackTags tags = TrackTags.All, CancellationToken cancellationToken = default) =>
        Task.FromResult(UrlToId.TryGetValue(url, out var id) ? MakeTrack(id) : null);

    public Task<Page<SavedPlaylist>> GetPlaylistsAsync(int start = 0, int count = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
