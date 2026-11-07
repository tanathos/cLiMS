namespace Clims.Lms;

/// <summary>Builds URLs for the audio file itself, used to decode a waveform.</summary>
public static class AudioUrl
{
    /// <summary>The original file of a track: <c>/music/&lt;id&gt;/download</c>.</summary>
    public static Uri Download(Uri baseUri, int trackId) =>
        new(baseUri, $"music/{trackId.ToString(System.Globalization.CultureInfo.InvariantCulture)}/download");
}
