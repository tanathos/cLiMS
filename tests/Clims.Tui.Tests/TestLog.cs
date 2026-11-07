using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Catalog;
using MusicCatalog = Clims.Catalog.Catalog;

namespace Clims.Tui.Tests;

internal static class TestLog
{
    [ModuleInitializer]
    internal static void Redirect() =>
        CrashLog.Path = Path.Combine(Path.GetTempPath(), "clims-tui-tests.log");
}
