using Clims.Lms;

namespace Clims.Catalog.Tests;

public class AssessorTests
{
    private static ServerStatus Server(long? scan = 100, int tracks = 10, int albums = 5, int artists = 3, int genres = 2) =>
        new("9", "u", scan is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null, albums, artists, genres, tracks, TimeSpan.Zero, Array.Empty<PlayerInfo>());

    private static CatalogMeta Meta(long? scan = 100, int tracks = 10, int albums = 5, int artists = 3, int genres = 2) =>
        new("u", scan, tracks, albums, artists, genres, DateTimeOffset.UnixEpoch);

    [Fact]
    public void No_meta_means_no_index() => Assert.Equal(AssessmentKind.NoIndex, SyncAssessor.Assess(null, Server()).Kind);

    [Fact]
    public void Same_scan_and_counts_is_up_to_date() => Assert.Equal(AssessmentKind.UpToDate, SyncAssessor.Assess(Meta(), Server()).Kind);

    [Fact]
    public void A_moved_lastscan_is_a_change()
    {
        var a = SyncAssessor.Assess(Meta(scan: 100), Server(scan: 200));
        Assert.Equal(AssessmentKind.Changed, a.Kind);
        Assert.Contains("rescanned", a.Reason);
    }

    [Theory]
    [InlineData(11, 5, 3, 2, "tracks 10 to 11")]
    [InlineData(10, 6, 3, 2, "albums 5 to 6")]
    [InlineData(10, 5, 4, 2, "artists 3 to 4")]
    [InlineData(10, 5, 3, 3, "genres 2 to 3")]
    public void Count_changes_are_reported_with_numbers(int tracks, int albums, int artists, int genres, string expected)
    {
        var a = SyncAssessor.Assess(Meta(), Server(tracks: tracks, albums: albums, artists: artists, genres: genres));
        Assert.Equal(AssessmentKind.Changed, a.Kind);
        Assert.Contains(expected, a.Reason);
    }

    [Fact]
    public void Several_changes_are_all_listed()
    {
        var a = SyncAssessor.Assess(Meta(), Server(scan: 5, tracks: 99));
        Assert.Contains("rescanned", a.Reason);
        Assert.Contains("tracks 10 to 99", a.Reason);
    }

    [Fact]
    public void A_server_that_never_scanned_matches_an_index_that_never_saw_a_scan() =>
        Assert.Equal(AssessmentKind.UpToDate, SyncAssessor.Assess(Meta(scan: null), Server(scan: null)).Kind);
}
