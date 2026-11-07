using System.Net;
using Clims.Lms;
using NAudio.Wave;

namespace Clims.Waveform.Tests;

internal static class Audio
{
    /// <summary>A 16-bit WAV: <paramref name="quiet"/> seconds at low amplitude, then <paramref name="loud"/> seconds at high amplitude.</summary>
    public static byte[] Wav(double quiet, double loud, int channels = 1, int rate = 8000, double quietLevel = 0.05, double loudLevel = 0.9)
    {
        using var ms = new MemoryStream();
        using (var writer = new WaveFileWriter(ms, new WaveFormat(rate, 16, channels)))
        {
            void Tone(double seconds, double level)
            {
                var frames = (int)(seconds * rate);
                for (var i = 0; i < frames; i++)
                {
                    var v = (short)(Math.Sin(i * 2 * Math.PI * 440 / rate) * level * short.MaxValue);
                    for (var c = 0; c < channels; c++)
                    {
                        writer.WriteByte((byte)(v & 0xff));
                        writer.WriteByte((byte)(v >> 8));
                    }
                }
            }

            Tone(quiet, quietLevel);
            Tone(loud, loudLevel);
        }

        return ms.ToArray();
    }
}
