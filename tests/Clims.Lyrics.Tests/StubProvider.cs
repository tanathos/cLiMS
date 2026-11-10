namespace Clims.Lyrics.Tests;

/// <summary>A provider that answers from a function (throw to simulate an unreachable provider) and counts its calls.</summary>
public sealed class StubProvider : ILyricsProvider
{
    private readonly Func<LyricsQuery, LyricsResult?> _answer;

    public StubProvider(string name, Func<LyricsQuery, LyricsResult?> answer)
    {
        Name = name;
        _answer = answer;
    }

    public string Name { get; }

    public int Calls { get; private set; }

    public Task<LyricsResult?> FindAsync(LyricsQuery query, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_answer(query));
    }
}
