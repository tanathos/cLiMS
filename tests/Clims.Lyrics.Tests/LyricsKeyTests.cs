namespace Clims.Lyrics.Tests;

public class LyricsKeyTests
{
    private static LyricsQuery Q(string artist, string album, string title) => new(artist, album, title, TimeSpan.Zero);

    [Fact]
    public void Case_accents_and_punctuation_do_not_matter()
    {
        Assert.Equal(Q("Motorhead", "Ace of Spades", "Ace Of Spades").Key, Q("Motörhead", "Ace Of Spades!", "ace of spades").Key);
    }

    [Theory]
    [InlineData("Raining Blood (2013 Remaster)")]
    [InlineData("Raining Blood [Remastered]")]
    [InlineData("Raining Blood - 2013 Remaster")]
    [InlineData("Raining Blood (Deluxe Edition)")]
    public void Remaster_and_edition_tags_are_dropped(string title)
    {
        Assert.Equal(Q("Slayer", "Reign In Blood", "Raining Blood").Key, Q("Slayer", "Reign In Blood", title).Key);
    }

    [Fact]
    public void A_title_in_brackets_that_is_not_an_edition_tag_is_kept()
    {
        Assert.NotEqual(Q("A", "B", "Song").Key, Q("A", "B", "Song (Live)").Key);
    }

    [Fact]
    public void Different_albums_are_different_keys()
    {
        Assert.NotEqual(Q("Slayer", "Reign In Blood", "Raining Blood").Key, Q("Slayer", "Decade Of Aggression", "Raining Blood").Key);
    }

    [Fact]
    public void Non_latin_text_survives()
    {
        Assert.Equal("мастер|альбом|песня", Q("Мастер", "Альбом", "Песня").Key);
    }
}
