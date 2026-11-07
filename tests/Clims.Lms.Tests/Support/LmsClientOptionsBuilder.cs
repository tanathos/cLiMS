using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

public sealed class LmsClientOptionsBuilder
{
    public System.Net.NetworkCredential? Credentials { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxConcurrency { get; set; } = 3;

    public int ReadRetries { get; set; } = 2;

    public int MaxIdsPerRequest { get; set; } = 400;
}
