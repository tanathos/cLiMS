namespace Clims.Lyrics.Tests;

public class ShareConnectionTests
{
    [Theory]
    [InlineData(@"\\192.168.1.100\Music", @"\\192.168.1.100\Music")]
    [InlineData(@"\\nas\Music\Vulture Industries\Ghosts", @"\\nas\Music")]
    [InlineData(@"\\nas\Music\", @"\\nas\Music")]
    public void The_host_and_share_are_taken_from_a_network_path(string path, string expected)
    {
        Assert.Equal(expected, ShareConnection.RootOf(path));
    }

    [Theory]
    [InlineData(@"C:\Music")]
    [InlineData("/mnt/Music")]
    [InlineData(@"\\nas")]
    [InlineData(@"\\?\C:\Music")]
    public void Other_paths_have_no_share_root(string path)
    {
        Assert.Null(ShareConnection.RootOf(path));
    }
}
