using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

/// <summary>Runs only when <c>CLIMS_LMS_URL</c> points at a real server. Read-only tests only.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Integration.Url))
        {
            Skip = "Set CLIMS_LMS_URL (for example http://192.168.1.20:9000/) to run read-only integration tests.";
        }
    }
}
