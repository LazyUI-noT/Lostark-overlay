using System.Diagnostics;
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
    TimeSpan _keptPublished = TimeSpan.MinValue;

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

    /// <summary>Raid reads that run only in a dungeon are on: the boss name, 줄 수, 암수. They need the probes when nothing
    /// else tells where the player is (the overlay's meter off).</summary>
    bool RaidReadsOn(Config config)
    {
        var plan = config.Raids is { } s ? RaidReadPlan.Of(s, config.RaidGates ?? [], _raidRec.Gate) : default;
        return plan.ReadName || plan.ReadCount || config.AmsuOn;
    }

    /// <summary>At each step: on leaving a dungeon, 줄 수 and 암수 stop reading, so their state is cleared here (a read that just
    /// stops would freeze the count and the countdown on screen).</summary>
    void StepPresence(TimeSpan now)
    {
        bool inside = _presence.InDungeon(now);
        if (_wasInDungeon && !inside)
        {
            (_amsuWatch, _bar, _raid) = (new AmsuWatch(), BarCountState.None, null);
            Trace.WriteLine("[presence] left the dungeon: 줄 수 and 암수 cleared");
            Publish(now);
        }
        _wasInDungeon = inside;
    }

    /// <summary>The boss text row, when a raid read is on and a probe is due (twice a second; it is how a boss bar is seen
    /// when the overlay's meter is off).</summary>
    PixelRect? BossProbeRect(Config config, TimeSpan now) =>
        RaidReadsOn(config) && Elapsed(now, _lastBossProbe, BossProbeEvery) ? Geometry.BossTextRow(_vp, _fit!) : null;

    void ReadBossProbe(Image row, TimeSpan t)
    {
        _lastBossProbe = t;
        var bar = BossBar(row);
        Saw(meter: false, boss: bar.Hp is not null || bar.Count is not null, t);
    }

    // The boss text row's read, shared by the step's readers (they get the same captured image): the full read and the count
    // alone (the 줄 수 창's, which reads from the right and stops at the count) each at most once a step.
    Image? _barRow;
    (int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea)? _barFull, _barCount;

    (int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea) BossBar(Image row, bool hp = true)
    {
        if (!ReferenceEquals(row, _barRow)) (_barRow, _barFull, _barCount) = (row, null, null);
        double scale = Geometry.Scale(_vp, _fit!);
        return hp ? _barFull ??= BossDigits.ReadBar(row, scale) : _barCount ??= BossDigits.ReadBar(row, scale, hp: false);
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
        if (!was && _presence.InDungeon(t)) Trace.WriteLine($"[presence] in a dungeon (by {(meter ? "the meter" : "a boss bar")})");
    }
}
