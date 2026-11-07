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
