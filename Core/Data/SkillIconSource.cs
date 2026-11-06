using System.Text.RegularExpressions;

namespace GaugeOverlay.Core.Data;

/// <summary>Where a skill's icon comes from (skill-icons.plan.md): Smilegate's own CDN, fetched on demand for a skill this PC never
/// captured, and kept under the app's folder. A record's icon path may come from an imported file, so only a plain
/// "atlas/file.png" of lower-case letters, digits and underscores is an icon: never a path out of the folder, never another host.</summary>
public static partial class SkillIconSource
{
    const string Host = "https://cdn-lostark.game.onstove.com/efui_iconatlas/";

    // \z, not $: "$" also matches before a final newline.
    [GeneratedRegex(@"^[a-z][a-z_]{1,24}/[a-z0-9_]{1,40}\.png\z")]
    private static partial Regex Pattern();

    // Names Windows keeps for devices, as a folder or a file name.
    [GeneratedRegex(@"^(con|prn|aux|nul|com\d|lpt\d)$")]
    private static partial Regex Reserved();

    public static bool IsValid(string? icon) =>
        icon is not null && Pattern().IsMatch(icon) && icon[..^4].Split('/').All(part => !Reserved().IsMatch(part));

    public static Uri Url(string icon) => new(Host + Checked(icon));

    /// <summary>The icon's file under <paramref name="root"/> (%APPDATA%\GaugeOverlay\skillicons).</summary>
    public static string LocalPath(string root, string icon)
    {
        var parts = Checked(icon).Split('/');
        return Path.Combine(root, parts[0], parts[1]);
    }

    static string Checked(string icon) => IsValid(icon) ? icon : throw new ArgumentException($"not a skill icon: {icon}", nameof(icon));
}
