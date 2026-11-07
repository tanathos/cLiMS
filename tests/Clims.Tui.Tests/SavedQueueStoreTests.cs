using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Catalog;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

public class SavedQueueStoreTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "clims-queues-" + Guid.NewGuid().ToString("N"), "queues.json");

    private static SavedTrack T(string name) => new($"file:///{name}.flac", name, "Artist", "Album", 200);

    [Fact]
    public void Queues_survive_a_restart_newest_first()
    {
        var path = NewPath();
        var store = new SavedQueueStore(path);
        store.Save("Old", new[] { T("a") }, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        store.Save("New", new[] { T("b"), T("c") }, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var again = new SavedQueueStore(path).List();

        Assert.Equal(new[] { "New", "Old" }, again.Select(q => q.Name));
        Assert.Equal(new[] { "file:///b.flac", "file:///c.flac" }, again[0].Tracks.Select(t => t.Url));
    }

    [Fact]
    public void Saving_under_an_existing_name_replaces_it_ignoring_case()
    {
        var store = new SavedQueueStore(NewPath());
        store.Save("Mix", new[] { T("a") });
        store.Save("mix", new[] { T("b"), T("c") });

        var queue = Assert.Single(store.List());
        Assert.Equal(2, queue.Tracks.Count);
    }

    [Fact]
    public void Delete_removes_one_and_reports_whether_it_existed()
    {
        var store = new SavedQueueStore(NewPath());
        store.Save("Mix", new[] { T("a") });

        Assert.True(store.Delete("MIX"));
        Assert.False(store.Delete("Mix"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void A_damaged_file_is_moved_aside_and_the_list_starts_empty()
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        var store = new SavedQueueStore(path);

        Assert.Empty(store.List());
        Assert.True(File.Exists(path + ".bad"));
        store.Save("Fresh", new[] { T("a") });
        Assert.Single(store.List());
    }

    [Fact]
    public void A_name_is_required_and_trimmed()
    {
        var store = new SavedQueueStore(NewPath());
        Assert.Throws<ArgumentException>(() => store.Save("   ", new[] { T("a") }));
        store.Save("  Spaced  ", new[] { T("a") });
        Assert.Equal("Spaced", store.List()[0].Name);
    }

    [Fact]
    public void Unicode_names_and_titles_roundtrip()
    {
        var store = new SavedQueueStore(NewPath());
        store.Save("Música ♫ 日本", new[] { new SavedTrack("file:///é.flac", "Pantéon – 'quoted' \"x\"", "Motörhead", null, 1.5) });
        var q = Assert.Single(store.List());
        Assert.Equal("Música ♫ 日本", q.Name);
        Assert.Equal("Pantéon – 'quoted' \"x\"", q.Tracks[0].Title);
    }
}
