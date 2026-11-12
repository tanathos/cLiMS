using System.Text.Json;
using Clims.Catalog;

namespace Clims.Tui.Settings;

/// <summary>
/// Reads and writes settings.json. A version 1 file is renamed to settings.v1.json (a numbered name if that exists) and the
/// converted settings are saved under the canonical name. The backup is never touched again.
/// </summary>
public sealed class SettingsStore
{
    private readonly object _lock = new();

    /// <param name="folder">Where the file lives (the per-user app data folder when null).</param>
    public SettingsStore(string? folder = null) => Folder = folder ?? CatalogPaths.DataFolder;

    public string Folder { get; }

    public string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>The settings as loaded or last changed through <see cref="Update"/> (the defaults before <see cref="Load"/>).</summary>
    public UserSettings Current { get; private set; } = new();

    /// <summary>Changes the current settings and saves them.</summary>
    public UserSettings Update(Func<UserSettings, UserSettings> change)
    {
        lock (_lock)
        {
            Current = change(Current);
            Save(Current);
            return Current;
        }
    }

    /// <summary>The settings, converted from version 1 when needed (they become <see cref="Current"/>). Never throws: unreadable files give the defaults.</summary>
    public UserSettings Load()
    {
        lock (_lock)
        {
            Current = Read();
            return Current;
        }
    }

    private UserSettings Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new UserSettings();
            }

            var text = File.ReadAllText(FilePath);
            UserSettings settings;
            bool migrate;
            try
            {
                migrate = SettingsMigration.IsOldVersion(text);
                settings = migrate ? SettingsMigration.FromV1(text) : JsonSerializer.Deserialize<UserSettings>(text, SettingsJson.Options) ?? new UserSettings();
            }
            catch (JsonException)
            {
                // Not readable: keep it as the backup and start from the defaults.
                Backup();
                return new UserSettings();
            }

            if (migrate)
            {
                Backup();
                Write(settings);
            }
            else if (ProtectSharePasswords(settings) is { } protectedSettings)
            {
                settings = protectedSettings;
                Write(settings);
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    /// <summary>
    /// A share password typed into the file as plain text is encrypted and removed (where encryption is possible).
    /// Returns the changed settings, or null when there was nothing to protect.
    /// </summary>
    private static UserSettings? ProtectSharePasswords(UserSettings settings)
    {
        var changed = false;
        var servers = settings.Servers.Select(server =>
        {
            var files = server.Lyrics.Files;
            if (files.SharePassword is not { Length: > 0 } plain || SecretStore.Protect(plain) is not { } protectedValue)
            {
                return server;
            }

            changed = true;
            return server with { Lyrics = server.Lyrics with { Files = files with { SharePassword = null, ProtectedSharePassword = protectedValue } } };
        }).ToList();

        return changed ? settings with { Servers = servers } : null;
    }

    /// <summary>Writes the file (indented), through a temporary file so that a failure cannot leave it half written.</summary>
    public void Save(UserSettings settings)
    {
        lock (_lock)
        {
            try
            {
                Write(settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Settings are a convenience; failing to save them must not stop the app.
            }
        }
    }

    /// <summary>The name the old file is moved to: settings.v1.json, else settings.v1.1.json, settings.v1.2.json...</summary>
    public string BackupPath()
    {
        var path = Path.Combine(Folder, "settings.v1.json");
        for (var n = 1; File.Exists(path); n++)
        {
            path = Path.Combine(Folder, $"settings.v1.{n}.json");
        }

        return path;
    }

    private void Backup() => File.Move(FilePath, BackupPath());

    private void Write(UserSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJson.Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
