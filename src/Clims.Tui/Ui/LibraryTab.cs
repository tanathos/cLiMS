using Clims.Tui.Data;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

public sealed class LibraryTab : View
{
    private readonly ILibrarySource _library;

    public LibraryTab(ILibrarySource library)
    {
        _library = library;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        var rows = new List<Row> { new("All Artists", $"{library.GetAlbums(0).Count}", Data: new ArtistItem(0, "All Artists", 0)) };
        rows.AddRange(library.Artists.Select(a => new Row(a.Name, a.AlbumCount.ToString(), Data: a)));

        Artists = new RowList { X = 0, Y = 0, Width = 30, Height = Dim.Fill(), TypeAhead = true };
        Albums = new AlbumsView { X = 31, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        Add(Artists, Albums);

        Artists.SelectionChanged += row => Show((ArtistItem)row.Data!);
        Artists.Rows = rows;
        Show((ArtistItem)rows[0].Data!);
    }

    public RowList Artists { get; }

    public AlbumsView Albums { get; }

    public void SelectArtist(string name)
    {
        for (var i = 0; i < Artists.Rows.Count; i++)
        {
            if (Artists.Rows[i].Text == name)
            {
                Artists.SelectIndex(i);
                return;
            }
        }
    }

    private void Show(ArtistItem artist) => Albums.SetAlbums(artist.Name, _library.GetAlbums(artist.Id));
}
