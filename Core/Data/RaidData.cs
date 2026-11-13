using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>A raid gate the 레이드 table lists (Profiles/raids.json): the boss names read above its HP bar for 자동 인식, the
/// default bar counts its 줄 수 창 glows at (high to low) and the debuff window's raid for it. <paramref name="Hidden"/>: its HP shows
/// ??? (종막 2-3, EX and 하드); it follows gate <paramref name="After"/> in the same fight and its HP removed is estimated from my
/// own damage (<see cref="Domain.HiddenHp"/>).</summary>
public sealed record RaidGate(string Id, string Raid, string Gate, IReadOnlyList<string> Bosses, IReadOnlyList<int> Alerts, Raid DebuffRaid,
    IReadOnlyDictionary<string, long>? HpByDifficulty = null, string? After = null, bool Hidden = false,
    string? Tab = null, IReadOnlyList<GateShield>? ShieldList = null,
    int Players = 8, int? Lines = null)
{
    /// <summary>4-man (세르카, 구원의 종탑; user 2026-11-04): the boss HP lost is the party's damage (MeterView BuffParty).</summary>
    public bool SmallParty => Players <= 4;

    /// <summary>A 그림자 레이드: the gate where 대난투 (the gimmick without a boss bar) happens.</summary>
    public bool IsShadow => Tab == "그림자 레이드";

    /// <summary>The gate's shield gimmicks: at a bar count, a shield worth some bars (종막 2-2: at 305, +10; a share of the HP, so
    /// the same bars at every difficulty).</summary>
    public IReadOnlyList<GateShield> Shields => ShieldList ?? [];

    /// <summary>The 설정 → 레이드 tab it is listed under (카제로스 레이드, 그림자 레이드, 어비스 던전); its <see cref="Raid"/> is the
    /// group within the tab.</summary>
    public string TabName => string.IsNullOrWhiteSpace(Tab) ? "기타" : Tab;

    /// <summary>The boss's max HP per difficulty ("노말" → 1,950,577,229,952): exact, so it names the gate and difficulty.</summary>
    public IReadOnlyDictionary<string, long> Hp => HpByDifficulty ?? new Dictionary<string, long>();
}

/// <summary>A shield gimmick: at bar count <paramref name="At"/> the boss gains a shield worth <paramref name="Lines"/> bars. It shows
/// after the HP in brackets ("(+n)", BossDigits.ReadHpWithShield) and counts as HP, so breaking it is damage.</summary>
public sealed record GateShield(int At, int Lines);

/// <summary>Loads Profiles/raids.json. Data, not code, so a later release can add gates and counts without a code change.</summary>
public static class RaidData
{
    public const string FileName = "raids.json";

    /// <summary>The gates in file order; none when the file is missing or broken (the 레이드 features then list nothing).</summary>
    public static IReadOnlyList<RaidGate> Load(string profilesDir)
    {
        try
        {
            var path = Path.Combine(profilesDir, FileName);
            if (!File.Exists(path)) return [];
            var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), Json);
            return [.. (file?.Gates ?? []).Where(g => !string.IsNullOrWhiteSpace(g.Id)).Select(g => new RaidGate(
                g.Id!, g.Raid ?? "", g.Gate ?? "", g.Bosses ?? [], [.. (g.Alerts ?? []).Distinct().OrderDescending()],
                Enum.TryParse<Raid>(g.DebuffRaid, out var r) ? r : Domain.Raid.없음, g.Hp, g.After, g.Hidden, g.Tab,
                g.Shields?.Where(x => x.At > 0 && x.Lines > 0).Select(x => new GateShield(x.At, x.Lines)).ToList(), g.Players is > 0 ? g.Players.Value : 8, g.Lines is > 0 ? g.Lines : null))];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>A fingerprint of the shipped raids.json (16 hex digits of its SHA-256), "" when there is no file. A new or changed
    /// gate changes it, which lights 레이드 once (<see cref="RailNews"/>).</summary>
    public static string Fingerprint(string profilesDir)
    {
        try
        {
            var path = Path.Combine(profilesDir, FileName);
            return File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))[..16] : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"[raids] fingerprint {profilesDir}: {e.Message}");
            return "";
        }
    }

    public static RaidGate? Find(IReadOnlyList<RaidGate> gates, string? id) => gates.FirstOrDefault(g => g.Id == id);

    /// <summary>The gate and difficulty whose boss max HP is <paramref name="max"/>, or null.</summary>
    public static (RaidGate Gate, string Difficulty)? ByHp(IReadOnlyList<RaidGate> gates, long? max) =>
        max is { } m && gates.SelectMany(g => g.Hp.Where(h => h.Value == m).Select(h => (g, h.Key))).FirstOrDefault() is ({ } g, { } d) ? (g, d) : null;

    /// <summary>The HP in one line of a bar of <paramref name="max"/>: the gate by that exact HP, else by the fight's <paramref name="label"/>
    /// (a patch changes the HP, not the lines); null when the gate's lines aren't known.</summary>
    public static double? HpPerLine(IReadOnlyList<RaidGate> gates, string? label, long max) =>
        (ByHp(gates, max)?.Gate ?? gates.FirstOrDefault(g => Label(g) == label))?.Lines is { } lines ? (double)max / lines : null;

    /// <summary>"3막 · 3관문"; the raid alone while its gate has no name.</summary>
    public static string Label(RaidGate g) => string.IsNullOrWhiteSpace(g.Gate) ? g.Raid : $"{g.Raid} · {g.Gate}";

    /// <summary>A fight's name (a gate's label, or the dungeon panel's title, OCR and all) is of <paramref name="gate"/>'s raid: most of
    /// the raid name's letters are in it ("4막: 파멸의 성채" → 파멸의 성채).</summary>
    public static bool IsRaidNamed(RaidGate gate, string name) =>
        Domain.DungeonPanel.Shared(gate.Raid[(gate.Raid.IndexOf(':') + 1)..], name) >= RaidNameShare;

    const double RaidNameShare = 0.6;

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    sealed record FileDto(List<GateDto>? Gates);
    sealed record GateDto(string? Id, string? Raid, string? Gate, List<string>? Bosses, List<int>? Alerts, string? DebuffRaid, Dictionary<string, long>? Hp,
        string? After, bool Hidden, string? Tab, List<ShieldDto>? Shields, int? Players, int? Lines);
    sealed record ShieldDto(int At, int Lines);
}
