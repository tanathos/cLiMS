using Clims.Catalog;
using Clims.Tui.Ui;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

public class SearchTabTests
{
    private static readonly CatalogAlbum Album = new(1, "Powerslave", 1, "Iron Maiden", 1984, "art1", false, 1);

    private static MusicCatalog Catalog(params CatalogTrack[] tracks) => new(
        new[] { new CatalogGenre(1, "Metal") },
        new[] { new CatalogArtist(1, "Iron Maiden") },
        new[] { Album },
        tracks,
        null);

    private static CatalogTrack Track(int id, string title = "Aces High") =>
        new(id, 1, title, "Iron Maiden", 1, 1, id, TimeSpan.FromSeconds(272), "Metal");

    private static SearchOutcome Outcome(string query, RankedSearchResult result, SearchGroups server = SearchGroups.None, bool pending = false, string? note = null) =>
        new(query, result, server, pending, note);

    private static RankedSearchResult Result(
        IReadOnlyList<CatalogTrack>? tracks = null, int trackTotal = -1, IReadOnlyList<CatalogArtist>? artists = null, IReadOnlyList<CatalogGenre>? genres = null) =>
        new(
            new SearchGroup<CatalogArtist>(artists ?? Array.Empty<CatalogArtist>(), artists?.Count ?? 0),
            SearchGroup<CatalogAlbum>.None,
            new SearchGroup<CatalogTrack>(tracks ?? Array.Empty<CatalogTrack>(), trackTotal >= 0 ? trackTotal : tracks?.Count ?? 0),
            new SearchGroup<CatalogGenre>(genres ?? Array.Empty<CatalogGenre>(), genres?.Count ?? 0),
            new[] { "iron" });

    [Fact]
    public void A_group_shows_its_best_few_with_the_total_in_the_header_and_a_more_row()
    {
        var tab = new SearchTab();
        tab.SetCatalog(Catalog());
        var tracks = Enumerable.Range(1, 25).Select(i => Track(i, $"Song {i}")).ToList();

        tab.Show(Outcome("iron", Result(tracks, trackTotal: 29)));

        var items = tab.List.Items;
        Assert.Equal("Tracks (10 / 29)", Assert.Single(items, i => i.Kind == ResultKind.Header).Text);
        Assert.Equal(SearchTab.TopTracks, items.Count(i => i.Kind == ResultKind.Track));
        Assert.Equal("More tracks… (15)", Assert.Single(items, i => i.Kind == ResultKind.More).Text);
    }

    [Fact]
    public void Expanding_a_group_shows_everything_the_search_returned_and_keeps_the_query_state()
    {
        var tab = new SearchTab();
        tab.SetCatalog(Catalog());
        tab.Show(Outcome("iron", Result(Enumerable.Range(1, 25).Select(i => Track(i, $"Song {i}")).ToList())));

        var more = tab.List.Items.ToList().FindIndex(i => i.Kind == ResultKind.More);
        tab.List.SelectIndex(more);
        tab.List.ActivateSelected();

        Assert.Equal(25, tab.List.Items.Count(i => i.Kind == ResultKind.Track));
        Assert.DoesNotContain(tab.List.Items, i => i.Kind == ResultKind.More);

        // A refresh of the same query (the server's part arriving) keeps the group expanded; a new query collapses it.
        tab.Show(Outcome("iron", Result(Enumerable.Range(1, 25).Select(i => Track(i, $"Song {i}")).ToList())));
        Assert.Equal(25, tab.List.Items.Count(i => i.Kind == ResultKind.Track));
        tab.Show(Outcome("iron m", Result(Enumerable.Range(1, 25).Select(i => Track(i, $"Song {i}")).ToList())));
        Assert.Equal(SearchTab.TopTracks, tab.List.Items.Count(i => i.Kind == ResultKind.Track));
    }

    [Fact]
    public void A_pending_server_part_is_announced_and_a_note_is_shown()
    {
        var tab = new SearchTab();
        tab.Show(Outcome("iron", RankedSearchResult.Empty with { Words = new[] { "iron" } }, SearchGroups.Tracks, pending: true));
        Assert.Equal("Searching the server for tracks…", tab.List.Items[0].Text);
        Assert.DoesNotContain(tab.List.Items, i => i.Text.StartsWith("No results"));

        tab.Show(Outcome("iron", RankedSearchResult.Empty, SearchGroups.Tracks, note: "The server could not be reached."));
        Assert.Contains(tab.List.Items, i => i.Text == "The server could not be reached.");
        Assert.Contains(tab.List.Items, i => i.Text.StartsWith("No results"));
    }

    [Fact]
    public void An_empty_query_explains_how_to_search()
    {
        var tab = new SearchTab();
        tab.Show(Outcome("", RankedSearchResult.Empty));
        Assert.Contains("Type in the search box", Assert.Single(tab.List.Items).Text);
    }

    [Fact]
    public void Albums_tracks_and_artists_get_covers_and_icons_and_counts()
    {
        var tab = new SearchTab();
        tab.SetCatalog(Catalog(Track(1)));
        tab.Show(Outcome("iron", Result(new[] { Track(1) }, artists: new[] { new CatalogArtist(1, "Iron Maiden") })));

        var artist = Assert.Single(tab.List.Items, i => i.Kind == ResultKind.Artist);
        Assert.Equal("1 album", artist.Detail);
        var track = Assert.Single(tab.List.Items, i => i.Kind == ResultKind.Track);
        Assert.Equal("Iron Maiden  ·  Powerslave", track.Detail);
        Assert.Equal("04:32", track.Right);
        Assert.Equal(new CoverKey("art1", 1), track.Cover);
        Assert.Equal(2, track.Height);
    }

    [Fact]
    public void Headers_are_not_selectable_and_the_first_row_is_selected()
    {
        var tab = new SearchTab();
        tab.Show(Outcome("iron", Result(new[] { Track(1) })));
        Assert.Equal(ResultKind.Track, tab.List.SelectedItem!.Kind);
    }

    [Fact]
    public void A_refresh_of_the_same_query_keeps_the_selection_and_a_new_query_resets_it()
    {
        var tab = new SearchTab();
        var tracks = Enumerable.Range(1, 5).Select(i => Track(i, $"Song {i}")).ToList();
        tab.Show(Outcome("iron", Result(tracks)));
        tab.List.SelectIndex(3);

        tab.Show(Outcome("iron", Result(tracks, trackTotal: 7)));
        Assert.Equal(3, tab.List.SelectedIndex);

        tab.Show(Outcome("iron m", Result(tracks)));
        Assert.Equal(1, tab.List.SelectedIndex); // first track row (row 0 is the header)
    }

    [Fact]
    public void Playing_a_track_resolves_its_album_and_position()
    {
        var tab = new SearchTab();
        var all = new[] { Track(1, "A"), Track(2, "B"), Track(3, "C") };
        tab.SetCatalog(Catalog(all));
        (CatalogAlbum, IReadOnlyList<CatalogTrack>, int)? played = null;
        tab.PlayRequested += (a, t, i) => played = (a, t, i);

        tab.Show(Outcome("b", Result(new[] { all[1] })));
        Activate(tab);

        Assert.Equal(Album, played!.Value.Item1);
        Assert.Equal(3, played.Value.Item2.Count);
        Assert.Equal(1, played.Value.Item3);
    }

    [Fact]
    public void A_server_hit_whose_album_tracks_are_not_loaded_plays_on_its_own()
    {
        var tab = new SearchTab();
        tab.SetCatalog(Catalog()); // album known, tracks not loaded yet
        (CatalogAlbum, IReadOnlyList<CatalogTrack>, int)? played = null;
        tab.PlayRequested += (a, t, i) => played = (a, t, i);

        tab.Show(Outcome("aces", Result(new[] { Track(7, "Aces High") })));
        Activate(tab);

        Assert.Equal(7, Assert.Single(played!.Value.Item2).Id);
        Assert.Equal(0, played.Value.Item3);
    }

    [Fact]
    public void A_track_of_an_unknown_album_gives_a_notice_instead_of_playing()
    {
        var tab = new SearchTab();
        tab.SetCatalog(null);
        var played = false;
        string? notice = null;
        tab.PlayRequested += (_, _, _) => played = true;
        tab.Notice += n => notice = n;

        tab.Show(Outcome("aces", Result(new[] { Track(7, "Aces High") })));
        Activate(tab);

        Assert.False(played);
        Assert.Contains("not in the local library", notice);
    }

    [Fact]
    public void Queue_requests_carry_the_action_and_the_resolved_track()
    {
        var tab = new SearchTab();
        var all = new[] { Track(1, "A"), Track(2, "B") };
        tab.SetCatalog(Catalog(all));
        (QueueAction, int[])? asked = null;
        tab.QueueRequested += (action, _, tracks, _) => asked = (action, tracks.Select(t => t.Id).ToArray());

        tab.Show(Outcome("b", Result(new[] { all[1] })));
        tab.List.RequestQueue(QueueAction.PlayNext);

        // A track row queues that one track, not its album.
        Assert.Equal(QueueAction.PlayNext, asked!.Value.Item1);
        Assert.Equal(new[] { 2 }, asked.Value.Item2);
    }

    [Fact]
    public void Choosing_an_artist_or_genre_is_reported()
    {
        var tab = new SearchTab();
        CatalogArtist? artist = null;
        CatalogGenre? genre = null;
        tab.ArtistChosen += a => artist = a;
        tab.GenreChosen += g => genre = g;

        tab.Show(Outcome("iron", Result(artists: new[] { new CatalogArtist(1, "Iron Maiden") }, genres: new[] { new CatalogGenre(1, "Metal") })));
        Activate(tab);                           // first selectable row: the artist
        var genreRow = tab.List.Items.ToList().FindIndex(i => i.Kind == ResultKind.Genre);
        tab.List.SelectIndex(genreRow);
        Activate(tab);

        Assert.Equal("Iron Maiden", artist!.Name);
        Assert.Equal("Metal", genre!.Name);
    }

    private static void Activate(SearchTab tab) => tab.List.ActivateSelected();
}
