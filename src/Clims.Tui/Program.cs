using System.Net;
using Clims.Catalog;
using Clims.Tui;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Terminal.Gui.App;

// Options:
//   --lms <url>              the server to use (also CLIMS_LMS_URL, or the address remembered from the last run)
//   --user <name>            login for a password-protected server (also CLIMS_LMS_USER)
//   --password-file <path>   file whose first line is the password (also CLIMS_LMS_PASSWORD). There is no --password:
//                            it would stay in the shell history and the process list
//   --player <id>            the player to control (its id, usually the MAC address; default: the first connected hardware player)
//   --demo                   built-in fake data, no server
//   --driver <name>          force a Terminal.Gui driver: windows | dotnet | ansi
//   --version                print the version and exit
// With no address from any of these, the connect screen opens (server search plus a form).
if (args.Contains("--version"))
{
    var v = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "unknown";
    Console.WriteLine($"cLiMS {v}");
    return 0;
}

string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

string? PasswordFromArgs()
{
    if (Arg("--password-file") is { Length: > 0 } file)
    {
        try
        {
            return File.ReadLines(file).FirstOrDefault()?.TrimEnd('\r', '\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read the password file: {ex.Message}");
            Environment.Exit(2);
        }
    }

    return Environment.GetEnvironmentVariable("CLIMS_LMS_PASSWORD");
}

var driver = Arg("--driver");
var settings = AppSettings.Load();
var requested = Arg("--lms") ?? Environment.GetEnvironmentVariable("CLIMS_LMS_URL") ?? settings.LmsUrl;
var demoOnly = args.Contains("--demo");
var uri = demoOnly ? null : AppSettings.NormalizeUrl(requested);

Connection? connection = null;
if (uri is not null)
{
    var user = Arg("--user") ?? Environment.GetEnvironmentVariable("CLIMS_LMS_USER") ?? settings.Username;
    var password = PasswordFromArgs() ?? SecretStore.Unprotect(settings.ProtectedPassword);
    connection = new Connection(uri, string.IsNullOrEmpty(user) ? null : new NetworkCredential(user, password ?? ""));
}

// Nothing to connect to and demo not asked for: the first thing the user sees is the connect screen.
var askFirst = connection is null && !demoOnly;
var exitCode = 0;
while (true)
{
    var changeServer = false;
    try
    {
        CrashLog.Info($"cLiMS starting ({(connection is null ? "demo" : connection.Uri.ToString())})");
        using var app = Application.Create().Init(driver);
        if (app.Driver is { } d)
        {
            d.Force16Colors = false;
        }

        SixelCovers.ReleaseAll();
        SixelCovers.Driver = app.Driver;
        // A fresh screen: images left by the previous window are not repainted by cell writes that did not change.
        Console.Out.Write("[2J[H");

        if (askFirst)
        {
            // First run: Esc means demo. Later (F2): Esc keeps what was running.
            var first = connection is null;
            var answer = ConnectWindow.Show(app, allowDemo: first, connection?.Uri.Authority, connection?.Credentials?.UserName);
            askFirst = false;
            if (answer.Choice == ConnectChoice.Cancelled && first)
            {
                break; // Ctrl+Q on the first screen leaves the app
            }

            if (answer.Choice == ConnectChoice.Connected)
            {
                connection = new Connection(answer.Uri!, string.IsNullOrEmpty(answer.Username) ? null : new NetworkCredential(answer.Username, answer.Password ?? ""));
                settings = settings with
                {
                    LmsUrl = answer.Uri!.ToString(),
                    ServerUuid = answer.Uuid ?? (settings.LmsUrl == answer.Uri.ToString() ? settings.ServerUuid : null),
                    Username = answer.Username,
                    ProtectedPassword = SecretStore.Protect(answer.Password),
                };
                settings.Save();
            }
            else if (answer.Choice == ConnectChoice.Demo)
            {
                connection = null;
            }
        }

        using var session = new Session(connection, settings, Arg("--player"));
        using var search = new SearchService(() => session.Catalog.Current, session.Library);
        using var main = new MainWindow(app, session.Catalog, search, session.Player, new SavedQueueStore(SavedQueueStore.DefaultPath), session.Waves, session.RecentAlbums);

        // Start talking to the server only once the UI loop is running, so its events have somewhere to go.
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            session.Start();
            return false;
        });

        // Returning false from the handler lets the exception end the run; it is logged below.
        app.Run(main, ex =>
        {
            CrashLog.Write(ex);
            return false;
        });
        changeServer = main.ChangeServerRequested;
        PerfStats.Dump();
    }
    catch (Exception ex)
    {
        var path = CrashLog.Write(ex);
        Console.Error.WriteLine($"cLiMS crashed: {ex.GetType().Name}: {ex.Message}");
        Console.Error.WriteLine($"Details written to {path}");
        exitCode = 1;
        break;
    }

    CrashLog.Info("cLiMS stopped");
    if (!changeServer)
    {
        break;
    }

    askFirst = true; // F2: back to the connect screen, with the current server filled in
}

return exitCode;
