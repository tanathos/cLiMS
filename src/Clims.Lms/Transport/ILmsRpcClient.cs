using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Clims.Lms;

/// <summary>Raw access to <c>POST /jsonrpc.js</c>: send any LMS command, get the <c>result</c> object back.</summary>
public interface ILmsRpcClient
{
    /// <summary>
    /// Sends one command. The reply is the <c>result</c> element of the JSON-RPC response.
    /// </summary>
    /// <param name="playerId">Player MAC (or id) for player commands; null or empty for server-wide commands.</param>
    /// <param name="command">Command words and arguments, for example <c>"albums", 0, 50, "tags:ly"</c>. Numbers stay numbers.</param>
    /// <param name="kind">Whether the call is a side-effect free read (retryable) or a state-changing command.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="LmsConnectionException">The server could not be reached or timed out.</exception>
    /// <exception cref="LmsProtocolException">The reply was empty or unusable (LMS's way of saying "bad request").</exception>
    /// <exception cref="LmsAuthenticationException">Credentials were rejected.</exception>
    Task<JsonElement> RequestAsync(
        string? playerId,
        IReadOnlyList<object> command,
        RequestKind kind = RequestKind.Command,
        CancellationToken cancellationToken = default);
}
