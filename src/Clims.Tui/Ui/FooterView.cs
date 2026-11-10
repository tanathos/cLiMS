using Clims.Catalog;
using Clims.Tui.Data;
using Clims.Waveform;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Always-visible footer: seek bar on top, cover and waveform on the left, transport buttons and volume.
/// Redrawn at about 10 fps by the main window's timer, so everything here must stay cheap.
/// </summary>
public sealed class FooterView : PaintView
{
    public const int Rows = 6;

    private const int CoverCols = 12;
    private const int CoverRows = 6;
    private const string Blocks = " ▁▂▃▄▅▆▇█";

    private readonly IPlayerController _player;
    private readonly WaveformService? _waves;
    private readonly Dictionary<int, float[]> _peaks = new();
    private readonly List<(int X0, int X1, Action Run)> _buttons = new();
    private (int X0, int X1) _seek;
    private (int X0, int X1) _volume;

    /// <summary>Creates the footer. With no <paramref name="waves"/> (demo mode) a generated waveform is drawn.</summary>
    public FooterView(IPlayerController player, WaveformService? waves = null)
    {
        _player = player;
        _waves = waves;
    }

    private double Fraction(CatalogTrack track) =>
        track.Duration <= TimeSpan.Zero ? 0 : Math.Clamp(_player.Elapsed / track.Duration, 0, 1);

    /// <summary>Set by the main window when the terminal is smaller than 100x30: drop cover/waveform, keep controls.</summary>
    public bool ShowCover { get; set; } = true;

    /// <summary>The cover cells never change between frames, so the sixel is not re-sent every frame (that flickered).</summary>
    protected override bool ResendSixelsOnPaint => false;

    protected override bool TracksHover => true;

    protected override int ClearFromX => ShowCover ? CoverCols : 0;

    public bool ShowWaveform { get; set; } = true;

    protected override void Paint()
    {
        _buttons.Clear();
        const string hint = "Space play/pause  ←/→ ±10 s  Alt+n/p track  +/- volume  / search  1-5 tabs  ? help  F5 sync  F6 library view  Ctrl+Q quit";
        if (_player.Current is not { } now)
        {
            _seek = (0, 0);
            Put(1, 0, Clip(_player.Problem ?? "Nothing playing", W - 2), _player.Problem is null ? Theme.Dim : new Attribute(Theme.Warn, Theme.Bg));
            PaintControls(1);
            Put(1, 5, Clip(hint, W - 2), Theme.Dim);
            return;
        }

        var track = now.Track;
        var album = now.Album;
        PaintSeekBar(track, ShowCover ? CoverCols + 1 : 1);

        var x0 = 1;
        if (ShowCover)
        {
            DrawCover(0, 0, CoverCols, CoverRows, new CoverKey(album.ArtworkId, album.Id));
            x0 = CoverCols + 2;
        }

        PaintTitle(now, x0, W - x0 - 2);

        if (ShowWaveform)
        {
            if (ShowSpectrum)
            {
                PaintSpectrum(track, x0, 2, W - x0 - 2);
            }
            else
            {
                PaintWaveform(track, x0, 2, W - x0 - 2);
            }
        }

        PaintControls(x0);
        Put(x0, 5, Clip(hint, W - x0 - 1), Theme.Dim);
    }

    /// <summary>The user clicked the cover: show the playing album full screen.</summary>
    public event Action? CoverClicked;

    /// <summary>The user clicked the artist of the playing track: show that artist in the library.</summary>
    public event Action<CatalogAlbum>? ArtistClicked;

    /// <summary>The user clicked the album of the playing track: show it in the library, scrolled into view.</summary>
    public event Action<CatalogAlbum>? AlbumClicked;

    private (int X0, int X1) _artistBox;
    private (int X0, int X1) _albumBox;

    /// <summary>"artist · album · track"; artist and album are links (underlined, clickable).</summary>
    private void PaintTitle(NowPlaying now, int x, int width)
    {
        const string sep = "  ·  ";
        var link = new Attribute(Theme.Fg, Theme.Bg, TextStyle.Underline);
        var linkHover = new Attribute(Theme.HoverFg, Theme.Bg, TextStyle.Underline);
        var artist = now.Track.Artist ?? now.Album.ArtistName;
        var end = x + width;

        _artistBox = _albumBox = (0, 0);
        var cx = x;
        var s = Clip(artist, Math.Max(0, end - cx));
        var next = Put(cx, 1, s, Over(cx, cx + s.Length, 1) ? linkHover : link);
        _artistBox = (cx, next);
        cx = next;
        if (cx >= end)
        {
            return;
        }

        cx = Put(cx, 1, Clip(sep, end - cx), Theme.Dim);
        s = Clip(now.Album.Title, Math.Max(0, end - cx));
        next = Put(cx, 1, s, Over(cx, cx + s.Length, 1) ? linkHover : link);
        _albumBox = (cx, next);
        cx = next;
        if (cx >= end)
        {
            return;
        }

        cx = Put(cx, 1, Clip(sep, end - cx), Theme.Dim);
        // "(3/12)": position in the queue, right-aligned on the same line.
        var count = _player.QueueIndex >= 0 && _player.Queue.Count > 0 ? $"({_player.QueueIndex + 1}/{_player.Queue.Count})" : "";
        var room = Math.Max(0, end - cx - (count.Length > 0 ? count.Length + 2 : 0));
        Put(cx, 1, Clip(now.Track.Title, room), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Bold));
        if (count.Length > 0 && end - cx >= count.Length + 2)
        {
            Put(end - count.Length, 1, count, Theme.Dim);
        }
    }

    private void PaintSeekBar(CatalogTrack track, int left)
    {
        var elapsed = Fmt(_player.Elapsed);
        var total = Fmt(track.Duration);
        var x0 = left + elapsed.Length + 1;
        var x1 = W - total.Length - 2;
        _seek = (x0, x1);
        Put(left, 0, elapsed, Theme.Normal);
        Put(x1 + 1, 0, total, Theme.Dim);

        var width = Math.Max(1, x1 - x0);
        var filled = (int)(width * Fraction(track));
        Put(x0, 0, new string('━', filled), new Attribute(Theme.Accent, Theme.Bg));
        Put(x0 + filled, 0, new string('─', width - filled), Theme.Dim);
    }

    /// <summary>F7: draw a spectrum analyzer in place of the waveform.</summary>
    public bool ShowSpectrum { get; set; }

    private int _spectrumTrack = -1;
    private int _spectrumFrame = -1;

    /// <summary>
    /// Added to the playhead when picking the spectrum frame. The position LMS reports is when the server last heard from the
    /// player, and the audio takes a little longer to reach the speakers, so a few hundred ms may be needed either way.
    /// </summary>
    public TimeSpan SpectrumOffset { get; set; }

    private readonly float[] _smooth = new float[SpectrumBands];
    private const int SpectrumBands = 32;

    /// <summary>
    /// Bars for the frame of the spectrum that is playing now (precomputed from the file, so it follows the playhead rather than
    /// the speakers). Rises at once and falls a little each frame, which is what makes it look alive at 10 fps.
    /// </summary>
    private void PaintSpectrum(CatalogTrack track, int x, int y, int width)
    {
        if (width < 8)
        {
            return;
        }

        var target = new float[SpectrumBands];
        if (_waves is null)
        {
            var t = _player.Elapsed.TotalSeconds;
            for (var b = 0; b < SpectrumBands; b++)
            {
                target[b] = (0.55f + 0.45f * MathF.Sin((float)t * (1.3f + b * 0.21f) + b * 1.7f)) * (1 - b / 60f);
            }
        }
        else if (_waves.TrySpectrum(track.Id) is { } spectrum)
        {
            var now = spectrum.IndexAt(_player.Elapsed + SpectrumOffset);
            // The footer repaints at about 10 fps and a frame lasts 50 ms: take the loudest of every frame passed since the
            // last repaint, otherwise a beat that falls between two repaints is never drawn.
            var from = _spectrumTrack == track.Id && _spectrumFrame is >= 0 && now > _spectrumFrame && now - _spectrumFrame <= 6 ? _spectrumFrame + 1 : now;
            _spectrumTrack = track.Id;
            _spectrumFrame = now;
            for (var i = Math.Max(0, from); i <= now; i++)
            {
                var frame = spectrum.Frame(i);
                for (var b = 0; b < SpectrumBands && b < frame.Length; b++)
                {
                    target[b] = Math.Max(target[b], frame[b] / 255f);
                }
            }
        }
        else
        {
            PaintPending(track, x, y, width, "spectrum");
            return;
        }

        for (var b = 0; b < SpectrumBands; b++)
        {
            _smooth[b] = Math.Max(target[b], _smooth[b] - 0.12f);
        }

        var top = new char[width];
        var bottom = new char[width];
        for (var i = 0; i < width; i++)
        {
            var at = i * (SpectrumBands - 1) / (float)Math.Max(1, width - 1);
            var lo = (int)at;
            var hi = Math.Min(SpectrumBands - 1, lo + 1);
            var level = (int)Math.Round((_smooth[lo] + (_smooth[hi] - _smooth[lo]) * (at - lo)) * 16);
            bottom[i] = Blocks[Math.Clamp(level, 0, 8)];
            top[i] = level > 8 ? Blocks[Math.Min(level - 8, 8)] : ' ';
        }

        var bars = new Attribute(Theme.Mix(Theme.Accent, Theme.Bg, 0.2), Theme.Bg);
        Put(x, y, new string(top), bars);
        Put(x, y + 1, new string(bottom), bars);
    }

    /// <summary>Says why the waveform or spectrum is not drawn yet instead of leaving a hole.</summary>
    private void PaintPending(CatalogTrack track, int x, int y, int width, string what)
    {
        var note = _waves!.StateOf(track.Id) switch
        {
            WaveformState.Unsupported => $"No {what}: " + (_waves.ErrorOf(track.Id) ?? "unsupported format"),
            WaveformState.Failed => $"No {what}: " + (_waves.ErrorOf(track.Id) ?? "download failed") + " (retrying later)",
            _ => $"◐ Analysing {what}…",
        };
        Put(x, y, Clip(note, width), Theme.Dim);
    }

    private void PaintWaveform(CatalogTrack track, int x, int y, int width)
    {
        if (width < 8)
        {
            return;
        }

        float[] peaks;
        if (_waves is null)
        {
            if (!_peaks.TryGetValue(track.Id, out peaks!))
            {
                peaks = MakePeaks(track.Id);
                _peaks[track.Id] = peaks;
            }
        }
        else if (_waves.TryGet(track.Id) is { } profile)
        {
            peaks = profile.Peaks;
        }
        else
        {
            // Not drawn yet: say why instead of leaving a hole (no colour-only or silent states).
            PaintPending(track, x, y, width, "waveform");
            return;
        }

        var played = (int)(width * Fraction(track));
        var top = new char[width];
        var bottom = new char[width];
        for (var i = 0; i < width; i++)
        {
            var from = i * peaks.Length / width;
            var to = Math.Max(from + 1, (i + 1) * peaks.Length / width);
            var peak = 0f;
            for (var k = from; k < to; k++)
            {
                peak = Math.Max(peak, peaks[k]);
            }

            var level = (int)(peak * 16);
            bottom[i] = Blocks[Math.Min(level, 8)];
            top[i] = level > 8 ? Blocks[level - 8] : ' ';
        }

        // The waveform is background decoration: a softer accent and a dimmer rest than the rest of the player.
        var done = new Attribute(Theme.Mix(Theme.Accent, Theme.Bg, 0.35), Theme.Bg);
        var todo = new Attribute(Theme.Mix(Theme.FgDim, Theme.Bg, 0.45), Theme.Bg);
        Put(x, y, new string(top, 0, played), done);
        Put(x + played, y, new string(top, played, width - played), todo);
        Put(x, y + 1, new string(bottom, 0, played), done);
        Put(x + played, y + 1, new string(bottom, played, width - played), todo);
    }

    private void PaintControls(int x0)
    {
        var x = x0;
        x = Button(x, 4, "|◀ prev", _player.Previous);
        x = Button(x + 2, 4, "◀◀ -10s", () => _player.Seek(TimeSpan.FromSeconds(-10)));
        x = Button(x + 2, 4, _player.IsPlaying ? "❚❚ pause" : "▶ play", _player.TogglePlay);
        x = Button(x + 2, 4, "+10s ▶▶", () => _player.Seek(TimeSpan.FromSeconds(10)));
        Button(x + 2, 4, "next ▶|", _player.Next);

        const int barWidth = 20;
        var vx = W - barWidth - 10;
        if (vx > x + 4)
        {
            Put(vx, 4, "🔊", Theme.Dim);
            var bx = vx + 4;
            _volume = (bx, bx + barWidth);
            var filled = _player.Volume * barWidth / 100;
            Put(bx, 4, new string('█', filled), new Attribute(Theme.Accent, Theme.Bg));
            Put(bx + filled, 4, new string('░', barWidth - filled), Theme.Dim);
            Put(bx + barWidth + 1, 4, $"{_player.Volume}%", Theme.Normal);
        }
        else
        {
            _volume = (0, 0);
        }
    }

    private int Button(int x, int y, string label, Action run)
    {
        var text = $"[ {label} ]";
        var end = Put(x, y, text, Over(x, x + text.Length, y) ? Theme.Hover : new Attribute(Theme.Fg, Theme.BgAlt));
        _buttons.Add((x, end, run));
        return end;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && !mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed)
            || mouse.Position is not { } p)
        {
            return base.OnMouseEvent(mouse);
        }

        if (ShowCover && p.X < CoverCols && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && _player.Current is not null)
        {
            CoverClicked?.Invoke();
            return true;
        }

        if (p.Y == 1 && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && _player.Current is { } playing)
        {
            if (p.X >= _artistBox.X0 && p.X < _artistBox.X1)
            {
                ArtistClicked?.Invoke(playing.Album);
                return true;
            }

            if (p.X >= _albumBox.X0 && p.X < _albumBox.X1)
            {
                AlbumClicked?.Invoke(playing.Album);
                return true;
            }
        }

        if (p.Y == 0 && p.X >= _seek.X0 && p.X < _seek.X1)
        {
            _player.SeekTo((p.X - _seek.X0) / (double)Math.Max(1, _seek.X1 - _seek.X0));
            return true;
        }

        if (p.Y == 4 && p.X >= _volume.X0 && p.X < _volume.X1)
        {
            _player.SetVolume((p.X - _volume.X0 + 1) * 100 / (_volume.X1 - _volume.X0));
            return true;
        }

        if (p.Y == 4 && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            foreach (var (x0, x1, run) in _buttons)
            {
                if (p.X >= x0 && p.X < x1)
                {
                    run();
                    return true;
                }
            }
        }

        return true;
    }

    private static float[] MakePeaks(int seed)
    {
        // Fake waveform: a slow envelope plus detail, deterministic per track.
        var rnd = new Random(seed);
        var a = rnd.NextDouble() * 6;
        var b = rnd.NextDouble() * 20;
        var peaks = new float[600];
        for (var i = 0; i < peaks.Length; i++)
        {
            var t = i / (double)peaks.Length;
            var env = 0.45 + 0.35 * Math.Sin(t * Math.PI * (1 + a)) * Math.Sin(t * Math.PI);
            var detail = 0.25 * Math.Abs(Math.Sin(i * 0.37 + b)) + 0.2 * rnd.NextDouble();
            peaks[i] = (float)Math.Clamp(env * (0.6 + detail), 0.05, 1);
        }

        return peaks;
    }
}
