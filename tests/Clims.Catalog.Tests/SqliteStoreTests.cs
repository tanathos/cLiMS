namespace Clims.Catalog.Tests;

public class SqliteStoreTests
{
    private static Catalog Sample(int tracks = 3)
    {
        var lms = FakeLms.WithLibrary(artists: 2, albumsPerArtist: 2, tracksPerAlbum: tracks);
        return Build.SyncedCatalog(lms);
    }

    [Fact]
    public void A_missing_file_loads_as_nothing()
    {
        using var dir = new TempFolder();
        Assert.Null(new SqliteCatalogStore(dir.File("none.db")).Load());
    }

    [Fact]
    public void Save_then_load_roundtrips_everything()
    {
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("a.db"));
        var original = Sample();
        store.Save(original);

        var loaded = store.Load()!;
        Assert.NotNull(loaded);
        Assert.Equal(original.Genres, loaded.Genres);
        Assert.Equal(original.Artists, loaded.Artists);
        Assert.Equal(original.Albums.OrderBy(a => a.Id), loaded.Albums.OrderBy(a => a.Id));
        Assert.Equal(original.SnapshotTracks().OrderBy(t => t.Id), loaded.SnapshotTracks().OrderBy(t => t.Id));
        Assert.Equal(original.Meta, loaded.Meta);
        Assert.True(loaded.TracksLoaded);
    }

    [Fact]
    public void Server_order_of_artists_survives()
    {
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("order.db"));
        var c = new Catalog(
            Array.Empty<CatalogGenre>(),
            new[] { new CatalogArtist(9, "Zed"), new CatalogArtist(1, "Alpha"), new CatalogArtist(5, "Mid") },
            Array.Empty<CatalogAlbum>(),
            Array.Empty<CatalogTrack>(),
            new CatalogMeta("u", null, 0, 0, 3, 0, DateTimeOffset.UnixEpoch));
        store.Save(c);
        Assert.Equal(new[] { 9, 1, 5 }, store.Load()!.Artists.Select(a => a.Id));
    }

    [Fact]
    public void Nullable_and_unicode_fields_survive()
    {
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("n.db"));
        var c = new Catalog(
            new[] { new CatalogGenre(1, "Rock & Roll") },
            new[] { new CatalogArtist(1, "Édith Piaf 日本") },
            new[] { new CatalogAlbum(1, "10 Años", null, "Various", 0, null, true, null) },
            new[] { new CatalogTrack(1, 1, "Ünï", null, null, 1, 0, TimeSpan.FromMilliseconds(1234), null) },
            new CatalogMeta("u", null, 1, 1, 1, 1, DateTimeOffset.UnixEpoch));
        store.Save(c);

        var l = store.Load()!;
        Assert.Equal("Édith Piaf 日本", l.Artists[0].Name);
        var album = l.Albums[0];
        Assert.Null(album.ArtistId);
        Assert.Null(album.ArtworkId);
        Assert.Null(album.DiscCount);
        Assert.True(album.IsCompilation);
        Assert.Equal(TimeSpan.FromMilliseconds(1234), l.TracksOf(1)[0].Duration);
        Assert.Null(l.Meta!.LastScan);
    }

    [Fact]
    public void Saving_again_replaces_the_old_content()
    {
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("r.db"));
        store.Save(Sample(tracks: 3));
        store.Save(Sample(tracks: 1));
        Assert.Equal(4, store.Load()!.TrackCount);
    }

    [Fact]
    public void A_catalogue_without_meta_cannot_be_saved_and_leaves_the_old_index_alone()
    {
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("m.db"));
        var good = Sample();
        store.Save(good);

        Assert.Throws<ArgumentException>(() => store.Save(Catalog.Empty));
        Assert.Equal(good.Albums.Count, store.Load()!.Albums.Count);
    }

    [Fact]
    public void A_garbage_file_loads_as_nothing_instead_of_throwing()
    {
        using var dir = new TempFolder();
        var path = dir.File("junk.db");
        File.WriteAllText(path, "this is not a database");
        Assert.Null(new SqliteCatalogStore(path).Load());
    }

    [Fact]
    public void A_different_schema_version_is_ignored_so_it_gets_rebuilt()
    {
        using var dir = new TempFolder();
        var path = dir.File("v.db");
        var store = new SqliteCatalogStore(path);
        store.Save(Sample());

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value='999' WHERE key='schema_version'";
            cmd.ExecuteNonQuery();
        }

        Assert.Null(store.Load());
    }

    [Fact]
    public void Replacing_one_albums_tracks_is_persisted_without_touching_the_others()
    {
        using var dir = new TempFolder();
        var store = new SqliteCatalogStore(dir.File("t.db"));
        var c = Sample(tracks: 2);
        store.Save(c);
        var albumId = c.Albums[0].Id;
        var other = c.Albums[1].Id;

        store.ReplaceAlbumTracks(albumId, new[] { new CatalogTrack(777, albumId, "New", null, null, 1, 1, TimeSpan.FromSeconds(5), "Jazz", "file:///x.flac") });

        var l = store.Load()!;
        var t = Assert.Single(l.TracksOf(albumId));
        Assert.Equal("file:///x.flac", t.Url);
        Assert.Equal(2, l.TracksOf(other).Count);
    }

    [Fact]
    public void Replacing_tracks_before_the_first_save_is_a_no_op_that_does_not_create_a_file()
    {
        using var dir = new TempFolder();
        var path = dir.File("early.db");
        new SqliteCatalogStore(path).ReplaceAlbumTracks(1, Array.Empty<CatalogTrack>());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_creates_the_folder_and_the_file_can_be_deleted_afterwards()
    {
        using var dir = new TempFolder();
        var path = Path.Combine(dir.Path, "sub", "deep", "c.db");
        new SqliteCatalogStore(path).Save(Sample());
        Assert.True(File.Exists(path));
        File.Delete(path); // pooling is off, so the file is not locked
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Cache_file_is_named_after_the_server_uuid_and_sanitised()
    {
        Assert.EndsWith("catalog-7c1f32ca-018f.db", CatalogPaths.For("7c1f32ca-018f", "x"));
        Assert.EndsWith("catalog-abc.db", CatalogPaths.For("a/b\\c:", "x"));
        Assert.EndsWith("catalog-unknown.db", CatalogPaths.For("", "x"));
    }
}
