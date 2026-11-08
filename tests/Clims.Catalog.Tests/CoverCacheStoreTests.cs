using Clims.Catalog;

namespace Clims.Catalog.Tests;

public sealed class CoverCacheStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Month = TimeSpan.FromDays(30);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clims-covers-" + Guid.NewGuid().ToString("N"));

    private CoverCacheStore Store() => new(CatalogPaths.CoversFor("abc", _folder));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void A_missing_file_is_a_miss_and_is_not_created_by_reading()
    {
        var store = Store();

        Assert.Null(store.Get("a1", 300, Month, T0));
        Assert.Equal(0, store.Count(300));
        Assert.False(File.Exists(store.Path));
    }

    [Fact]
    public void A_stored_cover_comes_back_unchanged_until_it_expires()
    {
        var store = Store();
        var bytes = new byte[] { 1, 2, 3, 255, 0 };
        store.Put("a1", 300, bytes, T0, Month);

        Assert.Equal(bytes, store.Get("a1", 300, Month, T0.AddDays(29)));
        Assert.Null(store.Get("a1", 300, Month, T0.AddDays(31)));
        Assert.Equal(0, store.Count(300)); // the expired row was removed when it was found
    }

    [Fact]
    public void Each_size_has_its_own_entry_and_its_own_ttl()
    {
        var store = Store();
        store.Put("a1", 300, new byte[] { 1 }, T0, TimeSpan.FromDays(30));
        store.Put("a1", 900, new byte[] { 2 }, T0, TimeSpan.FromDays(2));

        Assert.Equal(new byte[] { 1 }, store.Get("a1", 300, TimeSpan.FromDays(30), T0.AddDays(5)));
        Assert.Null(store.Get("a1", 900, TimeSpan.FromDays(2), T0.AddDays(5)));
    }

    [Fact]
    public void Putting_again_replaces_the_cover_and_restarts_its_clock()
    {
        var store = Store();
        store.Put("a1", 300, new byte[] { 1 }, T0, Month);
        store.Put("a1", 300, new byte[] { 9 }, T0.AddDays(20), Month);

        Assert.Equal(1, store.Count(300));
        Assert.Equal(new byte[] { 9 }, store.Get("a1", 300, Month, T0.AddDays(45)));
    }

    [Fact]
    public void Only_the_newest_entries_are_kept_when_there_is_a_limit()
    {
        var store = Store();
        for (var i = 0; i < 5; i++)
        {
            store.Put("a" + i, 900, new byte[] { (byte)i }, T0.AddMinutes(i), Month, maxEntries: 3);
        }

        Assert.Equal(3, store.Count(900));
        Assert.Null(store.Get("a0", 900, Month, T0.AddHours(1)));
        Assert.Null(store.Get("a1", 900, Month, T0.AddHours(1)));
        Assert.NotNull(store.Get("a4", 900, Month, T0.AddHours(1)));
    }

    [Fact]
    public void The_limit_applies_per_size()
    {
        var store = Store();
        store.Put("a1", 300, new byte[] { 1 }, T0, Month, maxEntries: 1);
        store.Put("a2", 300, new byte[] { 2 }, T0.AddMinutes(1), Month, maxEntries: 1);
        store.Put("b1", 900, new byte[] { 3 }, T0, Month, maxEntries: 1);

        Assert.Equal(1, store.Count(300));
        Assert.Equal(1, store.Count(900));
    }

    [Fact]
    public void Expired_entries_of_a_size_are_dropped_when_something_new_is_stored()
    {
        var store = Store();
        store.Put("old", 300, new byte[] { 1 }, T0, Month);
        store.Put("new", 300, new byte[] { 2 }, T0.AddDays(40), Month);

        Assert.Equal(1, store.Count(300));
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_just_a_miss()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, "this is not sqlite, it is long enough to look like a broken file ............................");

        Assert.Null(store.Get("a1", 300, Month, T0));
        store.Put("a1", 300, new byte[] { 1 }, T0, Month); // must not throw either
    }

    [Fact]
    public void The_cover_file_sits_next_to_the_catalog_with_its_own_name()
    {
        Assert.EndsWith("covers-7c1f32ca-018f.db", CatalogPaths.CoversFor("7c1f32ca-018f", "x"));
        Assert.EndsWith("covers-unknown.db", CatalogPaths.CoversFor("", "x"));
    }
}
