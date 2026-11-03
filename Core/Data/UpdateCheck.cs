using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>A release that can be installed: its version, tag, the zip and .sha256 download links, and its notes (markdown,
/// shown once after the update: <see cref="UpdateCheck.PlainNotes"/>).</summary>
public sealed record Release(Version Version, string Tag, string ZipUrl, string ShaUrl, string Notes = "")
{
    /// <summary>A "vX.Y.Z-beta.N" pre-release, offered only on the 베타 channel.</summary>
    public bool IsBeta => UpdateCheck.ParseTag(Tag)?.Beta > 0;
}

/// <summary>업데이트 확인: the latest release of Lostark-overlay (GitHub's public API, no login), whether it's newer than the
/// running version, and whether a downloaded zip matches its .sha256.</summary>
public static class UpdateCheck
{
    /// <summary>The newest releases, betas included (pre-releases aren't "latest").</summary>
    public const string ListUrl = "https://api.github.com/repos/LazyUI-noT/Lostark-overlay/releases?per_page=30";
    /// <summary>Downloads are taken only from this repo's releases, whatever the answer says.</summary>
    public const string DownloadPrefix = "https://github.com/LazyUI-noT/Lostark-overlay/releases/download/";

    /// <summary>One release answer, or null when it's no installable release: a draft, a tag that isn't "vX.Y.Z" or
    /// "vX.Y.Z-beta.N", a pre-release without a beta tag (the retired alpha line), no GaugeOverlay-*.zip with its .sha256,
    /// a link outside <paramref name="prefix"/> (the repo's releases), or not a release at all.</summary>
    public static Release? Parse(string json, string prefix = DownloadPrefix)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement, prefix);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The releases list (<see cref="ListUrl"/>): the installable ones, as <see cref="Parse(string, string)"/> takes
    /// them; empty for a broken or error answer.</summary>
    public static IReadOnlyList<Release> ParseList(string json, string prefix = DownloadPrefix)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            return [.. doc.RootElement.EnumerateArray().Select(e => Parse(e, prefix)).OfType<Release>()];
        }
        catch (JsonException) { return []; }
    }

    static Release? Parse(JsonElement root, string prefix)
    {
        try
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tagEl)
                || Flag(root, "draft") || !root.TryGetProperty("assets", out var assets)) return null;
            string tag = tagEl.GetString() ?? "";
            if (ParseTag(tag) is not var (version, beta)) return null;
            if (Flag(root, "prerelease") && beta == 0) return null;

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

    static readonly Regex TagPattern = new(@"^v(\d+)\.(\d+)\.(\d+)(?:-beta\.([1-9]\d*))?$", RegexOptions.CultureInvariant);

    /// <summary>"vX.Y.Z" (Beta 0) or "vX.Y.Z-beta.N" (Beta N ≥ 1); null for any other tag.</summary>
    public static (Version Version, int Beta)? ParseTag(string tag)
    {
        var m = TagPattern.Match(tag);
        if (!m.Success) return null;
        try
        {
            var version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            return (version, m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0);
        }
        catch (OverflowException) { return null; }
    }

    /// <summary>Release order: by version, and a version's betas before its stable release. Unknown tags come first.</summary>
    public static int CompareTags(string a, string b)
    {
        static (Version, int) Key(string t) => ParseTag(t) is var (v, beta) ? (v, beta == 0 ? int.MaxValue : beta) : (new Version(0, 0, 0), -1);
        var (va, ba) = Key(a);
        var (vb, bb) = Key(b);
        int c = va.CompareTo(vb);
        return c != 0 ? c : ba.CompareTo(bb);
    }

    /// <summary>What to offer on <paramref name="channel"/> to the running <paramref name="currentTag"/>: 베타 → the newest
    /// release of all, when after this one; 안정 → the newest stable release, when after this one, or, on a beta, back to
    /// it even when it is older (the way out of a broken beta). Null for nothing.</summary>
    public static Release? Offer(UpdateChannel channel, string currentTag, IEnumerable<Release> releases)
    {
        var pick = releases.Where(r => channel == UpdateChannel.Beta || !r.IsBeta)
            .OrderByDescending(r => r.Tag, Comparer<string>.Create(CompareTags)).FirstOrDefault();
        if (pick is null) return null;
        bool onBeta = ParseTag(currentTag)?.Beta > 0;
        return CompareTags(pick.Tag, currentTag) > 0 || channel == UpdateChannel.Stable && onBeta ? pick : null;
    }

    /// <summary>Installing <paramref name="release"/> goes back from <paramref name="currentTag"/> (안정 after a beta).</summary>
    public static bool IsDowngrade(Release release, string currentTag) => CompareTags(release.Tag, currentTag) < 0;

    /// <summary>The running tag from the assembly's informational version ("0.16.0-beta.1+commit" → "v0.16.0-beta.1").</summary>
    public static string TagOf(string informationalVersion) => "v" + informationalVersion.Split('+')[0];

    static bool Flag(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>True when <paramref name="latest"/> changes A or B of A.B.x: parties need the same A.B, so after it this version
    /// can't party with those who don't update.</summary>
    public static bool BreaksParty(Version latest, Version current) => latest.Major != current.Major || latest.Minor != current.Minor;

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
