using Clims.Tui.Data;

namespace Clims.Tui.Tests;

public class StreamSourceTests
{
    [Theory]
    [InlineData("spotify://track:4uLU6hMCjMI75M1A2tKUQC", "Spotify")]
    [InlineData("spoton://track/abc", "SpotOn")]
    [InlineData("tidal://12345.flc", "TIDAL")]
    [InlineData("unknownservice://x", "Unknownservice")]
    public void A_plugin_address_names_its_service(string url, string expected) => Assert.Equal(expected, StreamSource.NameOf(url));

    [Theory]
    [InlineData("http://regiocast.streamabc.net/stream")]
    [InlineData("file:///mnt/Music/a.flac")]
    [InlineData("/mnt/Music/a.flac")]
    [InlineData(null)]
    public void Web_radio_and_library_files_have_no_service(string? url) => Assert.Null(StreamSource.NameOf(url));
}
