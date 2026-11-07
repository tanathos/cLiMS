using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Catalog;
using Clims.Lms;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Clims.Waveform;
using Terminal.Gui.App;

namespace Clims.Tui;

/// <summary>Where to connect and with which login.</summary>
public sealed record Connection(Uri Uri, NetworkCredential? Credentials);
