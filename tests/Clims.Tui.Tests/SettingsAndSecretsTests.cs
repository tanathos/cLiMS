using System.Net;
using Clims.Lms;
using Clims.Tui;
using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

public class SettingsAndSecretsTests
{
    [Theory]
    [InlineData("192.168.1.20", "http://192.168.1.20:9000/")]
    [InlineData("192.168.1.20:9100", "http://192.168.1.20:9100/")]
    [InlineData("http://lms.local", "http://lms.local/")]
    public void The_default_port_is_added_only_to_a_bare_host(string input, string expected) =>
        Assert.Equal(expected, ServerUrl.Normalize(input, 9000)!.ToString());

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
        var server = new ServerSettings { Url = "http://h/", Username = "me", ProtectedPassword = SecretStore.Protect("hunter2") };
        var json = System.Text.Json.JsonSerializer.Serialize(new UserSettings { Servers = [server] }, SettingsJson.Options);
        Assert.DoesNotContain("hunter2", json);
        Assert.Contains("\"Username\": \"me\"", json);
    }
}
