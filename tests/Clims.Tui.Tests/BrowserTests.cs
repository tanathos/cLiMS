using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class BrowserTests
{
    [Theory]
    [InlineData("https://en.wikipedia.org/wiki/Helloween", true)]
    [InlineData("http://example.org", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyWebAddressesAreOpened(string? url, bool expected) => Assert.Equal(expected, Browser.IsWebAddress(url));

    [Fact]
    public void SomethingThatIsNotAWebAddressIsNeverStarted() => Assert.False(Browser.Open("calc.exe"));
}
