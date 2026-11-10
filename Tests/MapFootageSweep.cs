using System.Globalization;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>Dev only (GAUGE_MAP_VIDEO, FFMPEG, GAUGE_SWEEP_OUT): the 보스 체력 and 대난투 labels over a 1920×1080 HUD 110 clip,
/// stepped as the engine steps them (Engine.Presence StepBossHp, Engine.Shadow ShadowStep), 10 frames a second, for
/// tools/make-map-gifs.ps1 to draw over the footage (the 표시 map's hover GIFs, user 2026-11-10). Lines: "#rect name x y w h"
/// for each label's place, then "t\tboss\tshadow\tdealt" (empty: not shown).</summary>
public class MapFootageSweep
{
    static readonly TimeSpan ShadowHold = TimeSpan.FromSeconds(3);   // Engine.Shadow's

    [Fact]
    public void Labels_over_a_video_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_MAP_VIDEO") is not { } video) return;
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(Geometry.ReferenceHud));
        double scale = Geometry.Scale(vp, fit);
        var (row, number) = (Geometry.BossTextRow(vp, fit), Geometry.ShadowNumber(vp, fit));
        var lines = new List<string>();
        foreach (var (name, r) in new[] { ("boss", Geometry.BossHpLabel(vp, fit)), ("bar", Geometry.ShadowBar(vp, fit)), ("dealt", Geometry.ShadowDealt(vp, fit)) })
            lines.Add($"#rect {name} {r.X} {r.Y} {r.Width} {r.Height}");

        var label = BossHpLabel.None;
        (long Current, long Max, int? Count)? lastBar = null;
        ShadowHp? shadow = null, before = null;
        var shadowAt = TimeSpan.Zero;
        using var rows = JeonbunSweep.Frames(video, row.X, row.Y, row.Width, row.Height, fps: 10).GetEnumerator();
        foreach (var (t, numberImg) in JeonbunSweep.Frames(video, number.X, number.Y, number.Width, number.Height, fps: 10))
        {
            if (!rows.MoveNext()) break;
            var at = TimeSpan.FromSeconds(t);
            var bar = BossDigits.ReadBar(rows.Current.Image, scale);
            label = label.Step(bar.Hp is var (current, _, shield) ? new BossHpView(current, shield, default) : null, at);
            if (bar.Hp is var (cur, max, _))
                (lastBar, shadow, before) = ((cur, max, bar.Count ?? (lastBar is { Max: var m, Count: var c } && m == max ? c : null)), null, null);
            else
            {
                long? read = BossDigits.ReadNumber(numberImg, scale);
                shadow = shadow is null ? ShadowHp.Resume(before, lastBar, read) : ShadowHp.Step(shadow, lastBar, read);
                if (read is not null) shadowAt = at;
                else if (shadow is not null && at - shadowAt > ShadowHold) (shadow, before) = (null, shadow);
            }
            var view = shadow is { Dead: false } s ? new ShadowView(s.Remaining, s.Lines, default, s.Dealt, default) : null;
            lines.Add(string.Join('\t', t.ToString("0.0", CultureInfo.InvariantCulture), label.View?.Text ?? "", view?.Text ?? "", view is null ? "" : EasyRead.Of(view.Dealt)));
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("GAUGE_SWEEP_OUT")!, lines);
    }
}
