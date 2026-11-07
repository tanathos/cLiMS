using System.Net;
using Clims.Lms;
using NAudio.Wave;

namespace Clims.Waveform.Tests;

public class RealTrackTests
{
    [IntegrationFact]
    public async Task A_real_flac_track_gets_a_plausible_waveform()
    {
        var baseUri = new Uri(Environment.GetEnvironmentVariable("CLIMS_LMS_URL")!);
        using var http = new HttpClient();
        using var lms = new LmsClient(new LmsClientOptions { BaseUri = baseUri }, http);
        var track = (await lms.Library.GetTracksAsync(new TrackQuery { Search = "iron maiden", Tags = TrackTags.Duration }, 0, 1)).Items[0];

        using var service = new WaveformService(http, baseUri);
        service.Want(track.Id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (service.StateOf(track.Id) is WaveformState.Unknown or WaveformState.Loading)
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(120), "timed out");
            await Task.Delay(100);
        }

        Assert.True(service.StateOf(track.Id) == WaveformState.Ready, $"{service.StateOf(track.Id)}: {service.ErrorOf(track.Id)} (track {track.Id})");
        var p = service.TryGet(track.Id)!;
        Assert.Equal(PeakAnalyzer.DefaultPoints, p.Peaks.Length);
        Assert.True(Math.Abs(p.Duration.TotalSeconds - track.Duration.TotalSeconds) < 2, $"{p.Duration} vs {track.Duration}");
        Assert.True(p.Peaks.Count(v => v > 0.05f) > p.Peaks.Length / 2, "mostly not silent");
        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "clims-real-peaks.txt"),
            $"{track.Title} {p.Duration} in {sw.Elapsed.TotalSeconds:F1}s: " + string.Join(' ', p.Peaks.Where((_, i) => i % 60 == 0).Select(v => v.ToString("F2"))));
    }
}
