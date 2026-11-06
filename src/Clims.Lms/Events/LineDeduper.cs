namespace Clims.Lms;

/// <summary>
/// Drops repeats of the same notification line. LMS often sends an event twice (or again about half a second later
/// after a seek or jump); the same line within <see cref="Window"/> counts as one event.
/// </summary>
public sealed class LineDeduper
{
    private readonly Dictionary<string, DateTimeOffset> _seen = new();

    /// <summary>Creates a deduper with the given window.</summary>
    public LineDeduper(TimeSpan window)
    {
        Window = window;
    }

    /// <summary>How long a line counts as a repeat after it was last seen.</summary>
    public TimeSpan Window { get; }

    /// <summary>True when <paramref name="line"/> was already seen within the window (and refreshes it otherwise).</summary>
    public bool IsDuplicate(string line, DateTimeOffset now)
    {
        if (_seen.Count > 256)
        {
            foreach (var key in _seen.Where(kv => now - kv.Value > Window).Select(kv => kv.Key).ToList())
            {
                _seen.Remove(key);
            }
        }

        if (_seen.TryGetValue(line, out var last) && now - last <= Window)
        {
            return true;
        }

        _seen[line] = now;
        return false;
    }
}
