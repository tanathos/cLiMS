using Clims.Catalog;
using Clims.Lms;
using Clims.Tui;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Waveform;
using Terminal.Gui.App;

// Options:
//   --lms <url>      the server to use (also CLIMS_LMS_URL, or the address remembered from the last run)
//   --player <id>    the player to control (its id, usually the MAC address; default: the first connected hardware player)
//   --demo           built-in fake data, no server
//   --driver <name>  force a Terminal.Gui driver: windows | dotnet | ansi
string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

var driver = Arg("--driver");
var settings = AppSettings.Load();
var requested = Arg("--lms") ?? Environment.GetEnvironmentVariable("CLIMS_LMS_URL") ?? settings.LmsUrl;
var baseUri = args.Contains("--demo") ? null : AppSettings.NormalizeUrl(requested);

using var cancel = new CancellationTokenSource();
using var http = new HttpClient();
ICatalogService catalog;
IPlayerController player;
LmsPlayerController? lmsPlayer = null;
LmsCoverSource? covers = null;
WaveformService? waves = null;
CliEventStream? events = null;
ILibraryApi? library = null; // the server's own search fills in what the local index does not have yet

if (baseUri is null)
{
    catalog = new DemoCatalogService();
    var demo = catalog.Current!;
    var fake = new FakePlayer("piCorePlayer");
    fake.Play(demo.Albums[0], demo.TracksOf(demo.Albums[0].Id), 0);
    player = fake;
}
else
{
    // The saved index is found again through the server's UUID, remembered per address.
    var known = settings.LmsUrl == baseUri.ToString() ? settings.ServerUuid : null;

    var lms = new LmsClient(new LmsClientOptions { BaseUri = baseUri }, http);
    library = lms.Library;
    catalog = new CatalogService(lms, new CatalogServiceOptions
    {
        KnownServerUuid = known,
        StoreFactory = uuid =>
        {
            // Called once the UUID is known: from the saved settings, or after the server was actually reached. Only
            // then is the address worth remembering, so a mistyped --lms never overwrites a working one.
            settings = settings with { LmsUrl = baseUri.ToString(), ServerUuid = uuid };
            settings.Save();
            return new SqliteCatalogStore(CatalogPaths.For(uuid));
        },
    });
    covers = new LmsCoverSource(http, baseUri);
    CoverArt.Source = covers;
    waves = new WaveformService(http, baseUri);
    events = new CliEventStream(new CliEventStreamOptions { Host = baseUri.Host });
    var service = catalog;
    lmsPlayer = new LmsPlayerController(lms, () => service.Current, new LmsPlayerOptions { PlayerId = Arg("--player") ?? settings.PlayerId });
    lmsPlayer.Notice += CrashLog.Info;
    player = lmsPlayer;
}

try
{
    CrashLog.Info($"cLiMS starting ({(baseUri is null ? "demo" : baseUri.ToString())})");
    using var app = Application.Create().Init(driver);
    if (app.Driver is { } d)
    {
        d.Force16Colors = false;
    }

    SixelCovers.Driver = app.Driver;
    using var search = new SearchService(() => catalog.Current, library);
    using var main = new MainWindow(app, catalog, search, player, new SavedQueueStore(SavedQueueStore.DefaultPath), waves);

    // Start talking to the server only once the UI loop is running, so its events have somewhere to go.
    app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
    {
        lmsPlayer?.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                await catalog.StartAsync(cancel.Token);
                if (events is not null)
                {
                    await catalog.RunEventsAsync(events, cancel.Token);
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
        if (lmsPlayer is not null && events is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Its own connection: one enumerator per stream object.
                    await lmsPlayer.FollowEventsAsync(new CliEventStream(new CliEventStreamOptions { Host = baseUri!.Host }), cancel.Token);
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

        return false;
    });

    // Returning false from the handler lets the exception end the run; it is logged below.
    app.Run(main, ex =>
    {
        CrashLog.Write(ex);
        return false;
    });
    PerfStats.Dump();
}
catch (Exception ex)
{
    var path = CrashLog.Write(ex);
    Console.Error.WriteLine($"cLiMS crashed: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine($"Details written to {path}");
    return 1;
}
finally
{
    // Graceful shutdown: stop every background loop, then close what they use. Playback is left exactly as it is.
    cancel.Cancel();
    lmsPlayer?.Dispose();
    waves?.Dispose();
    catalog.Dispose();
    covers?.Dispose();
    CrashLog.Info("cLiMS stopped");
}

return 0;
