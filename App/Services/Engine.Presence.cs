using System.Diagnostics;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>Where the player is (<see cref="RaidPresence"/>, meter-gating.plan.md): the in-game DPS meter and the boss HP bar,
/// read cheaply, gate the raid reads. Evidence comes from the reads that run anyway (the meter's panel and HP text) and two
/// probes: the boss text row twice a second while a boss read waits for it, and, with the overlay's meter off, the meter's
/// green cell at its saved spot once a second while 자동 인식 waits for the dungeon.</summary>
public sealed partial class Engine
{
    static readonly TimeSpan BossProbeEvery = TimeSpan.FromMilliseconds(500), MeterProbeEvery = TimeSpan.FromSeconds(1);
    RaidPresence _presence = RaidPresence.None;
    TimeSpan _lastBossProbe = TimeSpan.MinValue, _lastMeterProbe = TimeSpan.MinValue;

    static readonly TimeSpan KeptPublishEvery = TimeSpan.FromSeconds(10);
    TimeSpan _keptPublished = TimeSpan.MinValue, _seenPublished = TimeSpan.MinValue;

    /// <summary>After the HUD vote: a hidden HUD keeps a dungeon (a cutscene hides the HUD and the meter alike). True when the
    /// app should hear of it: nothing else publishes on a still cutscene, and the app's windows read the published presence.</summary>
    bool KeepWhileHidden(TimeSpan t)
    {
        if (_hud.Visible != false) return false;
        var was = _presence.Kept;
        _presence = _presence.Hidden(t);
        if (_presence.Kept == was || !Elapsed(t, _keptPublished, KeptPublishEvery)) return false;
        _keptPublished = t;
        return true;
    }

    bool _wasInDungeon;

    /// <summary>In a dungeon or not (<see cref="RaidPresence.InDungeon"/>), on each change: 오버레이 표시's 던전 안에서.</summary>
    public event Action<bool>? InDungeonChanged;

    // The loading screen (meter-gating.plan.md phase 2): its bar read 4/s while the HUD is hidden or not yet known (the app
    // started on a loading screen) and presence matters.
    static readonly TimeSpan LoadingEvery = TimeSpan.FromMilliseconds(250);
    LoadingWatch _loading = LoadingWatch.None;
    TimeSpan _lastLoading = TimeSpan.MinValue;

    PixelRect? LoadingRect(Config config, TimeSpan now) =>
        _hud.Visible != true && (_meterOn || RaidReadsOn(config)) && Elapsed(now, _lastLoading, LoadingEvery)
        && Geometry.LoadingBar(_vp) is var r && r.Inside(_vp) ? r : null;

    void ReadLoading(Image strip, TimeSpan t)
    {
        _lastLoading = t;
        bool was = _loading.Loading;
        _loading = _loading.Read(LoadingScreen.Is(strip, _vp.Height / Geometry.ReferenceHeight), _presence, t);
        if (was || !_loading.Loading) return;
        Interlocked.Increment(ref _dungeonGen);   // maybe another gate: its name gets fresh votes
        Trace.WriteLine($"[presence] loading screen{(_presence.InDungeon(t) ? " (in a dungeon)" : "")}");
    }

    /// <summary>A new 전분 record after another one: between gates there is no loading screen, but the record starts at 00:00
    /// (user, 2026-11-03). As after a loading screen, the dungeon panel gets fresh votes, read fast.</summary>
    void NewGate()
    {
        Interlocked.Increment(ref _dungeonGen);
        WakeDungeon();
        ShadowReset();   // the next gate's boss
        ReinforcedReset();
        Trace.WriteLine("[presence] new 전분 record: the dungeon panel read afresh");
    }

    /// <summary>The HUD is back after a loading screen (로딩 뒤 직업각인 자동 감지).</summary>
    public event Action? LoadingEnded;

    /// <summary>The HUD is back: after a loading screen, look for the meter at once and start the wait for it.</summary>
    void LoadingOver(TimeSpan t)
    {
        if (_loading.Loading) { (_lastMeterProbe, _lastMeter) = (TimeSpan.MinValue, TimeSpan.MinValue); LoadingEnded?.Invoke(); }   // look for the meter at once
        _loading = _loading.HudBack(t);
    }

    /// <summary>Raid reads that run only in a dungeon are on: the boss name, 줄 수, 암수, the raid's 디버프 창, and a buff bar on 자동. They need
    /// the probes when nothing else tells where the player is (the overlay's meter off).</summary>
    bool RaidReadsOn(Config config)
    {
        var plan = config.Raids is { } s ? RaidReadPlan.Of(s, config.RaidGates ?? [], _raidRec.Gate) : default;
        return plan.ReadName || plan.ReadCount || config.AmsuOn || config.DebuffPick != Raid.없음 || config.Raids?.DebuffsAuto == true   // the 디버프 창 waits for a dungeon too
            || config.BuffsInRaidOnly;
    }

    /// <summary>At each step: on leaving a dungeon, 줄 수 and 암수 stop reading, so their state is cleared here (a read that just
    /// stops would freeze the count and the countdown on screen).</summary>
    void StepPresence(TimeSpan now)
    {
        _loading = _loading.Tick(_hud.Visible == true && Seen, now);
        if (_loading.Left)
        {
            (_presence, _loading) = (RaidPresence.None, LoadingWatch.None);
            Trace.WriteLine($"[presence] no meter {LoadingWatch.MeterWait.TotalSeconds:0} s after a loading screen: left");
        }
        bool inside = _presence.InDungeon(now);
        if (_wasInDungeon && !inside)
        {
            (_amsuWatch, _bar, _raid, _bossHp) = (new AmsuWatch(), BarCountState.None, null, BossHpLabel.None);
            ShadowReset();
            Ocr = NewOcr();   // the OCR engine is let go until the next dungeon, which starts with a fresh one (user, 2026-11-03)
            Trace.WriteLine("[presence] left the dungeon: 줄 수 and 암수 cleared, OCR closed");
            Publish(now);
        }
        if (inside != _wasInDungeon) InDungeonChanged?.Invoke(inside);
        _wasInDungeon = inside;
    }

    /// <summary>The boss text row, when a raid read is on and a probe is due (twice a second; it is how a boss bar is seen
    /// when the overlay's meter is off; faster during 대난투).</summary>
    PixelRect? BossProbeRect(Config config, TimeSpan now) =>
        (RaidReadsOn(config) || !_meterOn && BossHpPlan.Wanted(config.Raids, config.RaidGates ?? [], config.HomeworkRead)) && Elapsed(now, _lastBossProbe, BossReadEvery(BossProbeEvery))
            ? Geometry.BossTextRow(_vp, _fit!) : null;

    /// <summary>With the meter off this is the boss HP's read, the 그림자 number's too; with it on, the meter's step does both.</summary>
    /// <returns>Publish: the gimmick changed, which nothing else would tell with the meter off.</returns>
    bool ReadBossProbe(Image row, Image? shadow, TimeSpan t)
    {
        _lastBossProbe = t;
        var was = _shadow;
        var bar = BossBar(row);
        var gimmick = _meterOn ? null : ShadowStep(bar, shadow, t);
        Saw(meter: false, boss: bar.Hp is not null || bar.Count is not null || gimmick is not null, t);
        return !Equals(was, _shadow);
    }

    // The boss text row's read, shared by the step's readers (they get the same captured image): the full read and the count
    // alone (the 줄 수 창's, which reads from the right and stops at the count) each at most once a step.
    Image? _barRow;
    TimeSpan _barAt;   // the step's time, for the 강화 HP reader
    readonly BossHpSoftReader _bossSoft = new();   // 테스트 모드 강화 읽기: the HP checked against the fight (max holds, HP falls)
    (TimeSpan At, (long Current, long Max, long Shield)? Hp)? _softHp;   // its read this step (another row capture reads afresh)
    bool _reinforcedWas;

    /// <summary>The 강화 readers start over: a new gate, or 강화 읽기 turned on or off.</summary>
    void ReinforcedReset()
    {
        _bossSoft.Reset();
        _jeonbunSoft.Reset();
        _softHp = null;
        ResetIconGates();
    }

    /// <summary>The icon gates identify every box again: sharpening or another source can keep a thumbnail within the
    /// threshold while the answer would differ (icon-gates step 2 review).</summary>
    void ResetIconGates()
    {
        _buffIdentify.Reset();
        _debuffIdentify.Reset();
        _judgmentIcon.Reset();
        _amsuWatch.ResetGate();
    }

    /// <summary>At each step: 강화 읽기 switched since the last one starts its readers over.</summary>
    void StepReinforced()
    {
        if (GameWindow.ReadsReinforced == _reinforcedWas) return;
        _reinforcedWas = !_reinforcedWas;
        ReinforcedReset();
    }

    /// <summary>The 강화 HP read, once a step (the readers capture the row more than once a step).</summary>
    (long Current, long Max, long Shield)? SoftHp(Image row, double scale)
    {
        if (_softHp is { } done && done.At == _barAt) return done.Hp;
        var hp = _bossSoft.Read(row, scale, _barAt);
        _softHp = (_barAt, hp);
        return hp;
    }
    (int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea)? _barFull, _barCount;

    (int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea) BossBar(Image row, bool hp = true)
    {
        if (!ReferenceEquals(row, _barRow)) (_barRow, _barFull, _barCount) = (row, null, null);
        double scale = Geometry.Scale(_vp, _fit!);
        if (!hp) return _barCount ??= BossDigits.ReadBar(row, scale, hp: false);
        if (_barFull is { } read) return read;
        var bar = BossDigits.ReadBar(row, scale, hp: !GameWindow.ReadsReinforced);   // 강화: the count as captured, the HP below
        var full = (_barFull = GameWindow.ReadsReinforced ? bar with { Hp = SoftHp(row, scale) } : bar).Value;
        StepBossHp(full.Hp);
        if (full.Hp is var (current, max, _)) BossRead?.Invoke(current, max);
        return full;
    }

    /// <summary>The boss bar's HP and max HP, once per captured row (engine thread; 숙제's fallback, HomeworkFallback).</summary>
    public event Action<long, long>? BossRead;

    BossHpLabel _bossHp = BossHpLabel.None;
    bool _bossHpChanged;

    /// <summary>The boss HP label (보스 체력 쉽게 보기) after a full read of the bar's text, once per captured row.</summary>
    void StepBossHp((long Current, long Max, long Shield)? hp)
    {
        var next = _bossHp.Step(hp is var (current, _, shield) ? new BossHpView(current, shield, Geometry.BossHpLabel(_vp, _fit!)) : null, _barAt);
        _bossHpChanged |= next != _bossHp;
        _bossHp = next;
    }

    /// <summary>The meter's saved spot, with the overlay's meter off while a raid read is on and a probe is due.</summary>
    PixelRect? MeterProbeRect(Config config, TimeSpan now) =>
        !_meterOn && RaidReadsOn(config) && Elapsed(now, _lastMeterProbe, MeterProbeEvery) ? PanelSpot(config) : null;

    void ReadMeterProbe(Image spot, TimeSpan t)
    {
        _lastMeterProbe = t;
        Saw(meter: MeterPresent(spot), boss: false, t);
    }

    /// <summary>The in-game meter in a capture of its spot (its scale follows the spot's width: the screen's height, not the HUD).</summary>
    static bool MeterPresent(Image spot) => JeonbunFinder.Present(spot, spot.Width / JeonbunReader.RefW);

    void Saw(bool meter, bool boss, TimeSpan t)
    {
        bool was = _presence.InDungeon(t);
        _presence = _presence.Saw(meter, boss, t);
        if (meter || boss) _loading = _loading.Saw();
        if (!meter && !boss || was && !Elapsed(t, _seenPublished, KeptPublishEvery)) return;
        if (!was) Trace.WriteLine($"[presence] in a dungeon (by {(meter ? "the meter" : "a boss bar")})");
        // A bar on 자동 reads the published presence: on entering, and well inside its hold while nothing else publishes (standing idle in a raid).
        _seenPublished = t;
        Publish(t);
    }
}
