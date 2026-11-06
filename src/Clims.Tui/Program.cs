using Clims.Tui.Data;
using Clims.Tui.Ui;
using Terminal.Gui.App;

// Optional: --driver windows|dotnet|ansi (ansi works with redirected stdio, handy for headless smoke tests).
var driver = args.SkipWhile(a => a != "--driver").Skip(1).FirstOrDefault();

// Optional: --lms http://host:9000 loads 10 random real covers (read-only) to use in place of the fake ones.
var lms = args.SkipWhile(a => a != "--lms").Skip(1).FirstOrDefault();
if (lms is not null)
{
    try
    {
        CoverArt.Real = await DevCovers.LoadAsync(lms, 10);
        Console.WriteLine($"Loaded {CoverArt.Real.Count} covers from {lms}:");
        foreach (var cover in CoverArt.Real)
        {
            Console.WriteLine($"  {cover.Label}");
        }
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
    {
        Console.Error.WriteLine($"Could not load covers from {lms} ({ex.Message}); using fake covers.");
    }
}

var library = new FakeLibrary();
var player = new FakePlayer(library.GetAlbums(1)[0]);

try
{
    using var app = Application.Create().Init(driver);
    if (app.Driver is { } d)
    {
        d.Force16Colors = false;
    }

    SixelCovers.Driver = app.Driver;
    using var main = new MainWindow(app, library, player);

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

return 0;
