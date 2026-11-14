namespace Clims.Info.Tests;

public class InfoServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clims-tests-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private static ArtistInfo Artist(string provider, string name) =>
        new(provider, "id", name, null, null, null, null, null, false, null, null, [], [], [], []);

    private sealed class FakeProvider(string name, Func<ArtistInfo?> artist) : IInfoProvider
    {
        public int Asked { get; private set; }

        public InfoQuery? LastQuery { get; private set; }

        public string Name => name;

        public Task<ArtistInfo?> GetArtistAsync(InfoQuery query, CancellationToken ct)
        {
            Asked++;
            LastQuery = query;
            return Task.FromResult(artist());
        }

        public Task<AlbumInfo?> GetAlbumAsync(InfoQuery query, CancellationToken ct) => Task.FromResult<AlbumInfo?>(null);
    }

    private InfoService Service(params IInfoProvider[] providers) =>
        new(providers, new InfoStore(Path.Combine(_folder, "info.db")), new HttpClient(new FakeHandler()), () => _now);

    [Fact]
    public async Task WhatProvidersKnowIsMergedTheEarlierOneFirst()
    {
        var first = new FakeProvider("first", () => Artist("first", "Band") with { Kind = "Group", WikidataId = "Q1" });
        var second = new FakeProvider("second", () => Artist("second", "Band") with { Kind = "Person", Biography = "Long text", BiographySource = "second" });

        var artist = await Service(first, second).ArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None);

        Assert.Equal("first, second", artist!.Provider);
        Assert.Equal("Group", artist.Kind);
        Assert.Equal(("Long text", "second", "Q1"), (artist.Biography, artist.BiographySource, artist.WikidataId));
    }

    [Fact]
    public async Task ALaterProviderIsToldTheWikidataEntryTheEarlierOneFound()
    {
        var first = new FakeProvider("first", () => Artist("first", "Band") with { WikidataId = "Q7" });
        var second = new FakeProvider("second", () => null);

        await Service(first, second).ArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None);

        Assert.Equal("Q7", second.LastQuery!.WikidataId);
    }

    [Fact]
    public async Task ARefreshAsksAgainAndReplacesTheStoredAnswer()
    {
        var text = "one";
        var provider = new FakeProvider("p", () => Artist("p", "Band") with { Biography = text });
        var service = Service(provider);
        var query = new InfoQuery("Band", "Album");

        await service.ArtistAsync(query, CancellationToken.None);
        text = "two";
        Assert.Equal("one", (await service.ArtistAsync(query, CancellationToken.None))!.Biography);
        Assert.Equal("two", (await service.ArtistAsync(query, CancellationToken.None, refresh: true))!.Biography);
        Assert.Equal("two", (await service.ArtistAsync(query, CancellationToken.None))!.Biography);
    }

    [Fact]
    public async Task AnAnswerMissingWhatAnUnreachableProviderKnowsIsNotStored()
    {
        var up = new FakeProvider("up", () => Artist("up", "Band"));
        var down = new FakeProvider("down", () => throw new HttpRequestException("busy"));
        var service = Service(up, down);

        Assert.NotNull(await service.ArtistAsync(new InfoQuery("Band", "A"), CancellationToken.None));
        Assert.NotNull(await service.ArtistAsync(new InfoQuery("Band", "A"), CancellationToken.None));
        Assert.Equal(2, up.Asked);
    }

    [Fact]
    public async Task AnAnswerIsStoredAndNotAskedAgainUntilItExpires()
    {
        var provider = new FakeProvider("p", () => Artist("p", "Band"));
        var service = Service(provider);
        var query = new InfoQuery("Band", "Album");

        await service.ArtistAsync(query, CancellationToken.None);
        await service.ArtistAsync(new InfoQuery("BAND", "Other album"), CancellationToken.None);
        Assert.Equal(1, provider.Asked);

        _now = _now.Add(InfoService.Lifetime).AddDays(1);
        await service.ArtistAsync(query, CancellationToken.None);
        Assert.Equal(2, provider.Asked);
    }


    [Fact]
    public async Task TheLogTellsWhichProvidersMatchedAndWhenTheStoredAnswerWasUsed()
    {
        var lines = new List<string>();
        var service = new InfoService([new FakeProvider("a", () => null), new FakeProvider("b", () => Artist("b", "Band"))], new InfoStore(Path.Combine(_folder, "log.db")), new HttpClient(new FakeHandler()), () => _now, lines.Add);

        await service.ArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None);
        await service.ArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None);

        Assert.Contains(lines, l => l.Contains("asking a, b"));
        Assert.Contains(lines, l => l.Contains("a has no match"));
        Assert.Contains(lines, l => l.Contains("b matched"));
        Assert.Contains(lines, l => l.Contains("stored answer used"));
    }
    [Fact]
    public async Task NothingKnownIsNotStored()
    {
        var provider = new FakeProvider("p", () => null);
        var service = Service(provider);

        Assert.Null(await service.ArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None));
        Assert.Null(await service.ArtistAsync(new InfoQuery("Band", "Album"), CancellationToken.None));
        Assert.Equal(2, provider.Asked);
    }

    [Fact]
    public async Task AProviderThatCannotBeReachedIsReportedOnlyWhenNoOtherKnows()
    {
        var down = new FakeProvider("down", () => throw new HttpRequestException("busy"));
        var up = new FakeProvider("up", () => Artist("up", "Band"));

        Assert.Equal("up", (await Service(down, up).ArtistAsync(new InfoQuery("Band", "A"), CancellationToken.None))!.Provider);
        await Assert.ThrowsAsync<HttpRequestException>(() => Service(down).ArtistAsync(new InfoQuery("Other", "A"), CancellationToken.None));
    }

    [Fact]
    public void TheStoreKeepsTextAndBytesUntilTheyExpire()
    {
        var store = new InfoStore(Path.Combine(_folder, "s.db"));
        store.PutText("t", "hello", _now);
        store.PutBytes("b", [1, 2, 3], _now);

        Assert.Equal("hello", store.GetText("t", TimeSpan.FromDays(1), _now.AddHours(1)));
        Assert.Equal<byte>([1, 2, 3], store.GetBytes("b", TimeSpan.FromDays(1), _now.AddHours(1))!);
        Assert.Null(store.GetText("t", TimeSpan.FromDays(1), _now.AddDays(2)));
        Assert.Null(store.GetText("b", TimeSpan.FromDays(1), _now));
    }
}
