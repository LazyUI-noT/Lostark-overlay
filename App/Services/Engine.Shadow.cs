using System.Diagnostics;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>The 그림자 레이드 gimmick (세르카, 벨가르딘; ShadowHp): while its fill bar replaces the boss bar, the boss HP is the last
/// bar read less the gimmick's number, fed to the meter as an HP read (the meter, records and the kill check go on as if the
/// bar showed) and shown inside the gimmick's fill bar. Read in those gates only, with the boss HP (2/s: the meter's step, or the probe;
/// <see cref="ShadowEvery"/> while the gimmick is on).</summary>
public sealed partial class Engine
{
    (long Current, long Max, int? Count)? _lastBar;
    ShadowHp? _shadow;
    ShadowHp? _shadowBefore;   // the count when the number went unread: the read after the gap is checked against it, not taken as a new start
    TimeSpan _shadowAt;
    PixelRect? _shadowBar, _shadowDealt;   // the gimmick's fill bar and the damage label's spot, placed when the number is read (the fit may be gone by the publish)
    static readonly TimeSpan ShadowHold = TimeSpan.FromSeconds(3);   // the number unread this long: the gimmick is over
    static readonly TimeSpan ShadowEvery = TimeSpan.FromMilliseconds(200);

    /// <summary>How often the boss HP is read: <paramref name="usual"/>, faster during the gimmick so its number follows the damage.</summary>
    TimeSpan BossReadEvery(TimeSpan usual) => _shadow is null ? usual : ShadowEvery;

    /// <summary>The gimmick's number area when this step reads the boss HP (the meter's, or the probe with the meter off) in a 그림자 gate with its switch on (the gate by the
    /// last bar's max HP, else the one recognised).</summary>
    PixelRect? ShadowRect(Config config, PixelRect? meterHp)
    {
        if (meterHp is null || ShadowGate(config) is null) return null;
        return Geometry.ShadowNumber(_vp, _fit!) is var r && r.Inside(_vp) ? r : null;
    }

    /// <summary>The 그림자 gate whose number is read now (ShadowRead.Gate: its 기믹 체력 switch hides the display only, user 2026-11-09).</summary>
    RaidGate? ShadowGate(Config config)
    {
        var gates = config.RaidGates ?? [];
        var gate = RaidData.ByHp(gates, _lastBar?.Max)?.Gate ?? RaidData.Find(gates, _raidRec.Gate);
        return ShadowRead.Gate(config.Raids, gate, gates, _meterOn, config.HomeworkRead);
    }

    /// <summary>A bar read keeps its HP and "X n" for the gimmick; with none, the gimmick's number gives the HP. Returns the HP
    /// to step the meter with (dead: 0).</summary>
    (long Current, long Max)? ShadowStep((int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea) bar, Image? number, TimeSpan t)
    {
        if (bar.Hp is var (current, max, _))
        {
            int? count = bar.Count ?? (_lastBar is { Max: var m, Count: var c } && m == max ? c : null);   // "X n" unread this time
            (_lastBar, _shadow, _shadowBefore) = ((current, max, count), null, null);
            return null;
        }
        if (number is null) return null;
        var was = _shadow;
        long? read = BossDigits.ReadNumber(number, Geometry.Scale(_vp, _fit!));
        _shadow = _shadow is null ? ShadowHp.Resume(_shadowBefore, _lastBar, read) : ShadowHp.Step(_shadow, _lastBar, read);
        if (read is not null) (_shadowAt, _shadowBar, _shadowDealt) = (t, Geometry.ShadowBar(_vp, _fit!), Geometry.ShadowDealt(_vp, _fit!));
        else if (_shadow is not null && t - _shadowAt > ShadowHold) (_shadow, _shadowBefore) = (null, _shadow);   // the same number for a while is still it
        if (_shadow is not { } s) return null;
        if (was is null) Trace.WriteLine($"[shadow] gimmick{(_shadowBefore is null ? "" : " again")}: {s.Start:N0} at its start, {s.Dealt:N0} dealt");
        if (s.Dead && was?.Dead != true) Trace.WriteLine($"[shadow] the gimmick took the boss's last HP ({s.Dealt:N0} ≥ {s.Start:N0})");
        return (s.Dead ? 0 : s.Remaining, s.Max);
    }

    /// <summary>A new gate or leaving: the last bar is another boss's.</summary>
    void ShadowReset() => (_lastBar, _shadow, _shadowBefore) = (null, null, null);
}
