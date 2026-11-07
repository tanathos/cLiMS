using System.Net;

namespace Clims.Lms;

/// <summary>What a connection check found.</summary>
public enum ProbeOutcome
{
    /// <summary>The server answered and accepted the credentials (if any).</summary>
    Connected,

    /// <summary>The server wants a username and password, or rejected the ones given.</summary>
    LoginRequired,

    /// <summary>Nothing answered at that address (refused, unknown host, timeout).</summary>
    Unreachable,

    /// <summary>Something answered, but it is not a Lyrion Music Server.</summary>
    NotLms,
}

/// <summary>Result of <see cref="ServerProbe.CheckAsync"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">A sentence for the user.</param>
/// <param name="Version">Server version when connected.</param>
/// <param name="Uuid">Server UUID when connected.</param>
public sealed record ProbeResult(ProbeOutcome Outcome, string Message, string? Version = null, string? Uuid = null);

/// <summary>A read-only check (<c>serverstatus</c>) that an address is a reachable LMS and that the login works.</summary>
public static class ServerProbe
{
    /// <summary>Runs the check.</summary>
    public static async Task<ProbeResult> CheckAsync(
        Uri baseUri,
        NetworkCredential? credentials,
        HttpClient? http = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var client = new LmsClient(
            new LmsClientOptions { BaseUri = baseUri, Credentials = credentials, ReadRetries = 0, Timeout = timeout ?? TimeSpan.FromSeconds(8) },
            http);
        try
        {
            var status = await client.Server.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return new ProbeResult(ProbeOutcome.Connected, "Connected.", status.Version, status.Uuid);
        }
        catch (LmsAuthenticationException)
        {
            return new ProbeResult(
                ProbeOutcome.LoginRequired,
                credentials is null ? "This server asks for a username and password." : "Wrong username or password.");
        }
        catch (LmsConnectionException)
        {
            return new ProbeResult(ProbeOutcome.Unreachable, "No LMS answered at that address.");
        }
        catch (LmsProtocolException)
        {
            return new ProbeResult(ProbeOutcome.NotLms, "That address answers, but it is not a Lyrion Music Server.");
        }
    }
}
