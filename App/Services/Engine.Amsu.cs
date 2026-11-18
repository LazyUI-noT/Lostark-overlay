using System.IO;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>암흑수류탄 (amsu.design.md): while its switch is on, the boss row is read on the debuff rows' cadence (RowCadence: a
/// frame pass 5/s, the bombs identified 2/s with the boss's HP text or at once on a shift, timers 10/s under 2 s). The loop is
/// <see cref="AmsuWatch"/> (Core); here only the captures, the boss's HP and the log.</summary>
public sealed partial class Engine
{
    AmsuWatch _amsuWatch = new();
    bool _amsuOn;
    RowPass _amsuPass;
    bool _amsuDaenantu;       // this step's strip is 대난투's (AmsuRects): the read uses the layout it was captured for
    bool _amsuTimed = true;   // the "N초" read at this game area and 타겟 UI 버프 크기 (Amsu.Timed): the 암수 창 counts down

    AmsuState _amsu => _amsuWatch.State;

    /// <summary>What to capture for 암수 this step: the boss's HP text with each icon read, and the row strip.</summary>
    (PixelRect? Text, PixelRect? Strip) AmsuRects(Config config, bool poll, TimeSpan now)
    {
        if (_amsuOn != config.AmsuOn) (_amsuOn, _amsuWatch) = (config.AmsuOn, new AmsuWatch());
        if (_brandOn != config.BrandOn) (_brandOn, _brand) = (config.BrandOn, new BrandWatch());
        (_amsuWatch.Rate, _amsuWatch.Bombs, _amsuWatch.Brand) = (config.AmsuRate, _amsuOn, _brandOn ? _brand : null);   // one strip, snap and walk for both
        // Only in a dungeon (the in-game meter or a boss bar, held through cutscenes); cleared on leaving (StepPresence).
        if (!(_amsuOn || _brandOn) || !poll || _fit is null || !_presence.InDungeon(now)) return (null, null);
        if (!Amsu.Readable(_vp, _fit, AmsuSize(config)))   // icons too small to read (80% at 1080p): nothing shown, a bomb up cleared
        {
            if (_amsu.Up.Count > 0) { _amsuWatch = new AmsuWatch(); Publish(now); }   // 낙인 fades on its grace (StepBrand)
            return (null, null);
        }
        _amsuTimed = Amsu.Timed(_vp, AmsuSize(config));
        (_amsuPass, bool bossText) = _amsuWatch.Due(now, _amsuTimed);
        if (_amsuPass == RowPass.None) return (null, null);
        _amsuDaenantu = InDaenantu;   // fixed for this step: the read uses the layout the strip was captured for
        // 대난투 has no boss bar: no HP text to read (ReadAmsu takes the last bar's).
        return (bossText && !_amsuDaenantu ? Geometry.BossTextRow(_vp, _fit) : null, AmsuStripNow(config, _amsuDaenantu));
    }

    /// <summary>대난투 is on (its number read, Engine.Shadow; read for 암수 too, ShadowRead.Gate): the boss debuff row sits under
    /// its fill bar, centred (<see cref="AmsuLayout.Daenantu"/>). For up to ShadowHold (3 s) after it ends the 대난투 row is
    /// still walked: the bombs go unseen meanwhile, the countdown runs on.</summary>
    bool InDaenantu => _shadow is not null;

    int AmsuSize(Config config) => InDaenantu ? Geometry.DaenantuBossSize : config.BossSize;

    /// <summary>The boss debuff strip and its layout, 대난투's or the usual row (암수 and 낙인 read the same row).</summary>
    PixelRect AmsuStripNow(Config config, bool daenantu) =>
        daenantu ? Geometry.DaenantuDebuffStrip(_vp, _fit!) : Geometry.BossDebuffStrip(_vp, _fit!, config.BossSize);

    AmsuLayout AmsuLayoutNow(Config config, bool daenantu) =>
        daenantu ? AmsuLayout.Daenantu(_vp, _fit!) : AmsuLayout.Of(_vp, _fit!, config.BossSize, config.BossShown, config.BossCount);

    /// <summary>Reads what <see cref="AmsuRects"/> captured and steps the countdown. True when the window changes.</summary>
    bool ReadAmsu(Config config, Image? text, Image? strip, TimeSpan t)
    {
        // In 대난투 the boss is the last bar's (the same boss: ShadowStep keeps it), so bombs are still identified there.
        long? boss = _amsuDaenantu ? _lastBar?.Max : text is null ? null : BossBar(text).Hp?.Max;
        // 강화 읽기: the row's levels stretched (Reinforce.Levels; 1440 → 1080 sweep 74 → 100%).
        if (strip is not null && GameWindow.ReadsReinforced) strip = Reinforce.Levels(strip);
        var (edges, changed) = _amsuWatch.Read(AmsuLayoutNow(config, _amsuDaenantu), _amsuPass, boss, strip, t);
        foreach (var e in edges)
            System.Diagnostics.Trace.WriteLine($"[amsu] {e.Kind} {e.Change}" + (e.Change == AmsuChange.끝 ? "" : $", {e.Left:0.0} s left") + $" (line {_amsuWatch.Line})");
        if (Tracing) TraceRead(config, boss, strip, t);
        return changed;
    }

    // Dev trace of every 암수 read (GAUGE_AMSU_TRACE=1, 2026-11-01: the in-game miss of a 빛암 in the second slot): its time, the
    // pass, the boss read, the strip's rect and size, the row's line and what was read where, so a replay can be lined up with
    // the video.
    static readonly bool Tracing = Environment.GetEnvironmentVariable("GAUGE_AMSU_TRACE") is "1";

    void TraceRead(Config config, long? boss, Image? strip, TimeSpan t)
    {
        var row = _amsuWatch.LastRow;
        var view = Amsu.View(_amsuWatch.State, t);
        System.Diagnostics.Trace.WriteLine($"[amsu-trace] t={t.TotalSeconds:0.00} {_amsuPass} boss={(boss is null ? "-" : "ok")} "
            + $"rect={AmsuStripNow(config, _amsuDaenantu)}{(_amsuDaenantu ? " daenantu" : "")} strip={strip?.Width}x{strip?.Height} line={_amsuWatch.Line} "
            + $"[{string.Join(" ", row?.Bombs.Select(b => $"{b.Kind}@{b.Slot}:{b.Seconds?.ToString() ?? "-"}") ?? [])}] full={row?.Full} seen={row?.Seen} "
            + $"| {view.Shown} {view.Left:0.0}{(view.Seen ? "" : " unseen")} timed={_amsuTimed} vp={_vp} hud={_fit!.Hud:0.##}");
        // A read with the boss's HP that found no bomb: the strip as the game showed it (once a second, at most ShotsMax), so
        // the reader can be run on it in a test.
        if (boss is null || strip is null || row is { Bombs.Count: > 0 } || _traceShots >= ShotsMax || !RowCadence.Elapsed(t, _traceShotAt, TimeSpan.FromSeconds(1))) return;
        (_traceShotAt, _traceShots) = (t, _traceShots + 1);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaugeOverlay", "logs", "amsu-shots");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, $"{t.TotalSeconds:0.00}.png");
        Platform.Png.Save(file, strip);
        System.Diagnostics.Trace.WriteLine($"[amsu-trace] saved {file}");
    }

    const int ShotsMax = 40;
    TimeSpan _traceShotAt = TimeSpan.MinValue;
    int _traceShots;
}
