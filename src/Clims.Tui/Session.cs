using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Catalog;
using Clims.Lms;
using Clims.Lyrics;
using Clims.Tui.Data;
using Clims.Tui.Settings;
using Clims.Tui.Ui;
using Clims.Waveform;
using Terminal.Gui.App;

namespace Clims.Tui;

/// <summary>
/// Everything that talks to one server (or the demo data), built from a <see cref="Connection"/> and torn down together.
/// Changing the server (F2) disposes one session and builds the next, so no service keeps the old address.
/// </summary>
public sealed class Session : IDisposable
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly HttpClient? _http;
    private readonly LmsPlayerController? _lmsPlayer;
    private readonly LmsCoverSource? _covers;
    private volatile CoverCacheStore? _coverCache;
    private readonly CliEventStream? _events;
    private readonly Connection? _connection;
    private readonly SettingsStore _settings;
    private HttpClient? _lyricsHttp;
    private ILmsRpcClient? _rpc;
    private LyricsStore? _lyricsStore;

    /// <summary>Builds the services. A null <paramref name="connection"/> means demo mode.</summary>
    public Session(Connection? connection, SettingsStore settings, string? playerId)
    {
        _connection = connection;
        _settings = settings;
        if (connection is null)
        {
            Catalog = new DemoCatalogService();
            var demo = Catalog.Current!;
            var fake = new FakePlayer("piCorePlayer");
            fake.Play(demo.Albums[0], demo.TracksOf(demo.Albums[0].Id), 0);
            Player = fake;
            return;
        }

        var uri = connection.Uri;
        var credentials = connection.Credentials;

        // One client for everything that downloads (JSON-RPC, covers, waveform files): every request carries the login.
        _http = new HttpClient();
        if (credentials is { UserName.Length: > 0 })
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.UserName}:{credentials.Password}")));
        }

        // The saved index is found again through the server's UUID, remembered per address.
        var server = settings.Current.Find(uri.ToString());
        var known = server?.Uuid;
        var lms = new LmsClient(new LmsClientOptions { BaseUri = uri, Credentials = credentials }, _http);
        Library = lms.Library;
        Catalog = new CatalogService(lms, new CatalogServiceOptions
        {
            KnownServerUuid = known,
            StoreFactory = uuid =>
            {
                // Called once the UUID is known: from the saved settings, or after the server was actually reached. Only
                // then is the address worth remembering, so a mistyped --lms never overwrites a working one.
                _settings.Update(s => s.WithServer(uri.ToString(), uuid));
                _coverCache = new CoverCacheStore(CatalogPaths.CoversFor(uuid));
                return new SqliteCatalogStore(CatalogPaths.For(uuid));
            },
        });
        _covers = new LmsCoverSource(_http, uri, credentials, cache: () => _coverCache, cacheSettings: settings.Current.Cache.Covers);
        CoverArt.Source = _covers;
        Waves = new WaveformService(_http, uri);
        if (server?.Lyrics.Enabled ?? true)
        {
            // Not the client above: that one carries the server login, which no other site may see.
            _rpc = lms.Rpc;
            _lyricsHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _lyricsStore = new LyricsStore(CatalogPaths.LyricsFile());
            Lyrics = new LyricsFeed(BuildLyrics(server?.Lyrics ?? new ServerLyricsSettings()), EnableOnlineLyrics);
        }

        _events = new CliEventStream(EventOptions());
        var service = Catalog;
        _lmsPlayer = new LmsPlayerController(lms, () => service.Current, new LmsPlayerOptions { PlayerId = playerId ?? server?.PlayerId });
        _lmsPlayer.Notice += CrashLog.Info;
        Player = _lmsPlayer;
        Online = new OnlineSource(new OnlineApi(lms.Rpc), () => _lmsPlayer.PlayerId);
    }

    /// <summary>The catalogue service.</summary>
    public ICatalogService Catalog { get; }

    /// <summary>The server's radios and apps (Spotty, TIDAL…), or null in demo mode.</summary>
    public OnlineSource? Online { get; }

    /// <summary>The player (a fake one in demo mode).</summary>
    public IPlayerController Player { get; }

    /// <summary>The server's own search, filling in what the local index does not have yet. Null in demo mode.</summary>
    public ILibraryApi? Library { get; }

    /// <summary>The newest albums, newest first: the server's own order (<c>sort:new</c>), or the last demo albums.</summary>
    public RecentAlbumsSource RecentAlbums => async (count, ct) =>
    {
        if (Library is null)
        {
            return Catalog.Current?.Albums.OrderByDescending(a => a.Id).Take(count).Select(a => a.Id).ToList() ?? new List<int>();
        }

        var page = await Library.GetAlbumsAsync(new AlbumQuery { Sort = AlbumSort.New }, 0, count, ct).ConfigureAwait(false);
        return page.Items.Select(a => a.Id).ToList();
    };

    /// <summary>Waveforms, or null in demo mode.</summary>
    public WaveformService? Waves { get; }

    /// <summary>The lyrics of the playing track, or null in demo mode or when lyrics are switched off in settings.</summary>
    public LyricsFeed? Lyrics { get; }

    private LyricsService BuildLyrics(ServerLyricsSettings lyrics)
    {
        var providers = new List<ILyricsProvider>();

        // Local sources first, whatever the online setting: a file next to the track (when a path map says where the music is as seen
        // from this PC), then the lyrics in the track's own tags (one small question to the server).
        if (lyrics.Files.PathMap is { Count: > 0 })
        {
            providers.Add(new SidecarLyricsProvider(lyrics.Files.PathMap, lyrics.Files.ShareUser, SecretStore.Unprotect(lyrics.Files.ProtectedSharePassword)));
        }

        if (_rpc is { } rpc)
        {
            providers.Add(new EmbeddedLyricsProvider((id, ct) => EmbeddedLyricsAsync(rpc, id, ct)));
        }

        if (lyrics.OnlineLookup && _lyricsHttp is { } http)
        {
            foreach (var name in (lyrics.Providers ?? []).Select(p => p.Trim().ToLowerInvariant()).Distinct())
            {
                switch (name)
                {
                    case "lrclib":
                        providers.Add(new LrclibProvider(http));
                        break;
                    case "darklyrics":
                        providers.Add(new DarkLyricsProvider(http));
                        break;
                    case "lyricsovh":
                        providers.Add(new LyricsOvhProvider(http));
                        break;
                }
            }
        }

        return new LyricsService(_lyricsStore!, providers, TimeSpan.FromDays(Math.Max(0, lyrics.MissRetryDays)));
    }

    /// <summary>The lyrics tag (<c>w</c>) of one track, straight from the server; null when it has none.</summary>
    private static async Task<string?> EmbeddedLyricsAsync(ILmsRpcClient rpc, int trackId, CancellationToken ct)
    {
        try
        {
            var reply = await rpc.RequestAsync(null, new object[] { "titles", 0, 1, "tags:w", $"track_id:{trackId}" }, RequestKind.Read, ct).ConfigureAwait(false);
            return reply.TryGetProperty("titles_loop", out var loop) && loop.ValueKind == System.Text.Json.JsonValueKind.Array && loop.GetArrayLength() > 0
                && loop[0].TryGetProperty("lyrics", out var lyrics) && lyrics.ValueKind == System.Text.Json.JsonValueKind.String
                ? lyrics.GetString()
                : null;
        }
        catch (LmsConnectionException ex)
        {
            // "The server could not be asked": the lyrics service does not remember that as "no lyrics".
            throw new HttpRequestException(ex.Message, ex);
        }
    }

    private void EnableOnlineLyrics()
    {
        var url = _connection!.Uri.ToString();
        var updated = _settings.Update(s => s.WithServer(url, change: server =>
            server with { Lyrics = server.Lyrics with { OnlineLookup = true, Providers = server.Lyrics.Providers is { Count: > 0 } ? server.Lyrics.Providers : ["lrclib"] } }));
        Lyrics?.Replace(BuildLyrics(updated.Find(url)!.Lyrics));
    }

    /// <summary>True when there is no server.</summary>
    public bool IsDemo => _connection is null;

    /// <summary>Starts talking to the server; call once the UI loop is running so events have somewhere to go.</summary>
    public void Start()
    {
        _lmsPlayer?.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                await Catalog.StartAsync(_cancel.Token);
                if (_events is not null)
                {
                    await Catalog.RunEventsAsync(_events, _cancel.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        });
        if (_lmsPlayer is not null && _events is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Its own connection: one enumerator per stream object.
                    await _lmsPlayer.FollowEventsAsync(new CliEventStream(EventOptions()), _cancel.Token);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down.
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                }
            });
        }
    }

    private CliEventStreamOptions EventOptions() => new() { Host = _connection!.Uri.Host, Credentials = _connection.Credentials };

    /// <summary>Stops every background loop, then closes what they use. Playback is left exactly as it is.</summary>
    public void Dispose()
    {
        _cancel.Cancel();
        _lmsPlayer?.Dispose();
        Waves?.Dispose();
        Catalog.Dispose();
        _covers?.Dispose();
        Lyrics?.Dispose();
        _lyricsHttp?.Dispose();
        _http?.Dispose();
        _cancel.Dispose();
    }
}
