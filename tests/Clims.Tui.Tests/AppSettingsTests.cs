using Clims.Catalog;
using Clims.Tui;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20/")]
    [InlineData("192.168.1.20:9000", "http://192.168.1.20:9000/")]
    [InlineData("http://192.168.1.20:9000", "http://192.168.1.20:9000/")]
    [InlineData("  http://lms.local:9000/  ", "http://lms.local:9000/")]
    [InlineData("https://lms.example.org/lms", "https://lms.example.org/lms/")]
    public void Addresses_are_normalised_to_a_slash_terminated_http_uri(string input, string expected) =>
        Assert.Equal(expected, AppSettings.NormalizeUrl(input)!.ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://host/")]
    public void Unusable_addresses_give_null(string? input) => Assert.Null(AppSettings.NormalizeUrl(input));
}
