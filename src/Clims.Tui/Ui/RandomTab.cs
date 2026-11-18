using Clims.Catalog;
using Terminal.Gui.ViewBase;

namespace Clims.Tui.Ui;

/// <summary>
/// "Random albums" tab: the same album list as Recently added, holding a random pick of the library. Pressing 8 on the tab
/// (or opening it for the first time, or a new catalogue) picks again.
/// </summary>
public sealed class RandomTab : View
{
    /// <summary>How many albums the tab lists.</summary>
    public const int Count = 50;

    public const string Heading = "Random albums";

    private readonly Random _random;
    private MusicCatalog? _catalog;
    private string _emptyMessage = "Library not synced yet. Press F5 to sync.";
    private IReadOnlyList<CatalogAlbum> _picked = Array.Empty<CatalogAlbum>();

    public RandomTab(Random? random = null)
    {
        _random = random ?? Random.Shared;
        CanFocus = true;
        Albums = new AlbumsView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ShowArtist = true, Sortable = false };
        Add(Albums);
        Rebuild();
    }

    public AlbumsView Albums { get; }

    /// <summary>The catalogue changed (loaded, synced): pick again.</summary>
    public void SetCatalog(MusicCatalog? catalog, string emptyMessage)
    {
        _catalog = catalog;
        _emptyMessage = emptyMessage;
        Shuffle();
    }

    /// <summary>Picks another set of random albums.</summary>
    public void Shuffle()
    {
        _picked = _catalog is null ? Array.Empty<CatalogAlbum>() : Pick(_catalog.Albums, Count, _random);
        Rebuild();
    }

    /// <summary>Up to <paramref name="count"/> different albums chosen at random (a partial Fisher-Yates shuffle).</summary>
    public static IReadOnlyList<CatalogAlbum> Pick(IReadOnlyList<CatalogAlbum> all, int count, Random random)
    {
        var pool = all.ToArray();
        var take = Math.Min(count, pool.Length);
        for (var i = 0; i < take; i++)
        {
            var j = random.Next(i, pool.Length);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        return pool.AsSpan(0, take).ToArray();
    }

    private void Rebuild()
    {
        if (_catalog is null || _catalog.Albums.Count == 0)
        {
            Albums.SetMessage(_emptyMessage);
            return;
        }

        Albums.SetAlbums($"{Heading} · {_picked.Count} albums · press 8 to pick again", _picked, _catalog);
    }
}
