using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>단심 (user, 2026-10-13): 심판's icon in the buff row (ten boxes deep) and its text round the character, read only
/// while 심판 can show (ready, or on and a second past); the cycle itself runs on the clock.</summary>
public sealed partial class Engine
{
    JudgmentState _judgment = JudgmentState.Ready;
    bool _judgmentOn;
    string? _judgmentFor;   // whose cycle _judgment is
    readonly IconGate<bool> _judgmentIcon = new(JudgmentReader.IsIcon);   // 심판 boxes identified on change (icon-gates 3)
    readonly IconGate<bool> _convictionIcon = new(JudgmentReader.IsConviction);   // 단죄 boxes, the same way

    /// <summary>What to capture for 단심 this step: the buff row (icon) and the band round the character (text), as
    /// <paramref name="plan"/>'s 감지 방식 asks; neither while it is off, between polls or through the cooldown.</summary>
    (PixelRect? Row, PixelRect? Band) JudgmentRects(JudgmentPlan? plan, bool poll, TimeSpan now)
    {
        if (plan is null || !poll || !_judgment.Watching(now)) return (null, null);
        return (plan.Source != JudgmentSource.Text ? Geometry.BuffArea(_vp, _fit!, plan.BuffSize, Geometry.JudgmentBoxCount) : null,
            plan.Source != JudgmentSource.Icon ? Geometry.JudgmentBand(_vp) : null);
    }

    /// <summary>단심 turned on or off, or on for another character (whose cycle isn't this one's): published at once, so the
    /// window shows it without waiting for another change. A character briefly unknown (another class on screen) keeps it.</summary>
    void SwitchJudgment(Config config, TimeSpan now)
    {
        bool on = config.Judgment is not null, changed = on != _judgmentOn;
        _judgmentOn = on;
        if (on && config.Character is { } who && who != _judgmentFor)
        {
            (_judgmentFor, _judgment) = (who, JudgmentState.Ready);
            changed = true;
        }
        if (changed) Publish(now);
    }

    /// <summary>Reads 심판's icon (and its timer) in <paramref name="row"/> and its text in <paramref name="band"/>, either
    /// may be null (not captured), and 단죄's icon and timer in the row; moves the cycle. Returns true if 심판's or 단죄's
    /// start moved. A 단죄 seen never stops the search for 심판 (user, 2026-11-10: it may start a frame later).</summary>
    bool ReadJudgment(JudgmentPlan plan, Image? row, Image? band, TimeSpan t)
    {
        bool icon = false, text = false, conviction = false;
        double? seconds = null, convictionSeconds = null;
        if (row is not null)
        {
            var origin = Geometry.BuffArea(_vp, _fit!, plan.BuffSize, Geometry.JudgmentBoxCount);
            PixelRect Local(PixelRect r) => r with { X = r.X - origin.X, Y = r.Y - origin.Y };
            var timers = new BuffReader(Geometry.BuffScale(_vp, plan.BuffSize));
            foreach (var (box, i) in Geometry.BuffBoxes(_vp, _fit!, plan.BuffSize, Geometry.JudgmentBoxCount).Select((b, i) => (b, i)))
            {
                if (_judgmentIcon.Classify(row, i, Local(box.Icon)))
                {
                    (icon, seconds) = (true, timers.ReadTimer(row, Local(box.Timer)));
                    BoxDebug("심판 아이콘", row, Local(box.Icon), Local(box.Timer), "심판", seconds);
                    break;
                }
                if (conviction || !_convictionIcon.Classify(row, i, Local(box.Icon))) continue;
                (conviction, convictionSeconds) = (true, timers.ReadTimer(row, Local(box.Timer)));
                BoxDebug("단죄 아이콘", row, Local(box.Icon), Local(box.Timer), "단죄", convictionSeconds);
            }
        }
        if (band is not null && !icon && _hud.Visible == true) text = JudgmentText.Find(band, _vp.Height / Geometry.ReferenceHeight, plan.FontSize);
        var next = JudgmentTracker.Update(_judgment, new(icon, text, seconds, conviction, convictionSeconds), t, _hud.Visible == true);
        if (_config.Debug && band is not null)
            ExtraDebug?.Invoke(new("심판 글자", band.Copy(), text ? "심판" : "없음", next.Phase(t).ToString()));
        bool moved = next.TriggeredAt != _judgment.TriggeredAt || next.ConvictionAt != _judgment.ConvictionAt;
        _judgment = next;
        return moved;
    }
}
