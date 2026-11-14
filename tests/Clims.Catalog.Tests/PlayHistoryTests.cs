using Xunit;

namespace Clims.Catalog.Tests;

public class PlayHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static PlayRecord Play(int album, int track, double daysAgo, int albumTracks = 10, double hours = 0) =>
        new(track, album, Now.AddDays(-daysAgo).AddHours(hours), "Artist", $"Album {album}", $"Track {track}", track, albumTracks);

    [Fact]
    public void TracksAreTheOnesInTheWindowNewestFirst()
    {
        var plays = new[] { Play(1, 1, 5), Play(1, 2, 2), Play(1, 3, 1) };
        var tracks = PlayHistory.Tracks(plays, Now.AddDays(-3));
        Assert.Equal([3, 2], tracks.Select(t => t.TrackId));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    [InlineData(10, 5)]
    [InlineData(0, 1)]
    public void HalfAnAlbumRoundedUp(int tracks, int needed) => Assert.Equal(needed, PlayHistory.Needed(tracks));

    [Theory]
    [InlineData(10, 50, 5)]
    [InlineData(10, 75, 8)]
    [InlineData(10, 100, 10)]
    [InlineData(1, 100, 1)]
    public void TheThresholdIsAPercentageRoundedUp(int tracks, int percent, int needed) => Assert.Equal(needed, PlayHistory.Needed(tracks, percent));

    [Fact]
    public void AStricterThresholdNeedsMoreOfTheAlbum()
    {
        var plays = Enumerable.Range(1, 6).Select(t => Play(1, t, 1, hours: t / 10.0)).ToList();
        Assert.Single(PlayHistory.Albums(plays, Now.AddDays(-7), 50));
        Assert.Empty(PlayHistory.Albums(plays, Now.AddDays(-7), 75));
    }

    [Fact]
    public void FewerThanHalfTheTracksIsNotAListen()
    {
        var plays = Enumerable.Range(1, 4).Select(t => Play(1, t, 1, hours: t / 10.0)).ToList();
        Assert.Empty(PlayHistory.Albums(plays, Now.AddDays(-7)));
    }

    [Fact]
    public void HalfTheTracksIsOneListen()
    {
        var plays = Enumerable.Range(1, 5).Select(t => Play(1, t, 1, hours: t / 10.0)).ToList();
        var listen = Assert.Single(PlayHistory.Albums(plays, Now.AddDays(-7)));
        Assert.Equal(1, listen.InWindow);
        Assert.Equal(1, listen.Total);
    }

    [Fact]
    public void ATrackHeardAgainStartsTheNextRun()
    {
        // Tracks 1-5 (a listen), then 1-5 again (a second one): the repeat of track 1 would otherwise be swallowed.
        var plays = new List<PlayRecord>();
        for (var pass = 0; pass < 2; pass++)
        {
            for (var t = 1; t <= 5; t++)
            {
                plays.Add(Play(1, t, 2 - pass, hours: t / 10.0));
            }
        }

        var listen = Assert.Single(PlayHistory.Albums(plays, Now.AddDays(-7)));
        Assert.Equal(2, listen.InWindow);
    }

    [Fact]
    public void ListensBeforeTheWindowAreOnlyInTheTotal()
    {
        var plays = new List<PlayRecord>();
        foreach (var daysAgo in new[] { 20.0, 2.0 })
        {
            for (var t = 1; t <= 5; t++)
            {
                plays.Add(Play(1, t, daysAgo, hours: t / 10.0));
            }
        }

        var week = Assert.Single(PlayHistory.Albums(plays, Now.AddDays(-7)));
        Assert.Equal(1, week.InWindow);
        Assert.Equal(2, week.Total);
        Assert.Equal(2, Assert.Single(PlayHistory.Albums(plays, Now.AddDays(-30))).InWindow);
    }

    [Fact]
    public void AlbumsAreOrderedByTheirLatestListen()
    {
        var plays = new List<PlayRecord>();
        for (var t = 1; t <= 5; t++)
        {
            plays.Add(Play(1, t, 3, hours: t / 10.0));
            plays.Add(Play(2, t, 1, hours: t / 10.0));
        }

        Assert.Equal([2, 1], PlayHistory.Albums(plays, Now.AddDays(-7)).Select(a => a.AlbumId));
    }

    [Fact]
    public void APlayLoggedBeforeTheTracksWereKnownDoesNotLowerTheBar()
    {
        var plays = new List<PlayRecord> { Play(1, 1, 1, albumTracks: 0), Play(1, 2, 1, albumTracks: 10, hours: 0.1) };
        Assert.Empty(PlayHistory.Albums(plays, Now.AddDays(-7)));
    }

    [Fact]
    public void PlaysBeforeAndAfterARebuildOfTheServerLibraryCountTogether()
    {
        // The same album and tracks, but the server gave them new ids.
        var plays = new List<PlayRecord>();
        for (var t = 1; t <= 5; t++)
        {
            plays.Add(new PlayRecord(100 + t, 7, Now.AddDays(-2).AddHours(t / 10.0), "Artist", "Album", $"Track {t}", t, 10));
            plays.Add(new PlayRecord(900 + t, 42, Now.AddDays(-1).AddHours(t / 10.0), "ARTIST", "album", $"track {t}", t, 10));
        }

        var listen = Assert.Single(PlayHistory.Albums(plays, Now.AddDays(-7)));
        Assert.Equal(2, listen.Total);
        Assert.Equal(42, listen.AlbumId);
    }
}
