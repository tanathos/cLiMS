using Clims.Catalog;
using Clims.Tui.Data;
using Clims.Visualizer;
using Clims.Waveform;

namespace Clims.Tui.Ui;

/// <summary>
/// The band levels of the spectrum shown now: from the live feed of the player, or else computed from the file and following the
/// playhead (a made-up one in demo mode). Rises at once and falls a little each update, which is what makes it look alive.
/// Each view that draws a spectrum has its own feed, because reading the live one empties it.
/// </summary>
public sealed class SpectrumFeed
{
    /// <summary>Number of bands in <see cref="Levels"/>.</summary>
    public const int Bands = 32;

    private readonly IPlayerController _player;
    private readonly WaveformService? _waves;
    private readonly float[] _smooth = new float[Bands];
    private int _track = -1;
    private int _frame = -1;

    /// <param name="player">Where the playhead is, for the spectrum computed from the file.</param>
    /// <param name="waves">The analysed tracks; null in demo mode.</param>
    public SpectrumFeed(IPlayerController player, WaveformService? waves)
    {
        _player = player;
        _waves = waves;
    }

    /// <summary>The live spectrum from the player (vismux), or null.</summary>
    public IVisualSource? Live { get; set; }

    /// <summary>
    /// Added to the playhead when picking the frame of the spectrum computed from the file. The position LMS reports is when the
    /// server last heard from the player, and the audio takes a little longer to reach the speakers.
    /// </summary>
    public TimeSpan Offset { get; set; }

    /// <summary>True while samples of what is playing are arriving.</summary>
    public bool IsLive => Live?.State == VisualState.Live;

    /// <summary>The levels after the last <see cref="Update"/>, each 0 to 1.</summary>
    public float[] Levels => _smooth;

    /// <summary>Moves <see cref="Levels"/> on to now. False while the spectrum of the track is not ready (see <see cref="Pending"/>).</summary>
    public bool Update(CatalogTrack track)
    {
        var target = new float[Bands];
        if (Live is { State: VisualState.Live } live && live.Bands == Bands)
        {
            live.TakeBands(target);
        }
        else if (_waves is null)
        {
            var t = _player.Elapsed.TotalSeconds;
            for (var b = 0; b < Bands; b++)
            {
                target[b] = (0.55f + 0.45f * MathF.Sin((float)t * (1.3f + b * 0.21f) + b * 1.7f)) * (1 - b / 60f);
            }
        }
        else if (_waves.TrySpectrum(track.Id) is { } spectrum)
        {
            var now = spectrum.IndexAt(_player.Elapsed + Offset);
            // A frame lasts 50 ms and the view repaints less often than that: take the loudest of every frame passed since the
            // last update, otherwise a beat that falls between two repaints is never drawn.
            var from = _track == track.Id && _frame is >= 0 && now > _frame && now - _frame <= 6 ? _frame + 1 : now;
            _track = track.Id;
            _frame = now;
            for (var i = Math.Max(0, from); i <= now; i++)
            {
                var frame = spectrum.Frame(i);
                for (var b = 0; b < Bands && b < frame.Length; b++)
                {
                    target[b] = Math.Max(target[b], frame[b] / 255f);
                }
            }
        }
        else
        {
            return false;
        }

        // 0.12 per update at the 1 to 10 fps of the spectrum computed from the file; the live one is repainted 30 times a second.
        var fall = IsLive ? 0.05f : 0.12f;
        for (var b = 0; b < Bands; b++)
        {
            _smooth[b] = Math.Max(target[b], _smooth[b] - fall);
        }

        return true;
    }

    /// <summary>Says why the waveform or spectrum of a track is not drawn yet, instead of leaving a hole.</summary>
    /// <param name="waves">The analysed tracks.</param>
    /// <param name="track">The playing track.</param>
    /// <param name="what">"waveform" or "spectrum".</param>
    public static string Pending(WaveformService waves, CatalogTrack track, string what) => waves.StateOf(track.Id) switch
    {
        WaveformState.Unsupported => $"No {what}: " + (waves.ErrorOf(track.Id) ?? "unsupported format"),
        WaveformState.Failed => $"No {what}: " + (waves.ErrorOf(track.Id) ?? "download failed") + " (retrying later)",
        _ => $"◐ Analysing {what}…",
    };
}
