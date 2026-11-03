using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>How the 줄 수 창 shows the count: a copy of the game's pixels, or the number read, drawn as text.</summary>
public enum BarCountLook { 화면그대로, 숫자 }

/// <summary>How long an alert glows: <see cref="RaidSettings.GlowSeconds"/> seconds, or until the count is
/// <see cref="RaidSettings.GlowBars"/> bars past it.</summary>
public enum GlowUntil { Seconds, Bars }

/// <summary>One gate's row in the 레이드 table.</summary>
public sealed class GateSettings
{
    /// <summary>줄 수 창 on for this gate.</summary>
    public bool CountWindow { get; set; }
    /// <summary>자동: the 줄 수 창 shows only while this gate's boss is recognised (or picked by hand).</summary>
    public bool CountAuto { get; set; } = true;
    /// <summary>The counts the window glows at; null = the gate's defaults (Profiles/raids.json). An emptied list is kept empty.</summary>
    public List<int>? Alerts { get; set; }
    /// <summary>Old (0.13–0.16.0): this gate's 디버프 창. Read once by <see cref="RaidSettings.Normalize"/> into
    /// <see cref="RaidSettings.DebuffsAuto"/> / <see cref="DebuffSettings.Raid"/>, then left off.</summary>
    public bool DebuffWindow { get; set; }
    /// <summary>Old (0.13–0.16.0): with <see cref="DebuffWindow"/>, 자동 (true) or always (false).</summary>
    public bool DebuffAuto { get; set; } = true;
    /// <summary>This gate's ± bars, used when <see cref="RaidSettings.OffsetPerGate"/> is on.</summary>
    public int Offset { get; set; }
    /// <summary>The 에스더 plan for this gate (종막 EX): each use's damage (억) and when it goes in (bar count; HP % on a ??? gate).
    /// 2-1's total sets the 조력 비율 (HiddenHp.CalibratedShare); 2-3's uses come off the estimate as they are reached.</summary>
    public List<EstherUse>? Esthers { get; set; }
    /// <summary>A ??? gate: each party's support 조력 피해량 at the end of its calibration gate (2-1; 억, 0 = unknown). Filled when
    /// 2-1 is cleared for the parties whose support sends; the user types the rest.</summary>
    public double Assist1 { get; set; }
    public double Assist2 { get; set; }
    /// <summary>HP 변경 감지: max HP learned per difficulty (<see cref="HpChange"/>), used instead of raids.json's.</summary>
    public Dictionary<string, LearnedHp>? LearnedHp { get; set; }
    /// <summary>HP 변경 감지: first values seen in one raid, waiting for a later raid to read them again (<see cref="HpChange.Keep"/>).</summary>
    public Dictionary<string, PendingHp>? PendingHp { get; set; }
}

/// <summary>설정 → 레이드 (spec § B): boss recognition (자동, or a gate picked by hand), the gates' rows, the alert offset and
/// glow, and the 줄 수 창's look and place (NaN = next to the overlay until first placed).</summary>
public sealed class RaidSettings : IWindowPlace, System.Text.Json.Serialization.IJsonOnDeserializing
{
    /// <summary>A file saved before 0.16.1 has no "DebuffsMigrated": its per-gate 디버프 창 is carried over once (Normalize).</summary>
    void System.Text.Json.Serialization.IJsonOnDeserializing.OnDeserializing() => DebuffsMigrated = false;

    /// <summary>The gate 모르둠's old debuff switch (<see cref="DebuffSettings.Raid"/>) carries over to.</summary>
    public const string MordumGate = "act3-g3";
    public const int MaxGlowSeconds = 600, MaxGlowBars = 200, MaxOffset = 999;

    /// <summary>자동 인식: the boss name above the HP bar picks the gate; off = <see cref="ManualGate"/>.</summary>
    public bool Auto { get; set; } = true;
    public string? ManualGate { get; set; }
    /// <summary>줄 수 읽기 for every raid: off stops the bar count (X 494) and hides the 줄 수 창, each gate's own
    /// <see cref="GateSettings.CountWindow"/> kept as it is. The 디버프 창 doesn't follow it.</summary>
    public bool CountEnabled { get; set; } = true;
    public Dictionary<string, GateSettings> Gates { get; set; } = [];
    /// <summary>디버프 창 자동, for every raid (user, 2026-11-04: no per-gate switch): a raid's debuff window shows while its gate
    /// is recognised (or picked by hand). A raid turned on in the tray (<see cref="DebuffSettings.Raid"/>) shows in any dungeon
    /// and wins over this.</summary>
    public bool DebuffsAuto { get; set; } = true;
    /// <summary>The per-gate 디버프 창 (0.13–0.16.0) has been carried over to <see cref="DebuffsAuto"/> and the tray's pick (false
    /// only while loading a file saved without it).</summary>
    public bool DebuffsMigrated { get; set; } = true;
    /// <summary>± bars added to every alert ("전 관문 공통"); per gate when <see cref="OffsetPerGate"/>. Target 750, +5 → 755.</summary>
    public int Offset { get; set; }
    public bool OffsetPerGate { get; set; }
    public bool Glow { get; set; } = true;
    public GlowUntil GlowUntil { get; set; } = GlowUntil.Seconds;
    public int GlowSeconds { get; set; } = 20;
    public int GlowBars { get; set; } = 5;
    public BarCountLook Look { get; set; } = BarCountLook.숫자;
    /// <summary>A ??? gate's estimate: a support's 조력 피해량 as a share of its party's damage (%), and the other party's damage as a
    /// multiple of the known one when only one party's figures arrive.</summary>
    public double AssistShare { get; set; } = 43.5;
    public double OtherParty { get; set; } = 1.0;
    public const double MinAssistShare = 20, MaxAssistShare = 80, MaxOtherParty = 3, MaxEsther = 1_000_000;
    public string Background { get; set; } = "#CC101418";
    public double Opacity { get; set; } = 1.0;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    /// <summary>The old 모르둠 debuff switch has been carried over (false only while loading a file saved before this table).</summary>
    public bool Migrated { get; set; } = true;

    /// <summary>Gate <paramref name="id"/>'s row, added with everything off the first time.</summary>
    public GateSettings Gate(string id)
    {
        if (!Gates.TryGetValue(id, out var g)) Gates[id] = g = new();
        return g;
    }

    /// <summary>A deep copy, for the engine's thread (설정 edits this one on the UI thread).</summary>
    public RaidSettings Clone() =>
        System.Text.Json.JsonSerializer.Deserialize<RaidSettings>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(this, SettingsStore.Json), SettingsStore.Json)!;

    /// <summary>A gate's 줄 수 창 is on (the window exists; what it shows waits for recognition).</summary>
    public bool CountShown() => CountEnabled && Gates.Values.Any(g => g.CountWindow);
    /// <summary>Gate <paramref name="id"/>'s 줄 수 창 is on, with the main switch.</summary>
    public bool CountOn(string id) => CountEnabled && Gates.TryGetValue(id, out var g) && g.CountWindow;
    /// <summary>The 디버프 창 may show: 자동 or a raid picked in the tray (what it shows waits for a dungeon).</summary>
    public bool DebuffShown(DebuffSettings debuffs) => DebuffsAuto || debuffs.Raid != Raid.없음;

    public int OffsetOf(string id) => OffsetPerGate ? (Gates.TryGetValue(id, out var g) ? g.Offset : 0) : Offset;

    /// <summary>The counts <paramref name="gate"/>'s window glows at: the user's list once edited, else the shipped defaults.</summary>
    public IReadOnlyList<int> AlertsOf(RaidGate gate) =>
        Gates.TryGetValue(gate.Id, out var g) && g.Alerts is { } own ? own : gate.Alerts;

    /// <summary>Load-time repairs (SettingsStore.Normalize): hand-edited numbers clamped, and the old 모르둠 switch carried over once.</summary>
    internal void Normalize(DebuffSettings debuffs)
    {
        Gates ??= [];
        foreach (var (key, g) in Gates.ToList())
            if (g is null) Gates.Remove(key);
            else
            {
                g.Alerts = g.Alerts is null ? null : [.. g.Alerts.Where(a => a is > 0 and < 100_000).Distinct().OrderDescending()];
                g.Esthers = g.Esthers?.Where(u => u is not null && !string.IsNullOrWhiteSpace(u.Name))   // hand edits
                    .Select(u => u with { Damage = double.IsFinite(u.Damage) ? Math.Clamp(u.Damage, 0, MaxEsther) : 0, At = Math.Clamp(u.At, 0, 99_999) }).ToList();
                g.Assist1 = double.IsFinite(g.Assist1) ? Math.Clamp(g.Assist1, 0, MaxEsther) : 0;
                g.Assist2 = double.IsFinite(g.Assist2) ? Math.Clamp(g.Assist2, 0, MaxEsther) : 0;
            }
        AssistShare = double.IsFinite(AssistShare) ? Math.Clamp(AssistShare, MinAssistShare, MaxAssistShare) : 43.5;
        OtherParty = double.IsFinite(OtherParty) ? Math.Clamp(OtherParty, 0, MaxOtherParty) : 1.0;
        (GlowSeconds, GlowBars) = (Math.Clamp(GlowSeconds, 1, MaxGlowSeconds), Math.Clamp(GlowBars, 1, MaxGlowBars));
        Offset = Math.Clamp(Offset, -MaxOffset, MaxOffset);
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.1, 1) : 1;
        if (DebuffsMigrated) return;
        // 0.13–0.16.0 kept the 디버프 창 per gate (and mirrored it into debuffs.Raid, 자동 or not): a gate on "always" becomes
        // the tray's pick, one on 자동 keeps 디버프 창 자동 on, one off turns it off (user, 2026-11-04: off stays off). Before the
        // table (Migrated false) debuffs.Raid was the switch, and 자동 keeps its default.
        if (Migrated && Gates.GetValueOrDefault(MordumGate) is var mordum)
            (debuffs.Raid, DebuffsAuto) = (mordum is { DebuffWindow: true, DebuffAuto: false } ? Raid.모르둠 : Raid.없음, mordum is { DebuffWindow: true, DebuffAuto: true });
        foreach (var g in Gates.Values) (g.DebuffWindow, g.DebuffAuto) = (false, true);
        (Migrated, DebuffsMigrated) = (true, true);
    }
}
