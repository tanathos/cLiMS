using Xunit;

namespace Clims.Catalog.Tests;

public class PlayHistoryStoreTests
{
    [Fact]
    public void PlaysComeBackInOrderWithTheirNames()
    {
        using var folder = new TempFolder();
        var store = new PlayHistoryStore(folder.File("history.db"));
        var at = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
        store.Add(new PlayRecord(2, 7, at.AddMinutes(5), "Artist", "Album", "Second", 2, 9));
        store.Add(new PlayRecord(1, 7, at, "Artist", "Album", "First", 1, 9));

        var all = store.All();
        Assert.Equal(["First", "Second"], all.Select(p => p.Title));
        Assert.Equal(new PlayRecord(1, 7, at, "Artist", "Album", "First", 1, 9), all[0]);
    }

    [Fact]
    public void NoFileMeansNoPlays()
    {
        using var folder = new TempFolder();
        Assert.Empty(new PlayHistoryStore(folder.File("none.db")).All());
    }

    [Fact]
    public void ASecondStoreOnTheSameFileSeesTheLog()
    {
        using var folder = new TempFolder();
        new PlayHistoryStore(folder.File("h.db")).Add(new PlayRecord(1, 1, DateTimeOffset.UtcNow, "A", "B", "C", 1, 1));
        Assert.Single(new PlayHistoryStore(folder.File("h.db")).All());
    }

    [Fact]
    public void TheLogHasItsOwnFileName() =>
        Assert.StartsWith("history-abc", Path.GetFileName(CatalogPaths.HistoryFor("abc", "x")));
}
