using System.Net;

namespace Clims.Lms;

/// <summary>Connection settings for <see cref="LmsClient"/>.</summary>
public sealed class LmsClientOptions
{
    /// <summary>Base address of the LMS web server, for example <c>http://192.168.1.20:9000/</c>.</summary>
    public required Uri BaseUri { get; init; }

    /// <summary>Optional HTTP Basic credentials, needed when LMS password protection is enabled.</summary>
    public NetworkCredential? Credentials { get; init; }

    /// <summary>Per-request timeout. LMS can take a long time while scanning, so this is generous.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How many requests may be in flight at once. LMS is mostly single-threaded, so 2 to 4 is plenty and more
    /// only slows it down.
    /// </summary>
    public int MaxConcurrency { get; init; } = 3;

    /// <summary>How many times an idempotent read is retried after a <see cref="LmsConnectionException"/>.</summary>
    public int ReadRetries { get; init; } = 2;

    /// <summary>First retry delay; doubled for each further retry.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Largest number of ids sent in one <c>playlistcontrol</c> call (the request body limit is about 8 KB).</summary>
    public int MaxIdsPerRequest { get; init; } = 400;
}
