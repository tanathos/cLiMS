namespace Clims.Tui.Settings;

/// <summary>Builders for the rows of the settings page.</summary>
public static class SettingItems
{
    public static SettingItem Toggle(string label, Func<UserSettings, bool> get, Func<UserSettings, bool, UserSettings> set, string hint = "", bool restart = false) => new()
    {
        Label = label,
        Kind = SettingKind.Toggle,
        Hint = hint,
        NeedsRestart = restart,
        Value = s => get(s) ? "on" : "off",
        Change = (s, _) => set(s, !get(s)),
    };

    /// <summary>One of <paramref name="options"/>; a step goes to the next (or previous) one, round the end.</summary>
    public static SettingItem Choice<T>(string label, Func<UserSettings, T> get, Func<UserSettings, T, UserSettings> set, IReadOnlyList<T> options, string hint = "", bool restart = false)
        where T : struct, Enum => new()
    {
        Label = label,
        Kind = SettingKind.Choice,
        Hint = hint,
        NeedsRestart = restart,
        Value = s => get(s).ToString(),
        Change = (s, delta) =>
        {
            var index = options.ToList().IndexOf(get(s));
            return set(s, options[((index < 0 ? 0 : index) + delta % options.Count + options.Count) % options.Count]);
        },
    };

    /// <summary>A number between <paramref name="min"/> and <paramref name="max"/>, changed by <paramref name="step"/>.</summary>
    /// <param name="zeroText">Shown instead of 0, when 0 has a meaning ("no limit", "off").</param>
    public static SettingItem Number(string label, Func<UserSettings, int> get, Func<UserSettings, int, UserSettings> set, int min, int max, int step, string? zeroText = null, string hint = "", bool restart = false) => new()
    {
        Label = label,
        Kind = SettingKind.Number,
        Hint = hint,
        NeedsRestart = restart,
        Value = s => get(s) == 0 && zeroText is not null ? zeroText : get(s).ToString(),
        Change = (s, delta) => set(s, Math.Clamp(get(s) + delta * step, min, max)),
    };

    public static SettingItem Text(string label, Func<UserSettings, string> get, Func<UserSettings, string, UserSettings> set, string hint = "") => new()
    {
        Label = label,
        Kind = SettingKind.Text,
        Hint = hint,
        Value = get,
        EditText = get,
        Edit = set,
    };

    public static SettingItem Info(string label, string value, string hint = "") => new() { Label = label, Kind = SettingKind.Info, Hint = hint, Value = _ => value };
}
