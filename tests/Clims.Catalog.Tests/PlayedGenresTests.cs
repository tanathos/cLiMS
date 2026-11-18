namespace Clims.Catalog.Tests;

public class PlayedGenresTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static PlayRecord Play(string title, int daysAgo = 0) => new(1, 1, Now.AddDays(-daysAgo), "Band", "Album", title, 1, 10);

    private static readonly Dictionary<string, string[]> Tags = new()
    {
        ["a"] = ["Death Metal", "Melodic Death Metal"],
        ["b"] = ["Death Metal"],
        ["c"] = ["Death Metal", "Melodic Death Metal", "Thrash Metal"],
        ["d"] = ["Folk"],
    };

    [Fact]
    public void GenresAreRankedAndEachCarriesTheGenresPlayedAlongWithIt()
    {
        var plays = new[] { Play("a"), Play("a"), Play("b"), Play("c"), Play("d") };

        var genres = PlayHistory.Genres(plays, DateTimeOffset.MinValue, p => Tags[p.Title]);

        Assert.Equal(["Death Metal", "Melodic Death Metal", "Folk", "Thrash Metal"], genres.Select(g => g.Genre));
        var death = genres[0];
        Assert.Equal(4, death.Plays);
        Assert.Equal([("Melodic Death Metal", 3), ("Thrash Metal", 1)], death.Together.Select(t => (t.Genre, t.Plays)));
        Assert.Empty(genres.Single(g => g.Genre == "Folk").Together);
    }

    [Fact]
    public void OnlyThePlaysSinceTheLimitCount()
    {
        var genres = PlayHistory.Genres([Play("b", daysAgo: 40), Play("d")], Now.AddDays(-30), p => Tags[p.Title]);

        Assert.Equal("Folk", Assert.Single(genres).Genre);
    }
}
