using SkiaSharp;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>A decoded cover that can be sampled onto a half-block cell grid. Resized pixels are cached per grid size.</summary>
public sealed class CoverImage : IDisposable
{
    private readonly SKBitmap _source;
    private readonly Dictionary<(int Cols, int PixelRows), Color[]> _grids = new();

    private CoverImage(SKBitmap source, string label)
    {
        _source = source;
        Label = label;
    }

    public string Label { get; }

    public static CoverImage? Decode(byte[] data, string label)
    {
        var bitmap = SKBitmap.Decode(data);
        return bitmap is null ? null : new CoverImage(bitmap, label);
    }

    /// <summary>Colour of pixel (x, y) when the cover is scaled to <paramref name="cols"/> x <paramref name="pixelRows"/>.</summary>
    public Color Pixel(int cols, int pixelRows, int x, int y)
    {
        if (!_grids.TryGetValue((cols, pixelRows), out var grid))
        {
            grid = Resample(cols, pixelRows);
            _grids[(cols, pixelRows)] = grid;
        }

        return grid[y * cols + x];
    }

    /// <summary>The cover resampled to width x height as a 2-D array, the input format of <c>SixelEncoder</c>.</summary>
    public Color[,] Bitmap(int width, int height)
    {
        var flat = _grids.TryGetValue((width, height), out var cached) ? cached : Resample(width, height);
        _grids[(width, height)] = flat;
        var grid = new Color[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                grid[x, y] = flat[y * width + x];
            }
        }

        return grid;
    }

    private Color[] Resample(int cols, int pixelRows)
    {
        // Mitchell keeps small covers (16x16 px) readable; plain nearest-neighbour aliases badly from 192 px.
        using var scaled = _source.Resize(new SKImageInfo(cols, pixelRows), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Cover resize failed.");
        var grid = new Color[cols * pixelRows];
        for (var y = 0; y < pixelRows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var c = scaled.GetPixel(x, y);
                grid[y * cols + x] = Theme.Rgb(c.Red, c.Green, c.Blue);
            }
        }

        return grid;
    }

    public void Dispose() => _source.Dispose();
}
