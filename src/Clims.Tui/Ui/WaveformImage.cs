using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>The waveform as pixels, for the Sixel style: thin bars growing up from the bottom, like the other styles.</summary>
public static class WaveformImage
{
    /// <param name="peaks">Peaks 0..1 over the whole track.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pitch">Pixels from one bar to the next (the last one is a gap).</param>
    /// <param name="playedPixels">Bars that start left of this pixel use <paramref name="done"/>.</param>
    public static Color[,] Render(IReadOnlyList<float> peaks, int width, int height, int pitch, int playedPixels, Color done, Color todo, Color background)
    {
        var pixels = new Color[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[x, y] = background;
            }
        }

        pitch = Math.Max(2, pitch);
        var bars = width / pitch;
        for (var bar = 0; bar < bars; bar++)
        {
            var x0 = bar * pitch;
            var barHeight = Math.Clamp((int)Math.Round(WaveformGlyphs.Peak(peaks, bar, bars) * height), 1, height);
            var colour = x0 < playedPixels ? done : todo;
            for (var y = height - barHeight; y < height; y++)
            {
                for (var x = x0; x < x0 + pitch - 1; x++)
                {
                    pixels[x, y] = colour;
                }
            }
        }

        return pixels;
    }
}
