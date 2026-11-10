using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>The 단심 window (user, 2026-10-13). 막대: full at the trigger and draining (가로: right to left, 세로: top to
/// bottom) over 심판's 6 s in <see cref="JudgmentSettings.Color"/>, then full again over the 24 s cooldown in
/// <see cref="JudgmentSettings.CooldownColor"/> or a dim of the colour, with the seconds on it (시간 표시); full and blinking
/// when ready, or gone with 비었을 때 숨기기. 아이콘: 심판's icon with its seconds while it is on, grey with the seconds to ready
/// through the cooldown, a flash as it is ready, then 단죄's icon only while 단죄 runs (JudgmentState.Icon).</summary>
public sealed partial class OverlayRenderer
{
    const double JudgmentBarLength = 4, JudgmentBarThickness = 0.45;   // × a skill box, × the window's 너비 / 높이
    const double JudgmentDim = 0.45;                                    // 한 색으로: the cooldown part at this lightness
    const double JudgmentIconText = 0.55;                               // the seconds, share of the icon's height
    static readonly Color JudgmentFlash = Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF);
    static readonly Color ConvictionColor = Color.FromRgb(0xFF, 0xD5, 0x4A);   // 단죄's 3 s (user, 2026-11-10: yellow)
    static readonly BitmapSource JudgmentIcon = JudgmentImage(JudgmentReader.Icon), ConvictionIcon = JudgmentImage(JudgmentReader.ConvictionIcon);
    static readonly ImageSource JudgmentGrey = Frozen(new FormatConvertedBitmap(JudgmentIcon, PixelFormats.Gray8, null, 0));   // the cooldown

    static BitmapSource JudgmentImage(Image icon) =>
        Frozen(BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Bgr32, null, icon.Bgra, icon.Stride));

    /// <summary>The 단심 window: 심판's cycle alone.</summary>
    public bool JudgmentOnly { get; set; }

    Rect JudgmentCell()
    {
        var j = Settings.Judgment;
        if (j.Display == JudgmentDisplay.ReadyIcon) return new Rect(Bleed, Bleed, BoxSize(j.Width), BoxSize(j.Width));
        return j.Side
            ? new Rect(Bleed, Bleed, BoxSize(j.Width, JudgmentBarThickness), BoxSize(j.Height, JudgmentBarLength))
            : new Rect(Bleed, Bleed, BoxSize(j.Width, JudgmentBarLength), BoxSize(j.Height, JudgmentBarThickness));
    }

    Size JudgmentPanelSize()
    {
        var c = JudgmentCell();
        return new(c.Right + Bleed, c.Bottom + Bleed);
    }

    const double ReadyBlinkHz = 1, ReadyBlinkLow = 0.15;   // 심판 ready: a slow, deep fade (user, 2026-11-10)

    void DrawJudgment(DrawingContext dc, TimeSpan now, double ms, bool reduced)
    {
        if (_player?.Snapshot.Judgment is not { } state) return;
        var j = Settings.Judgment;
        var cell = JudgmentCell();
        bool icon = j.Display == JudgmentDisplay.ReadyIcon, conviction = state.ConvictionLeft(now) is not null;
        bool waiting = state.Phase(now) == JudgmentPhase.Ready && !conviction;   // ready with nothing running: blinks
        dc.PushOpacity(Math.Clamp(j.Opacity, JudgmentSettings.MinOpacity, 1));
        if (icon) { if (state.Icon(now) is { } shown) DrawJudgmentIcon(dc, cell, shown, reduced); }   // ready, no 단죄: nothing
        else Faint(dc, j.HideWhenEmpty && waiting, () => DrawJudgmentBar(dc, cell, state, now, waiting ? Blink(ms, reduced, ReadyBlinkHz, ReadyBlinkLow) : 1, j));
        dc.Pop();
        // On the clock: the bar drains or blinks (ready), the countdown ticks; the icon's flash asks for its own frames.
        if (_frozenAt is null) { _moving |= !waiting; _blinking |= waiting && !icon && !reduced && !j.HideWhenEmpty; }
    }

    /// <summary>Under reduced motion there is no white flash.</summary>
    void DrawJudgmentIcon(DrawingContext dc, Rect cell, JudgmentIcon i, bool reduced)
    {
        _animating |= _frozenAt is null && !reduced && i.Flash;   // the flash wants smooth frames
        dc.DrawImage(i.Conviction ? ConvictionIcon : i.Dim ? JudgmentGrey : JudgmentIcon, cell);
        if (i.Flash && !reduced) dc.DrawRectangle(Solid(JudgmentFlash), null, cell);
        double font = Math.Round(cell.Height * JudgmentIconText / TimerFontStep) * TimerFontStep;
        if (i.Seconds is { } s && font >= MinTimerFont) DrawOutlined(dc, cell, JudgmentState.LabelText(s), font);
    }

    /// <param name="readyBlink">심판 ready (user, 2026-11-10): the bar full in 심판's colour at this opacity, blinking.</param>
    void DrawJudgmentBar(DrawingContext dc, Rect cell, JudgmentState state, TimeSpan now, double readyBlink, JudgmentSettings j)
    {
        var colour = Parse(j.Color);
        var wait = j.OneColor ? Level(colour, JudgmentDim) : Parse(j.CooldownColor);
        dc.DrawRectangle(Solid(DebuffEmpty), null, cell);
        var (active, cooldown) = state.Fill(now);
        var conviction = state.ConvictionLeft(now);
        bool ready = state.Phase(now) == JudgmentPhase.Ready && conviction is null;
        if (ready) dc.PushOpacity(readyBlink);
        var (fill, brush) = conviction is { } c ? (c / JudgmentState.ConvictionDuration.TotalSeconds, ConvictionColor)
            : ready ? (1.0, colour) : active > 0 ? (active, colour) : (cooldown, wait);
        if (fill > 0)
            dc.DrawRectangle(Solid(brush), null, j.Side
                ? new Rect(cell.X, cell.Bottom - cell.Height * fill, cell.Width, cell.Height * fill)   // from the bottom: drains from the top
                : new Rect(cell.X, cell.Y, cell.Width * fill, cell.Height));                          // from the left: drains from the right
        if (ready) dc.Pop();
        double font = Math.Round(Math.Min(cell.Width, cell.Height) * 0.75 / TimerFontStep) * TimerFontStep;
        if (j.ShowLabel && font >= MinTimerFont && state.LabelSeconds(now) is { } s)
            DrawOutlined(dc, cell, JudgmentState.LabelText(s), font);
    }
}
