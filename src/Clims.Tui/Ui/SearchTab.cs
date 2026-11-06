using Clims.Tui.Data;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>Search results grouped as Artists (top 2), Albums (top 4), Tracks (top 10), each expandable.</summary>
public sealed class SearchTab : View
{
    private const int TopArtists = 2;
    private const int TopAlbums = 4;
    private const int TopTracks = 10;

    private readonly ILibrarySource _library;
    private readonly HashSet<string> _expanded = new();
    private SearchResults _results = new(Array.Empty<ArtistItem>(), Array.Empty<AlbumItem>(), Array.Empty<TrackItem>());
    private string _query = "";

    public SearchTab(ILibrarySource library)
    {
        _library = library;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
        List = new RowList { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        Add(List);
        List.RowActivated += OnActivated;
        Rebuild();
    }

    public RowList List { get; }

    public event Action<ArtistItem>? ArtistChosen;

    public event Action<AlbumItem, int>? PlayRequested;

    public void Query(string text)
    {
        _query = text.Trim();
        _expanded.Clear();
        _results = _library.Search(_query);
        Rebuild();
    }

    private void OnActivated(Row row)
    {
        switch (row.Data)
        {
            case ArtistItem artist:
                ArtistChosen?.Invoke(artist);
                break;
            case AlbumItem album:
                PlayRequested?.Invoke(album, 0);
                break;
            case TrackItem track:
                if (_library.GetAlbumOfTrack(track.Id) is { } owner)
                {
                    PlayRequested?.Invoke(owner, track.Number - 1);
                }

                break;
            case string section:
                var keep = List.SelectedIndex;
                _expanded.Add(section);
                Rebuild();
                List.SelectIndex(keep);
                break;
        }
    }

    private void Rebuild()
    {
        var rows = new List<Row>();
        if (_query.Length == 0)
        {
            rows.Add(new Row("Type in the search box (press / to focus it).", Kind: RowKind.More));
            List.Rows = rows;
            return;
        }

        Section(rows, "artists", "Artists", _results.Artists, TopArtists, a => new Row(a.Name, $"{a.AlbumCount} albums", Data: a));
        Section(rows, "albums", "Albums", _results.Albums, TopAlbums,
            a => new Row($"{a.Title}  ·  {a.Artist}", $"{a.Year}", Data: a));
        Section(rows, "tracks", "Tracks", _results.Tracks, TopTracks,
            t => new Row($"{t.Title}  ·  {t.Artist}  ·  {t.Album}", Fmt(t.Duration), Data: t));

        if (rows.Count == 0)
        {
            rows.Add(new Row($"No results for \"{_query}\".", Kind: RowKind.More));
        }

        List.Rows = rows;
    }

    private void Section<T>(List<Row> rows, string key, string title, IReadOnlyList<T> items, int top, Func<T, Row> map)
    {
        if (items.Count == 0)
        {
            return;
        }

        rows.Add(new Row($"{title} ({items.Count})", Kind: RowKind.Header));
        var shown = _expanded.Contains(key) ? items.Count : Math.Min(top, items.Count);
        rows.AddRange(items.Take(shown).Select(map));
        if (shown < items.Count)
        {
            rows.Add(new Row($"More {title.ToLowerInvariant()}… ({items.Count - shown})", Kind: RowKind.More, Data: key));
        }
    }

    private static string Fmt(TimeSpan t) => $"{t.Minutes:00}:{t.Seconds:00}";
}
