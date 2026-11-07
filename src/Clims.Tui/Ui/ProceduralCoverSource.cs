using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Lms;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>Covers generated from the seed: for the demo mode (no server).</summary>
public sealed class ProceduralCoverSource : ICoverSource
{
    private readonly Dictionary<int, CoverImage> _cache = new();

    /// <inheritdoc />
    public event Action? Updated
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public CoverImage? TryGet(CoverKey key)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(key.Seed, out var image))
            {
                _cache[key.Seed] = image = ProceduralCovers.Create(key.Seed);
            }

            return image;
        }
    }

    /// <inheritdoc />
    public void Want(object owner, IReadOnlyCollection<CoverKey> keys)
    {
    }
}
