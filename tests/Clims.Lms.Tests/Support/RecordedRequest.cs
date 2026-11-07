using System.Net;
using System.Text;
using System.Text.Json;

namespace Clims.Lms.Tests;

/// <summary>One request captured by <see cref="FakeHandler"/>, with the JSON-RPC body already taken apart.</summary>
public sealed class RecordedRequest
{
    public RecordedRequest(HttpRequestMessage message, string body)
    {
        Message = message;
        Body = body;
        using var doc = JsonDocument.Parse(body);
        var parameters = doc.RootElement.GetProperty("params");
        PlayerId = parameters[0].GetString() ?? string.Empty;
        Command = parameters[1].EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()).ToArray();
        Id = doc.RootElement.GetProperty("id").GetInt32();
    }

    public HttpRequestMessage Message { get; }

    public string Body { get; }

    public string PlayerId { get; }

    public string[] Command { get; }

    public int Id { get; }

    public string Text => string.Join(' ', Command);
}
