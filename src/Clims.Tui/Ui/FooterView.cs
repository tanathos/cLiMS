using Clims.Tui.Data;
using Terminal.Gui.Input;

namespace Clims.Tui.Ui;

/// <summary>
/// Always-visible footer: seek bar on top, cover and waveform on the left, transport buttons and volume.
/// Redrawn at about 10 fps by the main window's timer, so everything here must stay cheap.
/// </summary>
public sealed class FooterView : PaintView
{
    public const int Rows = 6;

    private const int CoverCols = 10;
    private const int CoverRows = 5;
    private const string Blocks = " ▁▂▃▄▅▆▇█";

    private readonly IPlayerController _player;
    private readonly Dictionary<int, float[]> _peaks = new();
    private readonly List<(int X0, int X1, Action Run)> _buttons = new();
    private (int X0, int X1) _seek;
    private (int X0, int X1) _volume;

    public FooterView(IPlayerController player)
    {
        _player = player;
    }

    /// <summary>Set by the main window when the terminal is smaller than 100x30: drop cover/waveform, keep controls.</summary>
    public bool ShowCover { get; set; } = true;

    public bool ShowWaveform { get; set; } = true;

    protected override void Paint()
    {
        _buttons.Clear();
        var track = _player.Current;
        if (track is null)
        {
            Put(1, 0, "Nothing playing", Theme.Dim);
            return;
        }

        PaintSeekBar(track);

        var x0 = 1;
        if (ShowCover && _player.CurrentAlbum is { } album)
        {
            DrawCover(1, 1, CoverCols, CoverRows, album.ArtSeed);
            x0 = CoverCols + 3;
        }

        Put(x0, 1, Clip($"{track.Artist} – {track.Title}  ·  {track.Album}", W - x0 - 2), Theme.Normal);

        if (ShowWaveform)
        {
            PaintWaveform(track, x0, 2, W - x0 - 2);
        }

        PaintControls(x0);
        Put(x0, 5, Clip("Space play/pause  ←/→ ±10 s  n/p track  +/- volume  / search  1-3 tabs  F2 sync  F3/F4 cover mode/size  Ctrl+Q quit", W - x0 - 1), Theme.Dim);
    }

    private void PaintSeekBar(TrackItem track)
    {
        var elapsed = Fmt(_player.Elapsed);
        var total = Fmt(track.Duration);
        var x0 = elapsed.Length + 2;
        var x1 = W - total.Length - 2;
        _seek = (x0, x1);
        Put(1, 0, elapsed, Theme.Normal);
        Put(x1 + 1, 0, total, Theme.Dim);

        var width = Math.Max(1, x1 - x0);
        var filled = (int)(width * Math.Clamp(_player.Elapsed / track.Duration, 0, 1));
        Put(x0, 0, new string('━', filled), new Attribute(Theme.Accent, Theme.Bg));
        Put(x0 + filled, 0, new string('─', width - filled), Theme.Dim);
    }

    private void PaintWaveform(TrackItem track, int x, int y, int width)
    {
        if (width < 8)
        {
            return;
        }

        if (!_peaks.TryGetValue(track.Id, out var peaks))
        {
            peaks = MakePeaks(track.Id);
            _peaks[track.Id] = peaks;
        }

        var played = (int)(width * Math.Clamp(_player.Elapsed / track.Duration, 0, 1));
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

        var done = new Attribute(Theme.Accent, Theme.Bg);
        var todo = new Attribute(Theme.FgDim, Theme.Bg);
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
            Put(vx, 4, "Vol", Theme.Dim);
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
        var end = Put(x, y, text, new Attribute(Theme.Fg, Theme.BgAlt));
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
