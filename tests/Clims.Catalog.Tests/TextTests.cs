using Clims.Lms;

namespace Clims.Catalog.Tests;

public class TextTests
{
    [Theory]
    [InlineData("Mew", "M")]
    [InlineData("The 13th Floor Elevators", "#")]
    [InlineData("The Cure", "C")]
    [InlineData("the cure", "C")]
    [InlineData("Édith Piaf", "E")]
    [InlineData("  ", "#")]
    [InlineData("4 Non Blondes", "#")]
    [InlineData("¿Qué?", "#")]
    public void Text_key(string name, string expected) => Assert.Equal(expected, new CatalogArtist(1, name).TextKey);

    [Fact]
    public void Folding_removes_case_and_accents() => Assert.Equal("pante", TextFolding.Fold("PANTÉ"));

    [Fact]
    public void Words_split_on_punctuation() =>
        Assert.Equal(new[] { "ac", "dc", "back", "in", "black" }, TextFolding.Words("ac/dc - back in black"));
}
