using Clims.Lms;

namespace Clims.Tui.Tests;

/// <summary>A plugin browser with a small fixed tree: Spotty (Albums → one album), a radio search, and what was played.</summary>
public sealed class FakeOnlineApi : IOnlineApi
{
    public List<string> Played { get; } = [];

    public List<string> Browsed { get; } = [];

    public Task<IReadOnlyList<OnlineApp>> GetAppsAsync(string playerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OnlineApp>>(
        [
            new OnlineApp("Local Radio", "local", true, false),
            new OnlineApp("Spotty", "spotty", false, false),
            new OnlineApp("Station search", "search", true, true),
        ]);

    public Task<OnlineListing> BrowseAsync(string playerId, string command, string? itemId, string? search, int start, int count, CancellationToken cancellationToken = default)
    {
        Browsed.Add($"{command}/{itemId}/{search}/{start}");
        var items = (command, itemId, search) switch
        {
            ("spotty", null, null) => new[]
            {
                new OnlineItem("0", "Search", "search", false, true),
                new OnlineItem("2", "Albums", "link", false, true),
                new OnlineItem("3", "Liked Songs", "link", false, true),
            },
            ("spotty", "3", null) =>
            [
                new OnlineItem("3.0", "One", "audio", true, true),
                new OnlineItem("3.1", "Two", "audio", true, true),
                new OnlineItem("3.2", "Three", "audio", true, true),
            ],
            ("spotty", "2", null) => [new OnlineItem("2.0", "Satellites by Mew", "playlist", true, true)],
            ("spotty", "0", "mew") => [new OnlineItem("0.0", "Artists", "link", false, true)],
            ("search", null, "jazz") => [new OnlineItem("9", "Jazz FM", "audio", true, false)],
            _ => Array.Empty<OnlineItem>(),
        };
        if (command == "spotty" && itemId == "3" && search is null && LikedCount > 3)
        {
            var page = Enumerable.Range(start, Math.Max(0, Math.Min(count, LikedCount - start)))
                .Select(i => new OnlineItem("3." + i, i switch { 0 => "One", 1 => "Two", 2 => "Three", _ => "Track " + i }, "audio", true, true))
                .ToArray();
            return Task.FromResult(new OnlineListing("", page, LikedCount, start));
        }

        return Task.FromResult(new OnlineListing("", items, items.Length, start));
    }

    /// <summary>When above 3, the "Liked Songs" folder has this many tracks, served page by page.</summary>
    public int LikedCount { get; set; }

    /// <summary>What the player's queue holds; the fake does not queue by itself, tests set it to pretend the server did.</summary>
    public int QueueLength { get; set; }

    /// <summary>When set, a play or add of a whole folder (item "3") makes the fake server queue its three tracks, as a plugin may.</summary>
    public bool FolderQueuesAll { get; set; }

    public Task<int> QueueLengthAsync(string playerId, CancellationToken cancellationToken = default) => Task.FromResult(QueueLength);

    public Task PlayAsync(string playerId, string command, string itemId, OnlineAction action, CancellationToken cancellationToken = default)
    {
        lock (Played)
        {
            Played.Add($"{command} {action} {itemId}");
        }

        if (FolderQueuesAll && itemId == "3")
        {
            QueueLength += 3;
        }

        return Task.CompletedTask;
    }
}
