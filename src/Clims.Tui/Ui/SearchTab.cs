using Clims.Catalog;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>
/// Search results grouped as Artists, Albums, Tracks and Genres, each showing its best few with the count
/// ("Tracks (10 / 29)") and a "More" row that expands it. Results arrive from <see cref="ISearchService"/> (through the
/// main window) as local hits at once, followed by the server's part while the index is incomplete.
/// </summary>
public sealed class SearchTab : View
{
    /// <summary>How many hits each group shows before "More" (tuned by eye on the real library).</summary>
    public const int TopArtists = 2;

    /// <inheritdoc cref="TopArtists"/>
    public const int TopAlbums = 4;

    /// <inheritdoc cref="TopArtists"/>
    public const int TopTracks = 10;

    /// <inheritdoc cref="TopArtists"/>
    public const int TopGenres = 3;

    private readonly HashSet<string> _expanded = new();
    private MusicCatalog? _catalog;
    private SearchOutcome? _outcome;
    private string _query = "";

    public SearchTab()
    {
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
        List = new ResultsView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        Add(List);
        List.ItemActivated += OnActivated;
        List.QueueRequested += OnQueue;
        Rebuild(keepPosition: false);
    }

    public ResultsView List { get; }

    public event Action<CatalogArtist>? ArtistChosen;

    public event Action<CatalogGenre>? GenreChosen;

    public event Action<CatalogAlbum, IReadOnlyList<CatalogTrack>, int>? PlayRequested;

    public event Action<QueueAction, CatalogAlbum, IReadOnlyList<CatalogTrack>, int>? QueueRequested;

    /// <summary>Something to tell the user ("this album's tracks are still loading").</summary>
    public event Action<string>? Notice;

    /// <summary>The catalogue used to resolve ids (an album's tracks, album counts). The next outcome carries the matching results.</summary>
    public void SetCatalog(MusicCatalog? catalog) => _catalog = catalog;

    /// <summary>Shows a search outcome. A refresh of the same query keeps the selection and the expanded groups.</summary>
    public void Show(SearchOutcome outcome)
    {
        var same = outcome.Query == _query;
        _query = outcome.Query;
        _outcome = outcome;
        if (!same)
        {
            _expanded.Clear();
        }

        Rebuild(keepPosition: same);
    }

    private void OnActivated(ResultItem item)
    {
        switch (item.Data)
        {
            case CatalogArtist artist:
                ArtistChosen?.Invoke(artist);
                break;
            case CatalogGenre genre:
                GenreChosen?.Invoke(genre);
                break;
            case string group when item.Kind == ResultKind.More:
                var keep = List.SelectedIndex;
                _expanded.Add(group);
                Rebuild(keepPosition: true);
                List.SelectIndex(keep);
                break;
            default:
                if (Resolve(item) is { } target)
                {
                    PlayRequested?.Invoke(target.Album, target.Tracks, target.Index);
                }

                break;
        }
    }

    private void OnQueue(QueueAction action, ResultItem item)
    {
        // A track row queues that track; an album row queues the whole album.
        if (Resolve(item) is { } target)
        {
            var tracks = item.Data is CatalogTrack ? new[] { target.Tracks[target.Index] } : target.Tracks;
            QueueRequested?.Invoke(action, target.Album, tracks, 0);
        }
    }

    /// <summary>Album, its tracks and the index to start at, for an album or track row. Null (with a notice) when it cannot be resolved.</summary>
    private (CatalogAlbum Album, IReadOnlyList<CatalogTrack> Tracks, int Index)? Resolve(ResultItem item)
    {
        switch (item.Data)
        {
            case CatalogAlbum album:
                return (album, _catalog?.TracksOf(album.Id) ?? Array.Empty<CatalogTrack>(), 0);
            case CatalogTrack track:
            {
                if (_catalog?.Album(track.AlbumId) is not { } owner)
                {
                    Notice?.Invoke("This track's album is not in the local library yet; sync the library first.");
                    return null;
                }

                var tracks = _catalog.TracksOf(owner.Id);
                var index = IndexOf(tracks, track.Id);
                // A hit that came from the server while the album's tracks are still loading plays on its own.
                return index >= 0 ? (owner, tracks, index) : (owner, new[] { track }, 0);
            }

            default:
                return null;
        }
    }

    private static int IndexOf(IReadOnlyList<CatalogTrack> tracks, int id)
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private void Rebuild(bool keepPosition)
    {
        var items = new List<ResultItem>();
        List.Words = _outcome?.Result.Words ?? Array.Empty<string>();

        if (_query.Length == 0)
        {
            items.Add(new ResultItem(ResultKind.Info, "Type in the search box (press / to focus it)."));
            List.SetItems(items, keepPosition);
            return;
        }

        var outcome = _outcome!;
        var result = outcome.Result;
        if (outcome.ServerPending)
        {
            items.Add(new ResultItem(ResultKind.Info, "Searching the server for " + Describe(outcome.ServerGroups) + "…"));
        }

        if (outcome.Note is { } note)
        {
            items.Add(new ResultItem(ResultKind.Info, note));
        }

        Section(items, "artists", "Artists", result.Artists, TopArtists, a =>
        {
            var albums = _catalog?.AlbumCountOf(a.Id) ?? 0;
            return new ResultItem(ResultKind.Artist, a.Name, albums > 0 ? (albums == 1 ? "1 album" : $"{albums} albums") : "Artist", Data: a);
        });
        Section(items, "albums", "Albums", result.Albums, TopAlbums, a =>
            new ResultItem(ResultKind.Album, a.Title, a.Year > 0 ? $"{a.ArtistName}  ·  {a.Year}" : a.ArtistName, Cover: new CoverKey(a.ArtworkId, a.Id), Data: a));
        Section(items, "tracks", "Tracks", result.Tracks, TopTracks, t =>
        {
            // The index knows the album; a track that came from the server before the index has it brings its own details.
            var album = _catalog?.Album(t.AlbumId);
            TrackExtra? extra = null;
            result.TrackExtras?.TryGetValue(t.Id, out extra);
            var detail = string.Join("  ·  ", new[] { t.Artist, album?.Title ?? extra?.AlbumTitle }.Where(s => !string.IsNullOrEmpty(s)));
            return new ResultItem(ResultKind.Track, t.Title, detail, Fmt(t.Duration), new CoverKey(album?.ArtworkId ?? extra?.ArtworkId, t.AlbumId), t);
        });
        Section(items, "genres", "Genres", result.Genres, TopGenres, g =>
        {
            var albums = _catalog?.AlbumCountOfGenre(g.Name) ?? 0;
            return new ResultItem(ResultKind.Genre, g.Name, Right: albums > 0 ? $"{albums} albums" : "", Data: g);
        });

        if (result.IsEmpty && !outcome.ServerPending)
        {
            items.Add(new ResultItem(ResultKind.Info, $"No results for \"{_query}\"."));
        }

        List.SetItems(items, keepPosition);
    }

    private void Section<T>(List<ResultItem> items, string key, string title, SearchGroup<T> group, int top, Func<T, ResultItem> map)
    {
        if (group.Items.Count == 0)
        {
            return;
        }

        var shown = _expanded.Contains(key) ? group.Items.Count : Math.Min(top, group.Items.Count);
        var count = shown < group.Total ? $"{title} ({shown} / {group.Total})" : $"{title} ({group.Total})";
        items.Add(new ResultItem(ResultKind.Header, count));
        items.AddRange(group.Items.Take(shown).Select(map));
        if (shown < group.Items.Count)
        {
            items.Add(new ResultItem(ResultKind.More, $"More {title.ToLowerInvariant()}… ({group.Items.Count - shown})", Data: key));
        }
    }

    private static string Describe(SearchGroups groups) =>
        groups == SearchGroups.All ? "everything" : groups == SearchGroups.Tracks ? "tracks" : groups.ToString().ToLowerInvariant();

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
}
