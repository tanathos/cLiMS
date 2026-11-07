using System.Text.Json;
using Clims.Lms.Json;

namespace Clims.Lms.Tests;

public class JsonReadingTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Numbers_are_read_from_numbers_and_numeric_strings()
    {
        var e = Parse("""{"a":5,"b":"7","c":"x","d":2015.0,"e":null}""");
        Assert.Equal(5, e.GetInt("a"));
        Assert.Equal(7, e.GetInt("b"));
        Assert.Equal(-1, e.GetInt("c", -1));
        Assert.Equal(2015, e.GetInt("d"));
        Assert.Null(e.GetIntOrNull("e"));
        Assert.Null(e.GetIntOrNull("missing"));
    }

    [Fact]
    public void Doubles_survive_as_number_or_string_and_ignore_locale()
    {
        var e = Parse("""{"time":78.9679701118469,"duration":"421.906"}""");
        Assert.Equal(78.9679701118469, e.GetDouble("time"));
        Assert.Equal(421.906, e.GetDouble("duration"));
        Assert.Equal(TimeSpan.FromSeconds(421.906), e.GetSeconds("duration"));
    }

    [Theory]
    [InlineData("""{"v":1}""", true)]
    [InlineData("""{"v":"1"}""", true)]
    [InlineData("""{"v":true}""", true)]
    [InlineData("""{"v":0}""", false)]
    [InlineData("""{"v":"0"}""", false)]
    [InlineData("""{"v":false}""", false)]
    [InlineData("""{"v":null}""", false)]
    [InlineData("""{}""", false)]
    public void Booleans_accept_the_many_ways_LMS_writes_them(string json, bool expected) =>
        Assert.Equal(expected, Parse(json).GetBool("v"));

    [Fact]
    public void Strings_come_back_for_numbers_too()
    {
        var e = Parse("""{"id":853519,"year":"2015","none":null}""");
        Assert.Equal("853519", e.GetStringOrNull("id"));
        Assert.Equal("2015", e.GetStringOrNull("year"));
        Assert.Null(e.GetStringOrNull("none"));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"count":0}""")]
    [InlineData("""{"x_loop":null}""")]
    public void A_missing_loop_is_an_empty_sequence(string json) =>
        Assert.Empty(Parse(json).GetLoop("x_loop"));

    [Fact]
    public void A_present_loop_is_enumerated() =>
        Assert.Equal(2, Parse("""{"x_loop":[{"id":1},{"id":2}]}""").GetLoop("x_loop").Count());
}
