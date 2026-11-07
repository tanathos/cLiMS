using System.Net;
using Clims.Lms;
using NAudio.Wave;

namespace Clims.Waveform.Tests;

/// <summary>Runs only when <c>CLIMS_LMS_URL</c> points at a real server: downloads one real track and draws it.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIMS_LMS_URL")))
        {
            Skip = "Set CLIMS_LMS_URL to analyse a real track.";
        }
    }
}
