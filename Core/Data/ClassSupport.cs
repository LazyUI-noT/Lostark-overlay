using System.Text.Json.Nodes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>How a 직업각인's 아덴 게이지 is read off the screen and shown on the overlay, in a sentence each.</summary>
public sealed record GaugeSupport(string Read, string Shown);

/// <summary>One supported 직업각인 in 설정 → 캐릭터: <see cref="Detect"/>, told by its 초각스 icon; its 아덴 게이지; its 아덴 skill
/// keys. Null: not supported yet. <see cref="Cores"/>: what 코어 빌드 does for it (Profiles/cores), null when it has no effects yet.</summary>
public sealed record SupportRow(string Class, string Subclass, bool Detect, GaugeSupport? Gauge, string? Skills, string? Cores = null)
{
    public string Key => $"{Class}/{Subclass}";

    /// <summary>The 직업각인 has 코어 빌드 effects in Profiles/cores.</summary>
    public bool HasCoreData => Cores is not null;

    /// <summary>The row and each feature it has, for "seen" (<see cref="ClassSupport.New"/>).</summary>
    public IEnumerable<string> Keys()
    {
        yield return Key;
        if (Detect) yield return Key + "/감지";
        if (Gauge is not null) yield return Key + "/게이지";
        if (Skills is not null) yield return Key + "/스킬";
        if (HasCoreData) yield return Key + "/코어";   // only where there is data: rows without effects stay as seen
    }
}

/// <summary>Which 직업각인 the app supports and how, built from the shipped data only, so a class added to Profiles shows up with
/// no code change: 직업각인 감지 when every 직업각인 of the class has an icon in Profiles/subclass (and the class's 직업각인 can be
/// told apart); the 아덴 게이지 from classes.json's "adenGauge" and its Profiles/aden gauge.json; the 아덴 skills when the gauge is
/// read and the class has "adenSlots".</summary>
public static class ClassSupport
{
    /// <summary>Every 직업각인 with at least one feature, in classes.json order. <paramref name="keyName"/> names a slot by the user's
    /// key (발키리's 아덴1 is Z).</summary>
    public static IReadOnlyList<SupportRow> Rows(string profilesDir, IReadOnlyList<ClassInfo> classes, Func<SlotId, string> keyName)
    {
        var rows = new List<SupportRow>();
        foreach (var cls in classes)
        {
            var cores = CoreEffects.Load(profilesDir, cls.Name);
            bool detect = !cls.SameSkills && cls.Subclasses.Keys.All(s => File.Exists(Path.Combine(profilesDir, "subclass", cls.Name, s + ".png")));
            foreach (var (sub, rule) in cls.Subclasses)
            {
                var gauge = rule is not null && GaugeLoader.Folder(profilesDir, cls.Name, sub) is { } dir && OrderKind(dir) is { } kind ? Gauge(kind, rule, keyName) : null;
                var slots = cls.AdenSlotsOf(sub);
                string? skills = gauge is not null && slots.Count > 0 ? Skills(slots, cls.AdenDark(sub), keyName) : null;
                string? coreLine = cores.GetValueOrDefault(sub) is { Count: > 0 } fx ? CoreEffects.Describe(fx, keyName) : null;
                if (detect || gauge is not null) rows.Add(new(cls.Name, sub, detect, gauge, skills, coreLine));
            }
        }
        return rows;
    }

    /// <summary>Every key of <paramref name="rows"/>, for Settings.SeenSupport.</summary>
    public static List<string> Keys(IEnumerable<SupportRow> rows) => [.. rows.SelectMany(r => r.Keys())];

    /// <summary>The keys of <paramref name="rows"/> not in <paramref name="seen"/>; none when nothing was ever recorded (a first run,
    /// or an update from before the list), so only what arrives after it is new.</summary>
    public static IReadOnlySet<string> New(IEnumerable<SupportRow> rows, IReadOnlyCollection<string>? seen) =>
        seen is null ? new HashSet<string>() : Keys(rows).Except(seen).ToHashSet();

    /// <summary>gauge.json's "order"."kind": how the gauge fills on screen; null for a file that can't be read, which the engine
    /// treats as no gauge too (CharacterService.LoadGauge), so the row doesn't claim one.</summary>
    static string? OrderKind(string dir)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(dir, GaugeLoader.JsonFile))) is JsonObject root
                && root["order"] is JsonObject order && order["kind"] is JsonValue kind && kind.TryGetValue<string>(out var s) ? s : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.WriteLine($"[support] {dir}: {ex.Message}");
            return null;
        }
    }

    static GaugeSupport Gauge(string kind, GaugeRule rule, Func<SlotId, string> keyName)
    {
        int sections = rule.Splits.Count + 1;
        string read = kind switch
        {
            "piecewise" => $"결정 {sections}개가 하나씩 차는 것을 읽습니다.",
            "rings" => $"구슬 {sections}개가 시계 방향으로 차는 것을 읽습니다.",
            _ => "차오르는 높이를 읽습니다.",
        };
        string? glow = rule.Glow switch
        {
            { When: GlowWhen.Sections } => "다 찬 구간마다 빛남",
            { When: GlowWhen.SlotReady, Slot: { } slot, MinGauge: var min } =>
                (min >= 1 ? "다 차고 " : min > 0 ? $"{min * 100:0}% 이상이고 " : "") + $"{keyName(slot)}가 준비되면 빛남",
            _ => null,
        };
        // "2구간, … 빛남"; one section: just when it glows, else a plain bar.
        string shown = sections > 1 ? (glow is null ? $"{sections}구간" : $"{sections}구간, {glow}") : glow ?? "막대";
        return new(read, shown);
    }

    static string Skills(IReadOnlyList<SlotId> slots, DarkCue dark, Func<SlotId, string> keyName) =>
        string.Join(", ", slots.Select(keyName)) + dark switch
        {
            DarkCue.Icon => " (게이지가 모자라면 아이콘이 어두움)",
            DarkCue.Label => " (게이지가 모자라면 키 글자가 회색)",
            _ => "",
        };
}
