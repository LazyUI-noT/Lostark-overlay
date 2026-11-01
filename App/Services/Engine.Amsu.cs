using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>암흑수류탄 (amsu.design.md): while its switch is on and a boss's HP text reads, the boss row is searched for the bombs
/// every 0.5 s (icons first, the "N초" only under a found bomb) and their timers read every 0.25 s under 7 s. The loop is
/// <see cref="AmsuWatch"/> (Core); here only the captures, the boss's HP and the log.</summary>
public sealed partial class Engine
{
    AmsuWatch _amsuWatch = new();
    bool _amsuOn;

    AmsuState _amsu => _amsuWatch.State;

    /// <summary>What to capture for 암수 this step: the boss's HP text with each icon read, and the row strip.</summary>
    (PixelRect? Text, PixelRect? Strip) AmsuRects(Config config, bool poll, TimeSpan now)
    {
        if (_amsuOn != config.AmsuOn) (_amsuOn, _amsuWatch) = (config.AmsuOn, new AmsuWatch());
        if (!_amsuOn || !poll || _fit is null) return (null, null);
        var (icons, timer) = _amsuWatch.Due(now, Amsu.Timed(config.BossSize));
        if (!icons && !timer) return (null, null);
        return (icons ? Geometry.BossTextRow(_vp, _fit) : null, Geometry.BossRowSearch(_vp, _fit, config.BossSize));
    }

    /// <summary>Reads what <see cref="AmsuRects"/> captured and steps the countdown. True when the window changes.</summary>
    bool ReadAmsu(Config config, Image? text, Image? strip, TimeSpan t)
    {
        long? boss = text is null ? null : BossDigits.ReadBar(text, Geometry.Scale(_vp, _fit!)).Hp?.Max;
        var (edges, changed) = _amsuWatch.Read(AmsuLayout.Of(_vp, _fit!, config.BossSize), text is not null, boss, strip, t);
        foreach (var e in edges)
            System.Diagnostics.Trace.WriteLine($"[amsu] {e.Kind} {e.Change}" + (e.Change == AmsuChange.끝 ? "" : $", {e.Left:0.0} s left") + $" (line {_amsuWatch.Line})");
        return changed;
    }
}
