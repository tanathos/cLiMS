using Clims.Waveform;
using NAudio.Flac;
using NAudio.Wave;
using NVorbis;

namespace Clims.ChordProbe;

/// <summary>Decodes a whole file to mono samples, with the decoders the waveform uses.</summary>
internal static class AudioDecoder
{
    public static (float[] Samples, int Rate) Decode(string path)
    {
        using var stream = File.OpenRead(path);
        switch (PeakAnalyzer.Sniff(stream))
        {
            case "MP3":
                using (var mp3 = new NLayer.MpegFile(stream))
                {
                    return (Mono(b => mp3.ReadSamples(b, 0, b.Length), mp3.Channels), mp3.SampleRate);
                }

            case "OggS":
                using (var ogg = new VorbisReader(stream, closeOnDispose: false))
                {
                    return (Mono(b => ogg.ReadSamples(b, 0, b.Length), ogg.Channels), ogg.SampleRate);
                }

            case "fLaC":
                return FromWave(new FlacReader(stream));
            case "RIFF":
                return FromWave(new WaveFileReader(stream));
            default:
                throw new UnsupportedAudioException("Only FLAC, MP3, Ogg Vorbis and WAV files are supported.");
        }
    }

    private static (float[] Samples, int Rate) FromWave(WaveStream reader)
    {
        using (reader)
        {
            var samples = reader.ToSampleProvider();
            return (Mono(b => samples.Read(b, 0, b.Length), samples.WaveFormat.Channels), samples.WaveFormat.SampleRate);
        }
    }

    private static float[] Mono(Func<float[], int> read, int channels)
    {
        var mono = new List<float>(1 << 23);
        var buffer = new float[channels * 8192];
        int got;
        while ((got = read(buffer)) > 0)
        {
            for (var i = 0; i + channels <= got; i += channels)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    sum += buffer[i + c];
                }

                mono.Add(sum / channels);
            }
        }

        return [.. mono];
    }
}
