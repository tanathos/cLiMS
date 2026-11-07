using System.Net;
using Clims.Lms;

namespace Clims.Tui.Ui;

/// <summary>What the connect screen produced.</summary>
public sealed record ConnectResult(ConnectChoice Choice, Uri? Uri = null, string? Username = null, string? Password = null, string? Uuid = null);
