using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms.Tests;

public class TagTests
{
    [Fact]
    public void No_flags_gives_an_empty_tag_list() => Assert.Equal("tags:", TrackTags.None.ToTagArgument());

    [Fact]
    public void Track_flags_map_to_their_letters() =>
        Assert.Equal("tags:aldu", (TrackTags.Artist | TrackTags.Album | TrackTags.Duration | TrackTags.Url).ToTagArgument());

    [Fact]
    public void Letters_do_not_depend_on_the_order_flags_were_combined()
    {
        var a = (TrackTags.Url | TrackTags.Artist).ToTagArgument();
        var b = (TrackTags.Artist | TrackTags.Url).ToTagArgument();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Every_track_flag_has_a_distinct_letter()
    {
        var letters = TrackTags.All.ToTagArgument()["tags:".Length..];
        Assert.Equal(letters.Length, letters.Distinct().Count());
        Assert.Equal(19, letters.Length);
    }

    [Fact]
    public void Album_tags_use_the_album_letters_not_the_track_ones()
    {
        // Artwork is 'j' on albums but 'J' on tracks; artist id is 'S' on albums but 's' on tracks.
        Assert.Equal("tags:jS", (AlbumTags.ArtworkTrackId | AlbumTags.ArtistId).ToTagArgument());
        Assert.Equal("tags:Js", (TrackTags.ArtworkTrackId | TrackTags.ArtistId).ToTagArgument());
    }

    [Fact]
    public void Default_album_list_asks_for_artwork_and_text_key() =>
        Assert.All(new[] { 'j', 's', 'y', 'a' }, c => Assert.Contains(c, AlbumTags.List.ToTagArgument()));
}
