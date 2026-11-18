using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>낙인 (user, 2026-11-15): while its bar is on, the boss debuff row is read with 암수's (one strip, snap and walk: <see cref="AmsuWatch.Brand"/>,
/// <see cref="Engine.AmsuRects"/>); the bar is <see cref="BrandWatch"/>'s, written over the own-row reads in <see cref="_buffs"/>.
/// Only in a dungeon and at a 타겟 UI 버프 크기 the icons read at (<see cref="Amsu.Readable"/>); elsewhere it fades after the grace.</summary>
public sealed partial class Engine
{
    BrandWatch _brand = new();
    bool _brandOn;

    /// <summary>Steps the 낙인 bar to <paramref name="t"/>: what this step's pass read is in <see cref="_brand"/>, the rest is its grace
    /// and end. True when the bar changed.</summary>
    bool StepBrand(Config config, TimeSpan t)
    {
        var was = _buffs[(int)BuffKind.낙인];
        if (!_brandOn)
        {
            // Turned off while up: end it now, or the record and the meter keep 낙인 for the rest of the fight.
            if (!was.Active) return false;
            _buffs[(int)BuffKind.낙인] = BuffState.Absent(BuffKind.낙인, t);
            return true;
        }
        var state = _brand.Expire(t);
        if (state.Active != was.Active)
            System.Diagnostics.Trace.WriteLine(state.Active ? $"[brand] on ({_brand.Icon})" : "[brand] off");
        _buffs[(int)BuffKind.낙인] = state;
        return state != was;
    }
}
