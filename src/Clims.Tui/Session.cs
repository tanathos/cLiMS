using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Catalog;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Waveform;
using Terminal.Gui.App;

namespace Clims.Tui;

/// <summary>Where to connect and with which login.</summary>
public sealed record Connection(Uri Uri, NetworkCredential? Credentials);

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
    private readonly CliEventStream? _events;
    private readonly Connection? _connection;
    private AppSettings _settings;

    /// <summary>Builds the services. A null <paramref name="connection"/> means demo mode.</summary>
    public Session(Connection? connection, AppSettings settings, string? playerId)
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
        var known = settings.LmsUrl == uri.ToString() ? settings.ServerUuid : null;
        var lms = new LmsClient(new LmsClientOptions { BaseUri = uri, Credentials = credentials }, _http);
        Library = lms.Library;
        Catalog = new CatalogService(lms, new CatalogServiceOptions
        {
            KnownServerUuid = known,
            StoreFactory = uuid =>
            {
                // Called once the UUID is known: from the saved settings, or after the server was actually reached. Only
                // then is the address worth remembering, so a mistyped --lms never overwrites a working one.
                _settings = _settings with { LmsUrl = uri.ToString(), ServerUuid = uuid };
                _settings.Save();
                return new SqliteCatalogStore(CatalogPaths.For(uuid));
            },
        });
        _covers = new LmsCoverSource(_http, uri, credentials);
        CoverArt.Source = _covers;
        Waves = new WaveformService(_http, uri);
        _events = new CliEventStream(EventOptions());
        var service = Catalog;
        _lmsPlayer = new LmsPlayerController(lms, () => service.Current, new LmsPlayerOptions { PlayerId = playerId ?? settings.PlayerId });
        _lmsPlayer.Notice += CrashLog.Info;
        Player = _lmsPlayer;
    }

    /// <summary>The catalogue service.</summary>
    public ICatalogService Catalog { get; }

    /// <summary>The player (a fake one in demo mode).</summary>
    public IPlayerController Player { get; }

    /// <summary>The server's own search, filling in what the local index does not have yet. Null in demo mode.</summary>
    public ILibraryApi? Library { get; }

    /// <summary>Waveforms, or null in demo mode.</summary>
    public WaveformService? Waves { get; }

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
        _http?.Dispose();
        _cancel.Dispose();
    }
}
