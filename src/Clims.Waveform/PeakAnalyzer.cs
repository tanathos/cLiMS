using NAudio.Flac;
using NAudio.Wave;
using NVorbis;

namespace Clims.Waveform;

/// <summary>An overview of one track: loudness per slice of its length, 0 to 1.</summary>
/// <param name="Peaks">Peak amplitude of each equal slice of the track, normalised so the loudest slice is 1.</param>
/// <param name="Duration">Length of the decoded audio.</param>
public sealed record PeakProfile(float[] Peaks, TimeSpan Duration);

/// <summary>The file is not in a format this library can decode (for example MP3).</summary>
public sealed class UnsupportedAudioException : Exception
{
    /// <summary>Creates the exception.</summary>
    public UnsupportedAudioException(string message)
        : base(message)
    {
    }
}

/// <summary>Decodes an audio file and reduces it to peaks. FLAC, Ogg Vorbis and WAV, recognised by their first bytes.</summary>
public static class PeakAnalyzer
{
    /// <summary>Number of peaks a profile has unless asked otherwise.</summary>
    public const int DefaultPoints = 1200;

    /// <summary>Reads the whole (seekable) stream and returns its peaks.</summary>
    /// <exception cref="UnsupportedAudioException">The format is not FLAC, Ogg or WAV.</exception>
    public static PeakProfile Analyze(Stream stream, int points = DefaultPoints, CancellationToken cancellationToken = default)
    {
        var tag = Sniff(stream);
        return tag switch
        {
            "fLaC" => FromWaveStream(new FlacReader(stream), points, cancellationToken),
            "RIFF" => FromWaveStream(new WaveFileReader(stream), points, cancellationToken),
            "OggS" => FromVorbis(stream, points, cancellationToken),
            "MP3" => FromMp3(stream, points, cancellationToken),
            _ => throw new UnsupportedAudioException($"Only FLAC, MP3, Ogg Vorbis and WAV files can be drawn as a waveform (this one starts with \"{tag}\")."),
        };
    }

    /// <summary>
    /// The four letters that say what the file is. A leading ID3v2 tag (FLAC files are often tagged this way) is looked
    /// past; the stream is left at its start so the decoder can skip the tag itself.
    /// </summary>
    internal static string Sniff(Stream stream)
    {
        var head = new byte[10];
        var read = stream.Read(head, 0, head.Length);
        var offset = 0L;
        if (read >= 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3')
        {
            // 10-byte header, then a size in four 7-bit bytes ("syncsafe"), plus a 10-byte footer when flag 0x10 is set.
            var size = (head[6] << 21) | (head[7] << 14) | (head[8] << 7) | head[9];
            offset = 10 + size + ((head[5] & 0x10) != 0 ? 10 : 0);
        }

        stream.Position = offset;
        var magic = new byte[4];
        read = stream.Read(magic, 0, 4);
        stream.Position = 0;
        if (read < 4)
        {
            throw new UnsupportedAudioException("The file is too short to be audio.");
        }

        // An MPEG audio frame starts with eleven set bits.
        if (magic[0] == 0xFF && (magic[1] & 0xE0) == 0xE0)
        {
            return "MP3";
        }

        return System.Text.Encoding.Latin1.GetString(magic).Select(c => char.IsControl(c) ? '?' : c).Aggregate("", (s, c) => s + c);
    }

    private static PeakProfile FromMp3(Stream stream, int points, CancellationToken ct)
    {
        using var mp3 = new NLayer.MpegFile(stream);
        var channels = mp3.Channels;
        var frames = (long)(mp3.Duration.TotalSeconds * mp3.SampleRate);
        var peaks = new float[points];
        var buffer = new float[channels * 8192];
        long frame = 0;
        int got;
        while ((got = mp3.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            Accumulate(peaks, buffer, got, channels, ref frame, frames);
        }

        return Finish(peaks, TimeSpan.FromSeconds(frame / (double)mp3.SampleRate));
    }

    private static PeakProfile FromWaveStream(WaveStream reader, int points, CancellationToken ct)
    {
        using (reader)
        {
            var samples = reader.ToSampleProvider();
            var channels = samples.WaveFormat.Channels;
            var frames = reader.Length / Math.Max(1, reader.WaveFormat.BlockAlign);
            var peaks = new float[points];
            var buffer = new float[channels * 8192];
            long frame = 0;
            int got;
            while ((got = samples.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                Accumulate(peaks, buffer, got, channels, ref frame, frames);
            }

            return Finish(peaks, TimeSpan.FromSeconds(frame / (double)samples.WaveFormat.SampleRate));
        }
    }

    private static PeakProfile FromVorbis(Stream stream, int points, CancellationToken ct)
    {
        using var reader = new VorbisReader(stream, closeOnDispose: false);
        var channels = reader.Channels;
        var frames = reader.TotalSamples;
        var peaks = new float[points];
        var buffer = new float[channels * 8192];
        long frame = 0;
        int got;
        while ((got = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            Accumulate(peaks, buffer, got, channels, ref frame, frames);
        }

        return Finish(peaks, TimeSpan.FromSeconds(frame / (double)reader.SampleRate));
    }

    /// <summary>Folds interleaved samples into the slice each frame belongs to (by position in the expected length).</summary>
    internal static void Accumulate(float[] peaks, float[] buffer, int count, int channels, ref long frame, long totalFrames)
    {
        var total = Math.Max(1, totalFrames);
        for (var i = 0; i + channels <= count; i += channels, frame++)
        {
            var slice = (int)Math.Min(peaks.Length - 1, frame * peaks.Length / total);
            var max = peaks[slice];
            for (var c = 0; c < channels; c++)
            {
                var a = Math.Abs(buffer[i + c]);
                if (a > max)
                {
                    max = a;
                }
            }

            peaks[slice] = max;
        }
    }

    /// <summary>Normalises to the loudest slice and bends the curve a little, so quiet passages stay visible in a few rows of text.</summary>
    internal static PeakProfile Finish(float[] peaks, TimeSpan duration)
    {
        var loudest = peaks.Max();
        if (loudest <= 0)
        {
            return new PeakProfile(peaks, duration);
        }

        for (var i = 0; i < peaks.Length; i++)
        {
            peaks[i] = MathF.Pow(peaks[i] / loudest, 0.7f);
        }

        return new PeakProfile(peaks, duration);
    }
}
