using NAudio.Flac;
using NAudio.Wave;
using NVorbis;

namespace Clims.Waveform;

/// <summary>The file is not in a format this library can decode (for example MP3).</summary>
public sealed class UnsupportedAudioException : Exception
{
    /// <summary>Creates the exception.</summary>
    public UnsupportedAudioException(string message)
        : base(message)
    {
    }
}
