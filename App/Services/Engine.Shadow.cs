using System.Diagnostics;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>The 그림자 레이드 gimmick (세르카, 벨가르딘; ShadowHp): while its fill bar replaces the boss bar, the boss HP is the last
/// bar read less the gimmick's number, fed to the meter as an HP read (the meter, records and the kill check go on as if the
/// bar showed) and shown inside the gimmick's fill bar. Read in those gates only, with the meter's boss HP (2/s).</summary>
public sealed partial class Engine
{
    (long Current, long Max, int? Count)? _lastBar;
    ShadowHp? _shadow;
    TimeSpan _shadowAt;
    PixelRect? _shadowBar, _shadowDealt;   // the gimmick's fill bar and the damage label's spot, placed when the number is read (the fit may be gone by the publish)
    static readonly TimeSpan ShadowHold = TimeSpan.FromSeconds(3);   // the number unread this long: the gimmick is over

    /// <summary>The gimmick's number area when this step reads the boss HP in a 그림자 gate with its switch on (the gate by the
    /// last bar's max HP, else the one recognised).</summary>
    PixelRect? ShadowRect(Config config, PixelRect? meterHp)
    {
        if (meterHp is null || config.Raids is not { } s) return null;
        var gates = config.RaidGates ?? [];
        var gate = RaidData.ByHp(gates, _lastBar?.Max)?.Gate ?? RaidData.Find(gates, _raidRec.Gate);
        return gate is { IsShadow: true } g && s.ShadowHpOn(g.Raid) && Geometry.ShadowNumber(_vp, _fit!) is var r && r.Inside(_vp) ? r : null;
    }

    /// <summary>A bar read keeps its HP and "X n" for the gimmick; with none, the gimmick's number gives the HP. Returns the HP
    /// to step the meter with (dead: 0).</summary>
    (long Current, long Max)? ShadowStep((int? Count, (long Current, long Max, long Shield)? Hp, PixelRect? CountArea) bar, Image? number, TimeSpan t)
    {
        if (bar.Hp is var (current, max, _))
        {
            int? count = bar.Count ?? (_lastBar is { Max: var m, Count: var c } && m == max ? c : null);   // "X n" unread this time
            (_lastBar, _shadow) = ((current, max, count), null);
            return null;
        }
        if (number is null) return null;
        var was = _shadow;
        long? read = BossDigits.ReadNumber(number, Geometry.Scale(_vp, _fit!));
        _shadow = ShadowHp.Step(_shadow, _lastBar, read);
        if (read is not null) (_shadowAt, _shadowBar, _shadowDealt) = (t, Geometry.ShadowBar(_vp, _fit!), Geometry.ShadowDealt(_vp, _fit!));
        else if (_shadow is not null && t - _shadowAt > ShadowHold) _shadow = null;   // the same number for a while is still it
        if (_shadow is not { } s) return null;
        if (was is null) Trace.WriteLine($"[shadow] gimmick: {s.Start:N0} at its start, {s.Dealt:N0} dealt");
        if (s.Dead && was?.Dead != true) Trace.WriteLine($"[shadow] the gimmick took the boss's last HP ({s.Dealt:N0} ≥ {s.Start:N0})");
        return (s.Dead ? 0 : s.Remaining, s.Max);
    }

    /// <summary>A new gate or leaving: the last bar is another boss's.</summary>
    void ShadowReset() => (_lastBar, _shadow) = (null, null);
}
