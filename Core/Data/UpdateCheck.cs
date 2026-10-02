using System.Security.Cryptography;
using System.Text.Json;

namespace GaugeOverlay.Core.Data;

/// <summary>A release that can be installed: its version, tag, the zip and .sha256 download links, and its notes (markdown,
/// shown once after the update: <see cref="UpdateCheck.PlainNotes"/>).</summary>
public sealed record Release(Version Version, string Tag, string ZipUrl, string ShaUrl, string Notes = "");

/// <summary>업데이트 확인: the latest release of Lostark-overlay (GitHub's public API, no login), whether it's newer than the
/// running version, and whether a downloaded zip matches its .sha256.</summary>
public static class UpdateCheck
{
    public const string LatestUrl = "https://api.github.com/repos/LazyUI-noT/Lostark-overlay/releases/latest";
    /// <summary>Downloads are taken only from this repo's releases, whatever the answer says.</summary>
    public const string DownloadPrefix = "https://github.com/LazyUI-noT/Lostark-overlay/releases/download/";

    /// <summary>The latest-release answer, or null when it's no installable release: a draft, a prerelease, a tag that isn't
    /// "vX.Y.Z", no GaugeOverlay-*.zip with its .sha256, a link outside <paramref name="prefix"/> (the repo's releases), or
    /// not a release at all.</summary>
    public static Release? Parse(string json, string prefix = DownloadPrefix)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tagEl)
                || Flag(root, "draft") || Flag(root, "prerelease") || !root.TryGetProperty("assets", out var assets)) return null;
            string tag = tagEl.GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;

            var links = assets.EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                .Where(a => a.Url.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(a => a.Name, a => a.Url);
            var zip = links.Keys.FirstOrDefault(n => n.StartsWith("GaugeOverlay-", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal));
            if (zip is null || !links.TryGetValue(zip + ".sha256", out var sha)) return null;
            string notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString()! : "";
            return new Release(version, tag, links[zip], sha, notes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException) { return null; }
    }

    static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Compares major.minor.build only: the exe's version carries a revision (1.0.5.0), a tag doesn't (v1.0.5).</summary>
    public static bool IsNewer(Version latest, Version current) => Plain(latest) > Plain(current);

    static Version Plain(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Release notes as plain lines for the "what's new" message: "### 새 기능" headings become "[새 기능]" with a blank
    /// line before each group after the first, "- " or "* " bullets become "• ", bold marks and other blank lines go.</summary>
    public static string PlainNotes(string markdown)
    {
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("**", "").Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length == 0) continue;
            if (l.StartsWith('#'))
            {
                if (lines.Count > 0) lines.Add("");
                lines.Add($"[{l.TrimStart('#').Trim()}]");
            }
            else lines.Add(l.StartsWith("- ") || l.StartsWith("* ") ? "• " + l[2..] : l);
        }
        var text = string.Join("\n", lines);
        return text.Length > MaxNotes ? text[..MaxNotes] + "…" : text;   // a message box, and kept in settings.json until shown
    }

    public const int MaxNotes = 1500;

    /// <summary>The zip's SHA-256 against the first word of the .sha256 file ("hex  name", either case).</summary>
    public static bool HashMatches(Stream zip, string shaText)
    {
        var expected = shaText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is not { Length: 64 }) return false;
        return string.Equals(Convert.ToHexString(SHA256.HashData(zip)), expected, StringComparison.OrdinalIgnoreCase);
    }
}
