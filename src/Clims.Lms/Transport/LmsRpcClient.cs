using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Clims.Lms;

/// <summary>Whether a request may be repeated safely.</summary>
public enum RequestKind
{
    /// <summary>A query with no side effects. Retried after connection failures.</summary>
    Read,

    /// <summary>Changes server or player state. Never retried automatically.</summary>
    Command,
}

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

/// <summary>Default <see cref="ILmsRpcClient"/> over an injected <see cref="HttpClient"/>.</summary>
public sealed class LmsRpcClient : ILmsRpcClient
{
    private readonly HttpClient _http;
    private readonly LmsClientOptions _options;
    private readonly Uri _endpoint;
    private readonly SemaphoreSlim _gate;
    private readonly AuthenticationHeaderValue? _auth;
    private int _nextId;

    /// <summary>Creates a client. The <see cref="HttpClient"/> is not disposed by this class.</summary>
    public LmsRpcClient(HttpClient http, LmsClientOptions options)
    {
        _http = http;
        _options = options;
        _endpoint = new Uri(options.BaseUri, "jsonrpc.js");
        _gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
        if (options.Credentials is { } c)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{c.Password}"));
            _auth = new AuthenticationHeaderValue("Basic", token);
        }
    }

    /// <inheritdoc />
    public async Task<JsonElement> RequestAsync(
        string? playerId,
        IReadOnlyList<object> command,
        RequestKind kind = RequestKind.Command,
        CancellationToken cancellationToken = default)
    {
        var body = BuildBody(Interlocked.Increment(ref _nextId), playerId, command);
        var attempts = kind == RequestKind.Read ? 1 + Math.Max(0, _options.ReadRetries) : 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendAsync(body, playerId, command, cancellationToken).ConfigureAwait(false);
            }
            catch (LmsConnectionException) when (attempt < attempts)
            {
                var delay = TimeSpan.FromTicks(_options.RetryDelay.Ticks << (attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Builds the JSON-RPC body: <c>{"id":1,"method":"slim.request","params":[player,[cmd...]]}</c>.</summary>
    public static byte[] BuildBody(int id, string? playerId, IReadOnlyList<object> command)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("method", "slim.request");
            w.WriteStartArray("params");
            w.WriteStringValue(playerId ?? string.Empty);
            w.WriteStartArray();
            foreach (var item in command)
            {
                switch (item)
                {
                    case string s:
                        w.WriteStringValue(s);
                        break;
                    case int i:
                        w.WriteNumberValue(i);
                        break;
                    case long l:
                        w.WriteNumberValue(l);
                        break;
                    case double d:
                        w.WriteNumberValue(d);
                        break;
                    case bool b:
                        w.WriteStringValue(b ? "1" : "0");
                        break;
                    default:
                        w.WriteStringValue(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                }
            }

            w.WriteEndArray();
            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Short text form of a command for error messages, for example <c>albums 0 50 tags:ly</c>.</summary>
    public static string Describe(IReadOnlyList<object> command)
    {
        var text = string.Join(' ', command.Select(c => Convert.ToString(c, CultureInfo.InvariantCulture)));
        return text.Length <= 200 ? text : text[..200] + "...";
    }

    private async Task<JsonElement> SendAsync(
        byte[] body,
        string? playerId,
        IReadOnlyList<object> command,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_options.Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new ByteArrayContent(body),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Authorization = _auth;

            try
            {
                using var response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                return await ReadResponseAsync(response, playerId, command, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new LmsConnectionException($"LMS did not answer within {_options.Timeout.TotalSeconds:0} s ({Describe(command)}).");
            }
            catch (HttpRequestException ex)
            {
                throw Map(ex, playerId, command);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<JsonElement> ReadResponseAsync(
        HttpResponseMessage response,
        string? playerId,
        IReadOnlyList<object> command,
        CancellationToken ct)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new LmsAuthenticationException($"LMS rejected the credentials (HTTP {(int)response.StatusCode}).");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new LmsProtocolException($"LMS answered HTTP {(int)response.StatusCode} to '{Describe(command)}'.", Describe(command), playerId);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length == 0)
        {
            throw new LmsProtocolException($"LMS sent an empty reply to '{Describe(command)}'.", Describe(command), playerId);
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("result", out var result))
            {
                return result.Clone();
            }
        }
        catch (JsonException ex)
        {
            throw new LmsProtocolException($"LMS sent a reply that is not JSON for '{Describe(command)}'.", Describe(command), playerId, ex);
        }

        throw new LmsProtocolException($"LMS reply to '{Describe(command)}' has no result.", Describe(command), playerId);
    }

    private static LmsException Map(HttpRequestException ex, string? playerId, IReadOnlyList<object> command)
    {
        // Closing the connection without an HTTP reply is how LMS rejects a bad command, a bad player id or
        // an oversized body; that is a protocol problem, not a network one, and retrying will not help.
        var protocolIssue = ex.HttpRequestError is HttpRequestError.ResponseEnded or HttpRequestError.InvalidResponse
            or HttpRequestError.HttpProtocolError;
        return protocolIssue
            ? new LmsProtocolException($"LMS closed the connection without answering '{Describe(command)}' (unknown command or player, or request too large).", Describe(command), playerId, ex)
            : new LmsConnectionException($"Cannot reach LMS: {ex.Message}", ex);
    }
}
