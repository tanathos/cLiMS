using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

public static class Integration
{
    public static string? Url => Environment.GetEnvironmentVariable("CLIMS_LMS_URL");

    public static string? PlayerId => Environment.GetEnvironmentVariable("CLIMS_LMS_PLAYER");

    public static LmsClient CreateClient() =>
        new(new LmsClientOptions { BaseUri = new Uri(Url!.EndsWith('/') ? Url : Url + "/") });
}
