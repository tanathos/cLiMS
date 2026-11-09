using Clims.Catalog;
using Clims.Tui.Ui;
using Terminal.Gui.Input;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

public class GenresTabTests
{
    private const int Rock = 1;
    private const int Metal = 2;
    private const int Thrash = 3;

    private static CatalogTrack T(int id, string artist) => new(id, 10, $"T{id}", artist, null, 1, id, TimeSpan.FromMinutes(3), null);

    private static MusicCatalog Library() => new(
        new[] { new CatalogGenre(Rock, "Rock"), new CatalogGenre(Metal, "Metal"), new CatalogGenre(Thrash, "Thrash") },
        Array.Empty<CatalogArtist>(),
        new[] { new CatalogAlbum(10, "A", null, "Anthrax", 1990, null, false, 1) },
        new[] { T(1, "Anthrax"), T(2, "Anthrax"), T(3, "Beatles"), T(4, "Metallica") },
        null,
        genreTracks: new Dictionary<int, int[]>
        {
            [Rock] = new[] { 1, 2, 3 },
            [Metal] = new[] { 1, 2, 4 },
            [Thrash] = new[] { 1 },
        });

    private static GenresTab Tab()
    {
        var tab = new GenresTab(new Random(3));
        tab.SetCatalog(Library(), "empty");
        return tab;
    }

    private static string[] Names(RowList list) => list.Rows.Where(r => r.Kind == RowKind.Item).Select(r => r.Text).ToArray();

    [Fact]
    public void Without_a_catalogue_the_artists_area_says_why_and_there_are_no_columns()
    {
        var tab = new GenresTab();
        tab.SetCatalog(null, "Library not synced yet.");

        Assert.Empty(tab.Columns);
        Assert.Equal("Library not synced yet.", tab.Artists.Rows[0].Text);
    }

    [Fact]
    public void It_starts_with_every_genre_listed_and_nothing_chosen()
    {
        var tab = Tab();

        Assert.Single(tab.Columns);
        Assert.Equal(new[] { "Metal", "Rock", "Thrash" }, Names(tab.Columns[0]));
        Assert.Empty(tab.Chain);
        Assert.Equal("", tab.ChainText);
        Assert.Null(tab.Columns[0].SelectedRow);
        Assert.DoesNotContain(tab.Artists.Rows, r => r.Kind == RowKind.Item);
    }

    [Fact]
    public void Choosing_a_genre_adds_a_column_of_the_genres_that_go_with_it_and_lists_its_artists()
    {
        var tab = Tab();

        tab.Columns[0].SelectIndex(2); // row 0 is the heading; the items are Metal, Rock, Thrash

        Assert.Equal("Rock", tab.ChainText);
        Assert.Equal(2, tab.Columns.Count);
        Assert.Equal(new[] { "Metal", "Thrash" }, Names(tab.Columns[1]));
        Assert.Equal(new[] { "Anthrax", "Beatles" }, Names(tab.Artists));
    }

    [Fact]
    public void The_bottom_list_can_show_the_tracks_with_how_many_genres_each_carries()
    {
        var tab = Tab();
        tab.Columns[0].SelectIndex(2);   // Rock: tracks 1, 2, 3

        tab.ShowTracks(true);

        var rows = tab.Artists.Rows.Where(r => r.Kind == RowKind.Item).ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "3 genres", "2 genres", "1 genre" }, rows.Select(r => r.Right));
        Assert.All(rows, r => Assert.NotNull(r.Bold));

        tab.ShowTracks(false);
        Assert.Equal(new[] { "Anthrax", "Beatles" }, Names(tab.Artists));
    }

    [Fact]
    public void Each_choice_narrows_again_until_nothing_is_left_to_add()
    {
        var tab = Tab();
        tab.Columns[0].SelectIndex(2);   // Rock
        tab.Columns[1].SelectIndex(1);   // Metal
        Assert.Equal("Rock + Metal", tab.ChainText);
        Assert.Equal(3, tab.Columns.Count);
        Assert.Equal(new[] { "Thrash" }, Names(tab.Columns[2]));

        tab.Columns[2].SelectIndex(1);   // Thrash
        Assert.Equal("Rock + Metal + Thrash", tab.ChainText);
        Assert.Equal(3, tab.Columns.Count);                       // no genre goes further
        Assert.Equal(new[] { "Anthrax" }, Names(tab.Artists));
    }

    [Fact]
    public void Changing_an_earlier_column_drops_the_later_choices()
    {
        var tab = Tab();
        tab.Columns[0].SelectIndex(2);   // Rock
        tab.Columns[1].SelectIndex(1);   // Metal
        tab.Columns[0].SelectIndex(1);   // Metal instead of Rock

        Assert.Equal("Metal", tab.ChainText);
        Assert.Equal(new[] { "Rock", "Thrash" }, Names(tab.Columns[1]));
    }

    [Fact]
    public void Escape_clears_the_chain()
    {
        var tab = Tab();
        tab.Columns[0].SelectIndex(2);

        Assert.True(tab.HandleKey(Key.Esc));

        Assert.Empty(tab.Chain);
        Assert.Single(tab.Columns);
    }

    [Fact]
    public void A_random_queue_holds_only_matching_tracks_and_can_replace_or_append()
    {
        var tab = Tab();
        tab.Columns[0].SelectIndex(2);   // Rock: tracks 1, 2, 3
        (IReadOnlyList<(CatalogAlbum Album, CatalogTrack Track)> Items, bool Replace, string What)? got = null;
        tab.RandomRequested += (items, replace, what) => got = (items, replace, what);

        Assert.True(tab.HandleKey(Key.R.WithAlt));

        Assert.NotNull(got);
        Assert.True(got!.Value.Replace);
        Assert.Equal("Rock", got.Value.What);
        Assert.Equal(new[] { 1, 2, 3 }, got.Value.Items.Select(i => i.Track.Id).Order());   // 3 match, fewer than the 50 asked for
        Assert.All(got.Value.Items, i => Assert.Equal(10, i.Album.Id));

        Assert.True(tab.HandleKey(Key.Q.WithAlt));
        Assert.False(got.Value.Replace);
    }

    [Fact]
    public void The_size_steps_through_25_50_100_with_square_brackets()
    {
        var tab = Tab();

        Assert.Equal(50, tab.Size);
        tab.HandleKey(new Key(']'));
        Assert.Equal(100, tab.Size);
        tab.HandleKey(new Key(']'));
        Assert.Equal(100, tab.Size);
        tab.HandleKey(new Key('['));
        tab.HandleKey(new Key('['));
        Assert.Equal(25, tab.Size);
    }

    [Fact]
    public void A_catalogue_still_loading_tracks_shows_no_columns()
    {
        var loading = new MusicCatalog(
            new[] { new CatalogGenre(Rock, "Rock") },
            Array.Empty<CatalogArtist>(),
            Array.Empty<CatalogAlbum>(),
            Array.Empty<CatalogTrack>(),
            null,
            tracksLoaded: false);
        var tab = new GenresTab();
        tab.SetCatalog(loading, "empty");

        Assert.Empty(tab.Columns);
        Assert.Contains("Loading", tab.Artists.Rows[0].Text);
    }
}
