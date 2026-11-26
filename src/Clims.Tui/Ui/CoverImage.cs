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

    /// <summary>Width of the decoded picture in pixels.</summary>
    public int PixelWidth => _source.Width;

    /// <summary>Height of the decoded picture in pixels.</summary>
    public int PixelHeight => _source.Height;

    /// <summary>Wraps raw pixels (generated demo covers).</summary>
    public static CoverImage FromPixels(Color[,] pixels, string label)
    {
        var width = pixels.GetLength(0);
        var height = pixels.GetLength(1);
        var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = pixels[x, y];
                bitmap.SetPixel(x, y, new SKColor((byte)c.R, (byte)c.G, (byte)c.B));
            }
        }

        return new CoverImage(bitmap, label);
    }

    /// <summary>Decodes JPEG, PNG and friends; null when the bytes are not an image (SkiaSharp throws on those, we do not).</summary>
    public static CoverImage? Decode(byte[] data, string label)
    {
        try
        {
            var bitmap = SKBitmap.Decode(data);
            return bitmap is null ? null : new CoverImage(bitmap, label);
        }
        catch (ArgumentException)
        {
            return null;
        }
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

    // Rec. 709 luma: the same grey from each channel.
    private static readonly float[] LumaMatrix =
    [
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0,
    ];

    private CoverImage? _gray;

    /// <summary>
    /// The same picture in shades of grey, made on first use and kept with this one (and disposed with it). Its label differs, so
    /// anything cached by label (a sixel, the cells blanked under one) treats it as another image.
    /// </summary>
    public CoverImage Grayscale()
    {
        if (_gray is not null)
        {
            return _gray;
        }

        var bitmap = new SKBitmap(_source.Width, _source.Height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(LumaMatrix) })
        using (var image = SKImage.FromBitmap(_source))
        {
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(), paint);
        }

        return _gray = new CoverImage(bitmap, Label + "|gray");
    }

    private readonly Dictionary<(Color Color, int Width), CoverImage> _framed = new();

    /// <summary>
    /// The same picture with a frame drawn over its edge, made on first use and kept with this one. It keeps the label: the
    /// cells and the place of the picture are the same, only its pixels are sent again.
    /// </summary>
    /// <param name="color">Colour of the frame.</param>
    /// <param name="share">Width of the frame as a share of the picture's width.</param>
    public CoverImage Framed(Color color, double share)
    {
        var width = Math.Max(1, (int)Math.Round(_source.Width * share));
        if (_framed.TryGetValue((color, width), out var framed))
        {
            return framed;
        }

        var bitmap = new SKBitmap(_source.Width, _source.Height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = new SKColor((byte)color.R, (byte)color.G, (byte)color.B) })
        using (var image = SKImage.FromBitmap(_source))
        {
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions());
            float w = _source.Width, h = _source.Height;
            canvas.DrawRect(SKRect.Create(0, 0, w, width), paint);
            canvas.DrawRect(SKRect.Create(0, h - width, w, width), paint);
            canvas.DrawRect(SKRect.Create(0, 0, width, h), paint);
            canvas.DrawRect(SKRect.Create(w - width, 0, width, h), paint);
        }

        return _framed[(color, width)] = new CoverImage(bitmap, Label);
    }

    public void Dispose()
    {
        foreach (var framed in _framed.Values)
        {
            framed.Dispose();
        }

        _gray?.Dispose();
        _source.Dispose();
    }
}
