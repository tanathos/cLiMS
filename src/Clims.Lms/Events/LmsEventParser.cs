using System.Globalization;
using System.Text.RegularExpressions;

namespace Clims.Lms;

/// <summary>
/// Parses lines of the LMS telnet CLI notification stream. Lines are space separated words, each percent-encoded
/// (<c>b8%3A27%3Aeb...</c>). A line starts with the player id for player events and with a plain word for server events.
/// Everything the UI does not need (skin chatter, preference changes, query echoes) maps to null.
/// </summary>
public static partial class LmsEventParser
{
    private static readonly string[] QueueWords =
    {
        "addtracks", "inserttracks", "loadtracks", "deletetracks", "delete", "deleteitem", "move", "clear",
        "load_done", "add", "insert", "load", "play", "resume", "append",
    };

    /// <summary>Splits a raw line into decoded words.</summary>
    public static string[] Tokenize(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Decode).ToArray();

    /// <summary>Decodes one percent-encoded word (UTF-8). Malformed escapes are left as they are.</summary>
    public static string Decode(string word)
    {
        try
        {
            return Uri.UnescapeDataString(word);
        }
        catch (UriFormatException)
        {
            return word;
        }
    }

    /// <summary>Parses a raw line. Returns null for noise or anything unrecognised.</summary>
    public static LmsEvent? Parse(string line) => Parse(Tokenize(line));

    /// <summary>Parses already decoded words.</summary>
    public static LmsEvent? Parse(string[] t)
    {
        if (t.Length == 0)
        {
            return null;
        }

        if (!IsPlayerId(t[0]))
        {
            return ParseServer(t);
        }

        var player = t[0];
        if (t.Length < 2)
        {
            return null;
        }

        switch (t[1])
        {
            case "playlist":
                return ParsePlaylist(player, t);
            case "playlistcontrol":
                return new QueueChangedEvent(player);
            case "play":
                return new PlaybackStateEvent(player, PlayMode.Play);
            case "stop":
                return new PlaybackStateEvent(player, PlayMode.Stop);
            case "pause":
                return new PlaybackStateEvent(player, t.Length > 2 ? (t[2] == "1" ? PlayMode.Pause : PlayMode.Play) : null);
            case "mixer" when t.Length > 3 && t[2] == "volume":
                return new VolumeChangedEvent(player, int.TryParse(t[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && t[3][0] is not ('+' or '-') ? Math.Clamp(v, 0, 100) : null);
            case "time" when t.Length > 2 && double.TryParse(t[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var secs):
                return new PositionChangedEvent(player, TimeSpan.FromSeconds(Math.Max(0, secs)));
            case "power":
                return new PowerChangedEvent(player, t.Length > 2 && t[2] == "1");
            case "client" when t.Length > 2:
                return t[2] switch
                {
                    "disconnect" or "forget" => new PlayerConnectionEvent(player, false),
                    "new" or "reconnect" => new PlayerConnectionEvent(player, true),
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>True for a MAC address or an IPv4 address, the two forms LMS uses as player ids.</summary>
    public static bool IsPlayerId(string word) => PlayerIdPattern().IsMatch(word);

    private static LmsEvent? ParseServer(string[] t) => (t[0], t.Length > 1 ? t[1] : "") switch
    {
        ("rescan", "done") => new ScanFinishedEvent(),
        ("library", "changed") => new LibraryChangedEvent(),
        _ => null,
    };

    private static LmsEvent? ParsePlaylist(string player, string[] t)
    {
        if (t.Length < 3)
        {
            return null;
        }

        var word = t[2];
        switch (word)
        {
            case "newsong":
            {
                // playlist newsong <title> <index>: the title is one encoded word, the index the last word.
                var rest = t.Skip(3).ToArray();
                int? index = rest.Length > 0 && int.TryParse(rest[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
                var title = string.Join(' ', index is null ? rest : rest[..^1]);
                return new TrackChangedEvent(player, index, title.Length > 0 ? title : null);
            }

            case "open" when t.Length > 3:
                return new TrackOpenedEvent(player, string.Join(' ', t.Skip(3)));
            case "stop":
                return new PlaybackStateEvent(player, PlayMode.Stop);
            case "pause":
                return new PlaybackStateEvent(player, t.Length > 3 ? (t[3] == "1" ? PlayMode.Pause : PlayMode.Play) : null);
            case "repeat" or "shuffle":
                return new ModeChangedEvent(player);
            default:
                return Array.IndexOf(QueueWords, word) >= 0 ? new QueueChangedEvent(player) : null;
        }
    }

    [GeneratedRegex(@"^(?:[0-9a-fA-F]{2}(?::[0-9a-fA-F]{2}){5}|\d{1,3}(?:\.\d{1,3}){3})$")]
    private static partial Regex PlayerIdPattern();
}
