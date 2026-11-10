namespace Clims.Tui.Settings;

/// <summary>One tab of the settings page.</summary>
public sealed record SettingsPage(string Title, IReadOnlyList<SettingItem> Items);
