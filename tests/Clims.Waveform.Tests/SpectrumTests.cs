namespace Clims.Waveform.Tests;

public sealed class SpectrumTests
{
    [Fact]
    public void A_pure_tone_lights_the_band_that_holds_its_frequency()
    {
        using var stream = new MemoryStream(Audio.Wav(quiet: 0, loud: 2));
        var spectrum = PeakAnalyzer.AnalyzeAll(stream).Spectrum;

        Assert.Equal(SpectrumBuilder.Bands, spectrum.Bands);
        Assert.InRange(spectrum.FrameCount, 30, 41); // two seconds at 20 frames a second, minus the window
        var frame = spectrum.At(TimeSpan.FromSeconds(1)).ToArray();
        var loudest = Array.IndexOf(frame, frame.Max());
        Assert.Equal(255, frame.Max());
        Assert.InRange(loudest, 15, 17); // 440 Hz on a log scale from 40 Hz to 3.8 kHz is about band 16.8
        Assert.True(frame[2] < 100 && frame[30] < 100, "far from the tone it stays low");
    }

    [Fact]
    public void A_quieter_part_draws_lower_bars_than_a_louder_one()
    {
        using var stream = new MemoryStream(Audio.Wav(quiet: 1.5, loud: 1.5));
        var spectrum = PeakAnalyzer.AnalyzeAll(stream).Spectrum;

        var quiet = spectrum.At(TimeSpan.FromSeconds(0.7)).ToArray().Max();
        var loud = spectrum.At(TimeSpan.FromSeconds(2.3)).ToArray().Max();
        Assert.True(quiet < loud, $"{quiet} should be below {loud}");
    }

    [Fact]
    public void Silence_gives_empty_bars_and_a_time_past_the_end_uses_the_last_frame()
    {
        using var stream = new MemoryStream(Audio.Wav(quiet: 1, loud: 0, quietLevel: 0));
        var spectrum = PeakAnalyzer.AnalyzeAll(stream).Spectrum;

        Assert.All(spectrum.At(TimeSpan.FromHours(1)).ToArray(), level => Assert.Equal(0, level));
    }

    [Fact]
    public void The_analysis_has_the_same_peaks_as_before()
    {
        using var a = new MemoryStream(Audio.Wav(1, 1));
        using var b = new MemoryStream(Audio.Wav(1, 1));
        Assert.Equal(PeakAnalyzer.Analyze(a, 50).Peaks, PeakAnalyzer.AnalyzeAll(b, 50).Peaks.Peaks);
    }

    [Theory]
    [InlineData(44100, 2)]
    [InlineData(48000, 2)]
    [InlineData(96000, 2)]
    [InlineData(22050, 1)]
    public void Real_world_sample_rates_and_channel_counts_are_analysed(int rate, int channels)
    {
        using var stream = new MemoryStream(Audio.Wav(quiet: 1, loud: 2, channels: channels, rate: rate));
        var analysis = PeakAnalyzer.AnalyzeAll(stream);

        Assert.InRange(analysis.Spectrum.FrameCount, 55, 61); // three seconds at 20 frames a second
        Assert.Equal(255, analysis.Spectrum.At(TimeSpan.FromSeconds(2)).ToArray().Max());
    }
}
