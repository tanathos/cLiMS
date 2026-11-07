using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

public class LineDeduperTests
{
    [Fact]
    public void The_same_line_within_the_window_is_a_duplicate()
    {
        var d = new LineDeduper(TimeSpan.FromMilliseconds(250));
        var t = DateTimeOffset.UtcNow;
        Assert.False(d.IsDuplicate("a playlist newsong x 1", t));
        Assert.True(d.IsDuplicate("a playlist newsong x 1", t.AddMilliseconds(100)));
    }

    [Fact]
    public void The_same_line_after_the_window_is_new_again()
    {
        var d = new LineDeduper(TimeSpan.FromMilliseconds(250));
        var t = DateTimeOffset.UtcNow;
        d.IsDuplicate("x", t);
        Assert.False(d.IsDuplicate("x", t.AddMilliseconds(400)));
    }

    [Fact]
    public void Different_lines_are_independent()
    {
        var d = new LineDeduper(TimeSpan.FromSeconds(1));
        var t = DateTimeOffset.UtcNow;
        Assert.False(d.IsDuplicate("x", t));
        Assert.False(d.IsDuplicate("y", t));
    }

    [Fact]
    public void The_seen_table_does_not_grow_forever()
    {
        var d = new LineDeduper(TimeSpan.FromMilliseconds(10));
        var t = DateTimeOffset.UtcNow;
        for (var i = 0; i < 2000; i++)
        {
            d.IsDuplicate("line " + i, t.AddMilliseconds(i * 50));
        }

        Assert.False(d.IsDuplicate("line 0", t.AddSeconds(500)));
    }
}
