using System.Diagnostics;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>설정 → 레이드 (project B): the boss's name above its HP bar picks the gate (자동 인식), and the bar count ("X n") feeds
/// the 줄 수 창 and its alerts. Each is read only while a window needs it (RaidReadPlan).</summary>
public sealed partial class Engine
{
    static readonly TimeSpan NameEvery = TimeSpan.FromSeconds(1);   // a gate changes only between fights
    RaidRecognitionState _raidRec = RaidRecognitionState.None;
    BarCountState _bar = BarCountState.None;
    string? _barGate;   // the gate _bar counts for
    RaidStatus? _raid;
    TimeSpan _lastName = TimeSpan.MinValue;

    /// <summary>The gate 자동 인식 recognises changed (its id, null: none); on the engine thread.</summary>
    public event Action<string?>? RaidRecognised;

    /// <summary>What to capture for 설정 → 레이드 this step: the name strip (once a second while a window on 자동 waits for it) and
    /// the count (every poll while the 줄 수 창 shows). A 줄 수 창 no longer shown is cleared here, since nothing is read for it.</summary>
    (PixelRect? Name, PixelRect? Count, PixelRect? Hp) RaidRects(Config config, bool poll, TimeSpan now)
    {
        var plan = config.Raids is { } s ? RaidReadPlan.Of(s, config.RaidGates ?? [], _raidRec.Gate) : default;
        if (!plan.ReadCount && _raid is not null) { _raid = null; Publish(now); }
        if (!poll) return (null, null, null);
        bool name = plan.ReadName && Elapsed(now, _lastName, NameEvery);
        // The boss's max HP (raids.json "hp") recognises a gate exactly, with its difficulty: read with the name.
        return (name ? Geometry.BossName(_vp, _fit!) : null, plan.ReadCount ? Geometry.BossCount(_vp, _fit!) : null,
            name ? Geometry.BossHpText(_vp, _fit!) : null);
    }

    /// <summary>The debuff window's raid this step: the gate's 디버프 창 (RaidReadPlan.DebuffRaid).</summary>
    Raid DebuffRaid(Config config) =>
        config.Raids is { } s ? RaidReadPlan.DebuffRaid(s, config.RaidGates ?? [], _raidRec.Gate) : Raid.없음;

    /// <summary>Reads the captured name and count (either may be null) and moves recognition and the alerts. True when the
    /// 줄 수 창 changed.</summary>
    bool ReadRaid(Config config, Image? name, Image? count, Image? hp, TimeSpan t)
    {
        var s = config.Raids!;
        var gates = config.RaidGates ?? [];
        double scale = Geometry.Scale(_vp, _fit!);
        if (name is not null)
        {
            _lastName = t;
            var byHp = hp is null ? null : RaidData.ByHp(gates, BossDigits.ReadHp(hp, scale)?.Max);
            var matched = byHp?.Gate.Id ?? BossName.Match(name, scale, config.BossNames ?? []);
            var was = _raidRec.Gate;
            _raidRec = RaidRecognition.Update(_raidRec, matched, t);
            if (_raidRec.Gate != was)
            {
                Trace.WriteLine($"[raid] recognised {_raidRec.Gate ?? "none"}" + (byHp is { } h ? $" by HP ({h.Difficulty})" : ""));
                RaidRecognised?.Invoke(_raidRec.Gate);
            }
            if (config.Debug) ExtraDebug?.Invoke(new("보스 이름", name.Copy(), matched ?? "없음", _raidRec.Gate ?? "인식 안 됨"));
        }
        var plan = RaidReadPlan.Of(s, gates, _raidRec.Gate);
        RaidStatus? next = null;
        if (plan is { ReadCount: true, Gate: { } gate })
        {
            if (gate.Id != _barGate) (_barGate, _bar) = (gate.Id, BarCountState.None);   // another gate: its own count
            if (count is not null)
            {
                int? read = BossDigits.ReadCount(count, scale);
                _bar = BarAlert.Update(_bar, read, t, BarAlert.Thresholds(s.AlertsOf(gate), s.OffsetOf(gate.Id)));
                if (config.Debug) ExtraDebug?.Invoke(new("줄 수", count.Copy(), read?.ToString() ?? "?", _bar.Shown?.ToString() ?? "?"));
            }
            bool glow = s.Glow && BarAlert.Glowing(_bar, s.GlowUntil, s.GlowSeconds, s.GlowBars, t);
            // 화면 그대로 only while a count is read: no boss bar (town), no copy of whatever is there.
            var crop = s.Look != BarCountLook.화면그대로 || _bar.Shown is null ? null : count is null ? _raid?.CountCrop : Same(_raid?.CountCrop, count);
            next = new(gate, _bar.Shown, glow, crop);
        }
        bool changed = next != _raid;   // CountCrop compares by reference: Same keeps it while the pixels don't change
        _raid = next;
        return changed;
    }

    /// <summary><paramref name="prev"/> when <paramref name="img"/> (a reused capture buffer) holds the same pixels, else a copy.</summary>
    static Image Same(Image? prev, Image img) =>
        prev is not null && prev.Width == img.Width && prev.Height == img.Height && prev.Bgra.AsSpan().SequenceEqual(img.Bgra) ? prev : img.Copy();
}
