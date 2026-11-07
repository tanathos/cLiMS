namespace Clims.Lms;

/// <summary>
/// Builds cover art URLs. The server resizes for us: <c>/music/&lt;id&gt;/cover_96x96_o.jpg</c> returns a few KB in
/// about 50 ms, so no local resizing is needed. The id is an album's or track's <c>artwork_track_id</c>, or a track id.
/// If password protection is on, send the same Basic credentials with the request.
/// </summary>
public static class ArtworkUrl
{
    /// <summary>Cover for an <c>artwork_track_id</c> (hex string) or track id given as text.</summary>
    public static Uri Cover(Uri baseUri, string artworkId, int width, int height, ArtworkFormat format = ArtworkFormat.Jpeg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artworkId);
        var extension = format == ArtworkFormat.Png ? "png" : "jpg";
        return new Uri(baseUri, $"music/{Uri.EscapeDataString(artworkId)}/cover_{width}x{height}_o.{extension}");
    }

    /// <summary>Cover for a track id.</summary>
    public static Uri Cover(Uri baseUri, int trackId, int width, int height, ArtworkFormat format = ArtworkFormat.Jpeg) =>
        Cover(baseUri, trackId.ToString(System.Globalization.CultureInfo.InvariantCulture), width, height, format);
}
