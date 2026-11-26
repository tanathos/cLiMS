using Clims.Catalog;
using Clims.Tui.Data;
using Clims.Visualizer;
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
    private readonly SpectrumFeed _feed;
    private readonly Dictionary<int, float[]> _peaks = new();
    private readonly List<(int X0, int X1, Action Run)> _buttons = new();
    private (int X0, int X1) _seek;
    private (int X0, int X1) _volume;

    /// <summary>Creates the footer. With no <paramref name="waves"/> (demo mode) a generated waveform is drawn.</summary>
    public FooterView(IPlayerController player, WaveformService? waves = null)
    {
        _player = player;
        _waves = waves;
        _feed = new SpectrumFeed(player, waves);
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

    /// <summary>True: the version number, very faint, right-aligned under the volume bar.</summary>
    public bool ShowVersion { get; set; } = true;

    private static readonly string VersionText = ReadVersion();

    private static string ReadVersion()
    {
        var v = typeof(FooterView).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
        var plus = v.IndexOf('+');
        v = plus >= 0 ? v[..plus] : v;
        return v.Length == 0 ? "" : "v" + v;
    }

    /// <summary>Draws the version on the hint row, ending where the volume percentage ends; returns the column the hint must stop before.</summary>
    private int PaintVersion(int minX)
    {
        var text = VersionText;
        var x = W - 2 - text.Length;   // the last character sits under the "%" of the volume reading
        if (!ShowVersion || text.Length == 0 || x < minX + 20)
        {
            return W - 1;
        }

        Put(x, 5, text, new Attribute(Theme.Faint, Theme.Bg));
        return x - 2;
    }

    protected override void Paint()
    {
        _buttons.Clear();
        _wave = (0, 0);
        _still = null;
        if (_player.Current is not { } now)
        {
            _seek = _totalBox = (0, 0);
            Put(1, 0, Clip(_player.Problem ?? "Nothing playing", W - 2), _player.Problem is null ? Theme.Dim : new Attribute(Theme.Warn, Theme.Bg));
            PaintControls(1);
            PaintHint(1);
            return;
        }

        var track = now.Track;
        var album = now.Album;
        PaintSeekBar(track, ShowCover ? CoverCols + 2 : 1);

        var x0 = 1;
        if (ShowCover)
        {
            DrawCover(0, 0, CoverCols, CoverRows, new CoverKey(album.ArtworkId, album.Id), CoverPlace.Footer);
            x0 = CoverCols + 2;
        }

        if (now.Station is not null)
        {
            PaintStationTitle(now, x0, W - x0 - 2);
        }
        else
        {
            _stationBox = (0, 0);
            PaintTitle(now, x0, W - x0 - 2);
        }

        if (IsLive)
        {
            // What the speakers play right now, so it also works for radio streams, which have no waveform.
            PaintSpectrum(track, x0, 2, W - x0 - 2);
        }
        else if (ShowWaveform && now.IsRemote)
        {
            // A stream is not a file the server can analyse: say so instead of waiting for an analysis that never comes.
            Put(x0, 2, Clip("Waveform not available", W - x0 - 2), Theme.Dim);
        }
        else if (ShowWaveform)
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
        PaintHint(x0);
    }

    /// <summary>The key legend of the last row, in the order shown.</summary>
    private static readonly (string Key, string Label)[] Hints =
    [
        ("Space", "play/pause"), ("←/→", "±10 s"), ("Alt+n/p", "track"), ("+/-", "volume"),
        ("/", "search"), ("1-8", "tabs"), ("Ctrl+Q", "quit"), ("?", "help"),
    ];

    /// <summary>The keys on the last row, with what each does, or the keys alone where there is no room for that.</summary>
    private void PaintHint(int x0)
    {
        var end = PaintVersion(x0);
        var full = Hints.Sum(h => h.Key.Length + 1 + h.Label.Length) + 2 * (Hints.Length - 1) <= end - x0;
        var key = new Attribute(Theme.Mix(Theme.Fg, Theme.Bg, 0.25), Theme.Bg);
        var label = new Attribute(Theme.Mix(Theme.Fg, Theme.Bg, 0.5), Theme.Bg);
        var x = x0;
        foreach (var (name, does) in Hints)
        {
            x = Put(x, 5, Clip(name, end - x), key);
            if (full)
            {
                x = Put(x, 5, " " + does, label);
            }

            x += 2;
        }
    }

    /// <summary>The user clicked the cover: show the playing album full screen.</summary>
    public event Action? CoverClicked;

    /// <summary>The user clicked the artist of the playing track: show that artist in the library.</summary>
    public event Action<CatalogAlbum>? ArtistClicked;

    /// <summary>The user clicked the album of the playing track: show it in the library, scrolled into view.</summary>
    public event Action<CatalogAlbum>? AlbumClicked;

    private (int X0, int X1) _artistBox;
    private (int X0, int X1) _albumBox;

    /// <summary>The user clicked the station name of a radio stream: show where it was chosen in the Online tab.</summary>
    public event Action? StationClicked;

    private (int X0, int X1) _stationBox;

    /// <summary>The name to show for where a streamed track comes from (the window knows which app it was played from); null: no tag.</summary>
    public Func<NowPlaying, string?>? SourceName { get; set; }

    /// <summary>"station · artist · title" for a radio stream; only the station is a link.</summary>
    private void PaintStationTitle(NowPlaying now, int x, int width)
    {
        const string sep = "  ·  ";
        var link = new Attribute(Theme.Fg, Theme.Bg, TextStyle.Underline);
        var linkHover = new Attribute(Theme.HoverFg, Theme.Bg, TextStyle.Underline);
        var end = x + width;
        _artistBox = _albumBox = (0, 0);
        var s = Clip(now.Station!, Math.Max(0, end - x));
        var cx = Put(x, 1, s, Over(x, x + s.Length, 1) ? linkHover : link);
        _stationBox = (x, cx);
        if (cx >= end)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(now.Track.Artist))
        {
            cx = Put(cx, 1, Clip(sep, end - cx), Theme.Dim);
            cx = Put(cx, 1, Clip(now.Track.Artist, Math.Max(0, end - cx)), Theme.Normal);
            if (cx >= end)
            {
                return;
            }
        }

        cx = Put(cx, 1, Clip(sep, end - cx), Theme.Dim);
        Put(cx, 1, Clip(now.Track.Title, Math.Max(0, end - cx)), new Attribute(Theme.Fg, Theme.Bg, TextStyle.Bold));
    }

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
        if (SourceName?.Invoke(now) is { Length: > 0 } source)
        {
            cx = Put(cx, 1, Clip($"[From {source}] ", Math.Max(0, end - cx)), Theme.Dim);
        }

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

    private bool _countdown;
    private (int X0, int X1) _totalBox;

    private void PaintSeekBar(CatalogTrack track, int left)
    {
        var elapsed = Fmt(_player.Elapsed);
        var total = _countdown ? "-" + Fmt(track.Duration - _player.Elapsed < TimeSpan.Zero ? TimeSpan.Zero : track.Duration - _player.Elapsed) : Fmt(track.Duration);
        var x0 = left + elapsed.Length + 1;
        // The total ends where the "(3/12)" queue position does, one column in from the edge.
        var x1 = W - total.Length - 3;
        _seek = (x0, x1);
        _totalBox = (x1 + 1, x1 + 1 + total.Length);
        Put(left, 0, elapsed, Theme.Normal);
        Put(x1 + 1, 0, total, Over(x1 + 1, x1 + 1 + total.Length, 0) ? Theme.Hover : Theme.Dim);

        var width = Math.Max(1, x1 - x0);
        var filled = (int)(width * Fraction(track));
        Put(x0, 0, new string('━', filled), new Attribute(Theme.Accent, Theme.Bg));
        Put(x0 + filled, 0, new string('─', width - filled), Theme.Dim);

        // Pointing past the playhead: the stretch a click would skip is drawn thick in a faint accent.
        var hover = -1;
        for (var c = x0 + filled; c < x0 + width; c++)
        {
            if (Over(c, c + 1, 0))
            {
                hover = c;
            }
        }

        if (hover >= 0)
        {
            Put(x0 + filled, 0, new string('━', hover - (x0 + filled) + 1), new Attribute(Theme.Mix(Theme.Accent, Theme.Bg, 0.5), Theme.Bg));
        }
    }

    /// <summary>The live spectrum from the player (vismux), or null. While it is live it replaces the waveform.</summary>
    public IVisualSource? Live
    {
        get => _feed.Live;
        set => _feed.Live = value;
    }

    /// <summary>True: the spectrum bars flash on the beat of the music.</summary>
    public bool BeatPulse
    {
        get => _feed.BeatPulse;
        set => _feed.BeatPulse = value;
    }

    /// <summary>True while the live spectrum is being drawn.</summary>
    public bool IsLive => ShowWaveform && ShowSpectrum && _feed.IsLive;

    /// <summary>True while the spectrum moves, which is what needs a repaint much faster than the clock does.</summary>
    public bool Animated => ShowWaveform && ShowSpectrum && (_feed.IsLive || _player.IsPlaying);

    public SpectrumStyle SpectrumStyle { get; set; } = SpectrumStyle.Bars;

    /// <summary>F8: draw a spectrum analyzer in place of the waveform (the live one, or the one precomputed from the file when there is no live feed).</summary>
    public bool ShowSpectrum { get; set; }

    /// <summary>
    /// Added to the playhead when picking the frame of the spectrum computed from the file. The position LMS reports is when the
    /// server last heard from the player, and the audio takes a little longer to reach the speakers, so a few hundred ms may be needed either way.
    /// </summary>
    public TimeSpan SpectrumOffset
    {
        get => _feed.Offset;
        set => _feed.Offset = value;
    }

    /// <summary>The bars of the spectrum that is playing now: live from the player, or computed from the file and following the playhead.</summary>
    private void PaintSpectrum(CatalogTrack track, int x, int y, int width)
    {
        if (width < 8)
        {
            return;
        }

        if (!_feed.Update(track))
        {
            PaintPending(track, x, y, width, "spectrum");
            return;
        }

        _wave = (x, x + width); // a click on it changes the style
        PutSpectrum(x, y, width, SpectrumStyle, _feed);
    }

    /// <summary>Goes to the next <see cref="SpectrumStyle"/>.</summary>
    public void NextSpectrumStyle()
    {
        SpectrumStyle = (SpectrumStyle)(((int)SpectrumStyle + 1) % Enum.GetValues<SpectrumStyle>().Length);
        SetNeedsDraw();
    }

    /// <summary>Says why the waveform or spectrum is not drawn yet instead of leaving a hole.</summary>
    private void PaintPending(CatalogTrack track, int x, int y, int width, string what) =>
        Put(x, y, Clip(SpectrumFeed.Pending(_waves!, track, what), width), Theme.Dim);

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
            _wave = (x, x + width);
            Reclaim(x, y, width, 2);
            PaintPending(track, x, y, width, "waveform");
            return;
        }

        _wave = (x, x + width);
        var played = (int)(width * Fraction(track));
        // The waveform is background decoration: a softer accent and a dimmer rest than the rest of the player.
        var doneColour = Theme.Mix(Theme.Accent, Theme.Bg, 0.35);
        var todoColour = Theme.Mix(Theme.FgDim, Theme.Bg, 0.45);
        if (WaveformStyle == WaveformStyle.Sixel && PaintWaveformImage(track, peaks, x, y, width, played, doneColour, todoColour))
        {
            return;
        }

        Reclaim(x, y, width, 2);
        PutWaveform(x, y, width, peaks, played, WaveformStyle);
    }

    /// <summary>How the waveform is drawn; a click on it moves to the next style (this run only).</summary>
    public WaveformStyle WaveformStyle { get; set; } = WaveformStyle.Blocks;

    private (int X0, int X1) _wave;
    private System.Drawing.Rectangle? _still;
    private System.Drawing.Size _stillSize;
    private CoverImage? _waveImage;
    private int _wavePlayed = -1;

    /// <summary>The waveform image does not move between frames: the per-frame clear must not wipe it.</summary>
    protected override System.Drawing.Rectangle? StillCells => _stillSize == new System.Drawing.Size(W, H) ? _still : null;

    /// <summary>
    /// The waveform as a sixel. The image is made again each time the played part grows by a cell (a few seconds), not every
    /// frame. False when no sixel can be shown (no terminal support, or a popup is open): the caller draws braille instead.
    /// </summary>
    private bool PaintWaveformImage(CatalogTrack track, float[] peaks, int x, int y, int width, int played, Color done, Color todo)
    {
        if (!SixelCovers.Available)
        {
            return false;
        }

        var cell = SixelCovers.CellPixels;
        // The label (and so the placement) stays the same while the played part grows: only the pixels change.
        var label = $"wave:{track.Id}:{width}x{cell.Width}x{cell.Height}";
        if (_waveImage?.Label != label || _wavePlayed != played)
        {
            _wavePlayed = played;
            var pixels = WaveformImage.Render(peaks, width * cell.Width, 2 * cell.Height, cell.Width / 2, played * cell.Width, done, todo, Theme.Bg);
            var old = _waveImage;
            _waveImage = CoverImage.FromPixels(pixels, label);
            if (old is not null)
            {
                SixelCovers.Forget(old);
                old.Dispose();
            }
        }

        if (!DrawSixel(_waveImage, x, y, width, 2))
        {
            return false;
        }

        _still = new System.Drawing.Rectangle(x, y, width, 2);
        _stillSize = new System.Drawing.Size(W, H);
        return true;
    }

    private void PaintControls(int x0)
    {
        var x = x0;
        var live = _player.Current?.Station is null;   // a radio stream cannot skip or seek
        // Full labels need the five buttons, the shuffle button and the volume; if they do not all fit, only the icons are shown.
        const int fullButtonsWidth = 11 + 12 + 12 + 12 + 12 + 4 * 2 + 2 + 13;
        const int fullVolumeWidth = 30;
        var compact = x0 + fullButtonsWidth + 2 + fullVolumeWidth > W;
        var barWidth = compact ? 10 : 20;
        x = Button(x, 4, compact ? "|◀" : "|◀ prev", _player.Previous, live);
        x = Button(x + 2, 4, compact ? "◀◀" : "◀◀ -10s", () => _player.Seek(TimeSpan.FromSeconds(-10)), live);
        x = Button(x + 2, 4, _player.IsPlaying ? (compact ? "❚❚" : "❚❚ pause") : (compact ? "▶" : "▶ play"), _player.TogglePlay);
        x = Button(x + 2, 4, compact ? "▶▶" : "+10s ▶▶", () => _player.Seek(TimeSpan.FromSeconds(10)), live);
        x = Button(x + 2, 4, compact ? "▶|" : "next ▶|", _player.Next, live);

        var vx = W - barWidth - 10;
        var shuffle = compact ? "⇄" : "⇄ shuffle";
        if (x + 2 + shuffle.Length + 4 < vx - 1)
        {
            x = Button(x + 2, 4, shuffle, _player.ToggleShuffle, active: _player.Shuffle);
        }

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

    /// <param name="active">A switch that is on: drawn in the accent colour.</param>
    private int Button(int x, int y, string label, Action run, bool enabled = true, bool active = false)
    {
        var text = $"[ {label} ]";
        if (!enabled)
        {
            return Put(x, y, text, new Attribute(Theme.Faint, Theme.BgAlt));
        }

        var end = Put(x, y, text, Over(x, x + text.Length, y) ? Theme.Hover : new Attribute(active ? Theme.Accent : Theme.Fg, Theme.BgAlt));
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

        if (p.Y == 1 && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && p.X >= _stationBox.X0 && p.X < _stationBox.X1 && _player.Current?.Station is not null)
        {
            StationClicked?.Invoke();
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

        if (p.Y is 2 or 3 && p.X >= _wave.X0 && p.X < _wave.X1 && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            if (ShowSpectrum)
            {
                NextSpectrumStyle();
            }
            else
            {
                WaveformStyle = (WaveformStyle)(((int)WaveformStyle + 1) % Enum.GetValues<WaveformStyle>().Length);
            }

            return true;
        }

        if (p.Y == 0 && p.X >= _totalBox.X0 && p.X < _totalBox.X1 && mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            _countdown = !_countdown;
            SetNeedsDraw();
            return true;
        }

        if (p.Y == 0 && p.X >= _seek.X0&& p.X < _seek.X1)
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

    /// <summary>A made-up waveform for demo mode, the same every time for one seed.</summary>
    internal static float[] MakePeaks(int seed)
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
