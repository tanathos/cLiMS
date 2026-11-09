using System.Diagnostics;
using Clims.Lms;

namespace Clims.Catalog.Tests;

public class RankingTests
{
    private static Catalog WithArtists(params string[] names) => new(
        Array.Empty<CatalogGenre>(),
        names.Select((n, i) => new CatalogArtist(i + 1, n)),
        Array.Empty<CatalogAlbum>(),
        Array.Empty<CatalogTrack>(),
        null);

    [Fact]
    public void Exact_beats_prefix_beats_word_start_beats_inside()
    {
        var c = WithArtists("Environment", "Cast Iron", "Iron Maiden", "Iron");
        Assert.Equal(new[] { "Iron", "Iron Maiden", "Cast Iron", "Environment" }, c.Search("iron").Artists.Items.Select(a => a.Name));
    }

    [Fact]
    public void Ties_go_to_the_shorter_name_then_alphabetical()
    {
        var c = WithArtists("Iron Longer Name", "Iron B", "Iron A");
        Assert.Equal(new[] { "Iron A", "Iron B", "Iron Longer Name" }, c.Search("iron").Artists.Items.Select(a => a.Name));
    }

    [Fact]
    public void Multi_word_queries_match_in_any_order_and_rank_by_how_well()
    {
        var c = WithArtists("Maiden Iron Tribute", "Iron Maiden", "Ironmaiden Fan Club");
        var names = c.Search("maiden iron").Artists.Items.Select(a => a.Name).ToList();
        Assert.Equal("Maiden Iron Tribute", names[0]);  // starts with the query text, in the order typed
        Assert.Equal("Iron Maiden", names[1]);          // every word starts a word, any order
        Assert.Equal("Ironmaiden Fan Club", names[2]);  // "iron" and "maiden" occur inside words: weakest tier
    }

    [Fact]
    public void A_match_only_through_the_artist_ranks_below_a_title_match()
    {
        var c = new Catalog(
            Array.Empty<CatalogGenre>(),
            new[] { new CatalogArtist(1, "Iron Maiden"), new CatalogArtist(2, "Other") },
            new[] { new CatalogAlbum(1, "Powerslave", 1, "Iron Maiden", 1984, null, false, 1), new CatalogAlbum(2, "Iron Sky", 2, "Other", 2012, null, false, 1) },
            Array.Empty<CatalogTrack>(),
            null);
        var albums = c.Search("iron").Albums.Items.Select(a => a.Title);
        Assert.Equal(new[] { "Iron Sky", "Powerslave" }, albums);
    }

    [Fact]
    public void A_title_match_by_the_matching_artist_is_boosted()
    {
        var c = new Catalog(
            Array.Empty<CatalogGenre>(),
            new[] { new CatalogArtist(1, "Iron Maiden"), new CatalogArtist(2, "Iron") },
            new[] { new CatalogAlbum(1, "A", 1, "Iron Maiden", 1984, null, false, 1), new CatalogAlbum(2, "B", 2, "Iron", 1984, null, false, 1) },
            new[]
            {
                new CatalogTrack(1, 1, "Iron Maiden", "Somebody Else", 9, 1, 1, TimeSpan.Zero, null),
                new CatalogTrack(2, 2, "Iron Maiden", "Iron Maiden", 1, 1, 1, TimeSpan.Zero, null),
            },
            null);
        Assert.Equal(new[] { 2, 1 }, c.Search("iron maiden").Tracks.Items.Select(t => t.Id));
    }

    [Fact]
    public void Totals_count_every_match_while_items_are_cut()
    {
        var c = WithArtists(Enumerable.Range(0, 30).Select(i => $"Band {i:00}").ToArray());
        var r = c.Search("band", maxArtists: 5);
        Assert.Equal(5, r.Artists.Items.Count);
        Assert.Equal(30, r.Artists.Total);
    }

    [Fact]
    public void Accents_and_case_are_ignored_both_ways()
    {
        var c = WithArtists("Édith Piaf", "Motörhead");
        Assert.Single(c.Search("EDITH").Artists.Items);
        Assert.Single(c.Search("motorhead").Artists.Items);
        Assert.Single(c.Search("MOTÖR").Artists.Items);
    }

    [Fact]
    public void Punctuation_in_names_and_queries_is_ignored()
    {
        var c = WithArtists("AC/DC", "Guns N' Roses");
        Assert.Single(c.Search("ac dc").Artists.Items);
        Assert.Single(c.Search("ac/dc").Artists.Items);
        Assert.Single(c.Search("guns n roses").Artists.Items);
    }

    [Fact]
    public void Local_search_over_twenty_thousand_tracks_is_fast()
    {
        var rnd = new Random(1);
        string Word() => new string(Enumerable.Range(0, rnd.Next(3, 9)).Select(_ => (char)('a' + rnd.Next(26))).ToArray());
        var albums = Enumerable.Range(1, 2000).Select(i => new CatalogAlbum(i, $"{Word()} {Word()}", 1, "Artist", 2000, null, false, 1)).ToList();
        var tracks = albums.SelectMany(a => Enumerable.Range(1, 10).Select(n =>
            new CatalogTrack(a.Id * 100 + n, a.Id, $"{Word()} {Word()} {Word()}", $"Artist {Word()}", 1, 1, n, TimeSpan.FromMinutes(3), "Rock"))).ToList();
        var c = new Catalog(Array.Empty<CatalogGenre>(), new[] { new CatalogArtist(1, "Artist") }, albums, tracks, null);
        Assert.Equal(20_000, c.TrackCount);

        _ = c.Search("e"); // builds the track index
        foreach (var query in new[] { "e", "a", "th", "abc def", "zzzz" })
        {
            // The fastest of several runs: a shared CI machine pauses a run now and then, which says nothing about the search.
            var best = double.MaxValue;
            for (var i = 0; i < 15; i++)
            {
                var sw = Stopwatch.StartNew();
                _ = c.Search(query);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            Assert.True(best < 100, $"'{query}' took {best:F1} ms at best");
        }
    }
}
