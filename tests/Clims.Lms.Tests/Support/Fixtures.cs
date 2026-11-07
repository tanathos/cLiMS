using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

public static class Fixtures
{
    public static string Load(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));
}
