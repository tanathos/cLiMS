using System.Net;
using Clims.Lms;
using Clims.Tui;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class ConnectModelTests
{
    private static async IAsyncEnumerable<DiscoveredServer> Found(params DiscoveredServer[] servers)
    {
        foreach (var s in servers)
        {
            yield return s;
            await Task.Yield();
        }
    }

    private static readonly DiscoveredServer Lms = new("Mnemonic Music", "192.168.1.20", 9000, "9.1.1", "u1", 9090);

    private static ConnectModel Model(ProbeResult? probe = null, Action<Uri, NetworkCredential?>? seen = null, params DiscoveredServer[] servers) =>
        new(_ => Found(servers), (uri, c, _) =>
        {
            seen?.Invoke(uri, c);
            return Task.FromResult(probe ?? new ProbeResult(ProbeOutcome.Connected, "Connected.", "9.1.1", "u1"));
        });

    [Fact]
    public async Task Servers_are_listed_as_they_answer_and_the_first_is_announced()
    {
        var model = Model(servers: [Lms, Lms with { Name = "Other", Host = "10.0.0.5", Uuid = "u2" }]);
        DiscoveredServer? first = null;
        model.FirstServerFound += s => first = s;
        await model.DiscoverAsync(CancellationToken.None);
        Assert.Equal(2, model.Servers.Count);
        Assert.Equal("Mnemonic Music", first!.Name);
        Assert.False(model.Searching);
        Assert.Contains("Pick a server", model.SearchLine);
    }

    [Fact]
    public async Task Finding_nothing_says_so_and_points_at_the_form()
    {
        var model = Model();
        await model.DiscoverAsync(CancellationToken.None);
        Assert.Empty(model.Servers);
        Assert.Contains("No server answered", model.SearchLine);
        Assert.Contains("Type its address", model.SearchLine);
    }

    [Fact]
    public async Task A_failing_search_does_not_break_the_form()
    {
        var model = new ConnectModel(_ => throw new System.Net.Sockets.SocketException(), (_, _, _) => throw new InvalidOperationException());
        await model.DiscoverAsync(CancellationToken.None);
        Assert.False(model.Searching);
    }

    [Fact]
    public async Task A_bare_host_gets_the_default_port_and_no_login_without_a_username()
    {
        Uri? uri = null;
        NetworkCredential? cred = new("x", "y");
        var model = Model(seen: (u, c) => (uri, cred) = (u, c));
        var result = await model.ConnectAsync("192.168.1.20", "", "ignored", CancellationToken.None);
        Assert.Equal(new Uri("http://192.168.1.20:9000/"), uri);
        Assert.Null(cred);
        Assert.Equal(ConnectChoice.Connected, result!.Choice);
        Assert.Equal("u1", result.Uuid);
        Assert.Null(result.Username);
    }

    [Fact]
    public async Task A_login_is_passed_on_and_returned()
    {
        NetworkCredential? cred = null;
        var model = Model(seen: (_, c) => cred = c);
        var result = await model.ConnectAsync("lms.local:9100", " me ", "pw", CancellationToken.None);
        Assert.Equal(("me", "pw"), (cred!.UserName, cred.Password));
        Assert.Equal(("me", "pw"), (result!.Username, result.Password));
        Assert.Equal(new Uri("http://lms.local:9100/"), result.Uri);
    }

    [Theory]
    [InlineData(ProbeOutcome.LoginRequired, "Wrong username or password.")]
    [InlineData(ProbeOutcome.Unreachable, "No LMS answered at that address.")]
    [InlineData(ProbeOutcome.NotLms, "not a Lyrion Music Server")]
    public async Task Failures_are_reported_in_plain_words_and_return_nothing(ProbeOutcome outcome, string text)
    {
        var model = Model(new ProbeResult(outcome, text));
        Assert.Null(await model.ConnectAsync("host", "", "", CancellationToken.None));
        Assert.Equal(outcome, model.LastOutcome);
        Assert.True(model.MessageIsError);
        Assert.Contains(text, model.Message);
        Assert.False(model.Busy);
    }

    [Theory]
    [InlineData("", "Type the address")]
    [InlineData("   ", "Type the address")]
    [InlineData("ftp://host", "does not look like an address")]
    public async Task Missing_or_unusable_addresses_never_reach_the_network(string address, string text)
    {
        var called = false;
        var model = Model(seen: (_, _) => called = true);
        Assert.Null(await model.ConnectAsync(address, "", "", CancellationToken.None));
        Assert.False(called);
        Assert.Contains(text, model.Message);
    }
}

public class SettingsAndSecretsTests
{
    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20:9000/")]
    [InlineData("192.168.1.20:9100", "http://192.168.1.20:9100/")]
    [InlineData("http://lms.local", "http://lms.local/")]
    public void The_default_port_is_added_only_to_a_bare_host(string input, string expected) =>
        Assert.Equal(expected, AppSettings.NormalizeUrl(input, 9000)!.ToString());

    [Fact]
    public void The_password_is_not_stored_in_plain_text_and_comes_back()
    {
        if (!SecretStore.CanStore)
        {
            Assert.Null(SecretStore.Protect("secret"));
            return;
        }

        var stored = SecretStore.Protect("s3cr3t-pässword")!;
        Assert.DoesNotContain("s3cr3t", stored);
        Assert.Equal("s3cr3t-pässword", SecretStore.Unprotect(stored));
        Assert.Null(SecretStore.Unprotect("not base64!"));
        Assert.Null(SecretStore.Unprotect(null));
        Assert.Null(SecretStore.Protect(""));
    }

    [Fact]
    public void Settings_json_never_contains_the_clear_password()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppSettings("http://h/", null, null, "me", SecretStore.Protect("hunter2")));
        Assert.DoesNotContain("hunter2", json);
        Assert.Contains("\"Username\":\"me\"", json);
    }
}
