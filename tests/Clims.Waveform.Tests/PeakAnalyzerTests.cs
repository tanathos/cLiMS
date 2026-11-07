using System.Net;
using Clims.Lms;
using NAudio.Wave;

namespace Clims.Waveform.Tests;

public class PeakAnalyzerTests
{
    [Fact]
    public void A_quiet_start_and_a_loud_end_show_in_the_peaks()
    {
        using var stream = new MemoryStream(Audio.Wav(1, 1));
        var p = PeakAnalyzer.Analyze(stream, points: 100);

        Assert.Equal(100, p.Peaks.Length);
        Assert.Equal(2, p.Duration.TotalSeconds, 2);
        Assert.True(p.Peaks[..45].Max() < 0.4f, "first half should be quiet");
        Assert.True(p.Peaks[55..].Min() > 0.9f, "second half should be at the top");
        Assert.Equal(1f, p.Peaks.Max(), 3);
    }

    [Fact]
    public void A_quiet_stereo_track_is_normalised()
    {
        using var stream = new MemoryStream(Audio.Wav(0.5, 0.5, channels: 2, quietLevel: 0.01, loudLevel: 0.02));
        var p = PeakAnalyzer.Analyze(stream, points: 50);
        Assert.Equal(1f, p.Peaks.Max(), 3);   // even a very quiet file reaches the top
        Assert.True(p.Peaks[0] < p.Peaks[^1]);
    }

    [Fact]
    public void Silence_gives_flat_peaks_without_dividing_by_zero()
    {
        using var stream = new MemoryStream(Audio.Wav(1, 0, quietLevel: 0));
        var p = PeakAnalyzer.Analyze(stream, points: 10);
        Assert.All(p.Peaks, v => Assert.Equal(0f, v));
    }

    [Theory]
    [InlineData("ID3 rest of an mp3 file")]
    [InlineData("abc")]
    public void Other_formats_are_reported_as_unsupported(string text)
    {
        using var stream = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(text));
        Assert.Throws<UnsupportedAudioException>(() => PeakAnalyzer.Analyze(stream));
    }

    [Fact]
    public void Cancellation_stops_the_analysis()
    {
        using var stream = new MemoryStream(Audio.Wav(1, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PeakAnalyzer.Analyze(stream, 100, cts.Token));
    }

    [Fact]
    public void A_stream_longer_than_announced_does_not_overflow_the_last_slice()
    {
        var peaks = new float[4];
        long frame = 0;
        PeakAnalyzer.Accumulate(peaks, new[] { 0.1f, 0.2f, 0.3f, 0.9f, 0.5f, 0.5f }, 6, 1, ref frame, totalFrames: 3);
        Assert.Equal(6, frame);
        Assert.Equal(0.9f, peaks[3]);
    }
}
