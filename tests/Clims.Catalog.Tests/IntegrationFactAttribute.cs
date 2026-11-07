using Clims.Lms;

namespace Clims.Catalog.Tests;

/// <summary>Runs only when <c>CLIMS_LMS_URL</c> points at a real server. Read-only: the sync only issues queries.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIMS_LMS_URL")))
        {
            Skip = "Set CLIMS_LMS_URL (for example http://192.168.1.20:9000/) to run the read-only sync against a real server.";
        }
    }
}
