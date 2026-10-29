using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The smaller drawing pieces: the gauge bar, buff bars, ready counts, the end flash, the ready band, outlined text, faint hidden parts.</summary>
public sealed partial class OverlayRenderer
{
    /// <summary>Your gauge bar: each segment's fill in its colour, then one glow in the chosen style over the whole glowing
    /// part (steady under reduced motion), then the split marks.</summary>
    void DrawBar(DrawingContext dc, Rect g, string key, GaugeBar bar, Snapshot s, double v, double ms, bool reduced)
    {
        var segments = bar.Segments;
        // 색 단계 and the filling colour, eased: a section turning full fades gold → blue, the ramp deepens smoothly.
        var colors = Get(_barColors, key).Value([.. bar.ColorsAt(s.Gauge ?? 0).Select(Parse)], ms, reduced, ref _animating);
        for (int i = 0; i < segments.Count; i++)
        {
            var (from, to) = segments[i];
            double end = Math.Min(to, v);
            if (end > from) dc.DrawRectangle(Solid(colors[i]), null, new Rect(g.X + g.Width * from, g.Y, g.Width * (end - from), g.Height));
        }
        bool ready = bar.Glow?.Slot is { } slot && s[slot].Phase == SlotPhase.Ready;
        if (bar.GlowSpan(s.Gauge ?? 0, ready) is { } span && Math.Min(span.To, v) > span.From)
        {
            // One glow over the whole glowing part, across the splits, not one per segment.
            if (!reduced && _frozenAt is null) _moving = true;
            double level = reduced ? 0.6 : GaugeBar.GlowLevel(ms, bar.Period);
            var fill = new Rect(g.X + g.Width * span.From, g.Y, g.Width * (Math.Min(span.To, v) - span.From), g.Height);
            switch (bar.Style)
            {
                case GlowStyle.Pulse:
                    dc.PushOpacity(GlowAlpha * level);
                    dc.DrawRectangle(Brushes.White, null, fill);
                    dc.Pop();
                    break;
                case GlowStyle.Outline:
                    dc.PushOpacity(0.4 + 0.6 * level);
                    dc.DrawRectangle(null, GlowPen, new Rect(fill.X + 0.5, fill.Y + 0.5, Math.Max(0, fill.Width - 1), Math.Max(0, fill.Height - 1)));
                    dc.Pop();
                    break;
                case GlowStyle.Shine:
                    double at = reduced ? 0.5 : ms / (Math.Max(0.1, bar.Period) * 1000) % 1;
                    double w = Math.Max(2, fill.Width * 0.25), x = fill.X - w + (fill.Width + w) * at;
                    double left = Math.Max(fill.X, x), right = Math.Min(fill.Right, x + w);   // the band cut to the fill
                    if (right <= left) break;
                    dc.PushOpacity(GlowAlpha);
                    dc.DrawRectangle(Brushes.White, null, new Rect(left, fill.Y, right - left, fill.Height));
                    dc.Pop();
                    break;
            }
        }
        var mark = Solid(Parse(bar.SplitColor));
        foreach (double split in bar.Splits)
            dc.DrawRectangle(mark, null, new Rect(g.X + g.Width * split - bar.SplitThickness / 2, g.Y, bar.SplitThickness, g.Height));
    }

    /// <summary>A 쿨 돈 뒤 지난 시간 number in the timer's place, in its own colour.</summary>
    void DrawReadyCount(DrawingContext dc, Rect r, int count)
    {
        var fill = Parse(Settings.Overlay.ReadyCountColor);
        DrawTimer(dc, r, count.ToString(CultureInfo.InvariantCulture), fill, Lightness(fill) < LightFill ? LightOutlinePen : OutlinePen);
    }

    /// <summary>A buff bar: a dim track, and while the buff is on the time left in its colour, draining right → left, with
    /// "6s" (from 1 s up) or "0.4s" over it when 시간 표시 is on. An upright bar (왼쪽 바깥) drains top → bottom and has no
    /// room for the time. A buff whose timer was never read shows full.</summary>
    /// 마지막 1초 깜빡임 (<paramref name="flash"/>): below <see cref="EndFlashFrom"/> s left, a white band sweeps the bar in its drain
    /// direction <see cref="EndFlashSweeps"/> times, the last one ending at 0 (steady under reduced motion).
    void DrawBuff(DrawingContext dc, Rect r, Color c, BuffState? st, bool label, bool flash, TimeSpan now, bool reduced)
    {
        dc.DrawRoundedRectangle(Solid(Level(c, ChangedOffLevel)), null, r, BoxRadius, BoxRadius);
        if (st is not { Active: true }) return;
        dc.PushClip(BoxClip(r));
        bool upright = r.Height > r.Width;
        double left = Settings.Buffs.Drain ? st.Progress(now) : 1;   // 버프 막대 줄어듦 off: full while on
        dc.DrawRectangle(Solid(c), null, upright ? new Rect(r.X, r.Bottom - r.Height * left, r.Width, r.Height * left) : new Rect(r.X, r.Y, r.Width * left, r.Height));
        dc.Pop();
        if (st.EndsAt is null) return;
        if (_frozenAt is null) _moving = true;
        double rem = st.Remaining(now).TotalSeconds;
        if (flash && rem is > 0 and < EndFlashFrom) DrawEndFlash(dc, r, upright, rem, reduced);
        if (!label || upright) return;
        double sec = st.Remaining(now).TotalSeconds;
        // Rounded down as the game does: "1s" from 1.9 to 1 s left.
        string text = sec >= 1 ? $"{Math.Floor(sec).ToString(CultureInfo.InvariantCulture)}s"
            : $"{(Math.Floor(sec * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture)}s";
        DrawOutlined(dc, r, text, Math.Floor((r.Height - 2) / DigitPair.Height / TimerFontStep) * TimerFontStep);
    }

    /// <summary>Draws a part that 비었을 때 숨기기 hides while <paramref name="hidden"/>: not at all, or faintly where hidden parts
    /// are shown anyway (<see cref="ShowHidden"/>: 편집 모드, 미리보기, the 레이아웃 편집기), to be seen and placed.</summary>
    void Faint(DrawingContext dc, bool hidden, Action draw)
    {
        if (!hidden) { draw(); return; }
        if (!ShowHidden) return;
        dc.PushOpacity(HiddenOpacity);
        draw();
        dc.Pop();
    }

    void DrawEndFlash(DrawingContext dc, Rect r, bool upright, double rem, bool reduced)
    {
        dc.PushClip(BoxClip(r));
        if (reduced)
        {
            dc.PushOpacity(GlowAlpha * 0.6);
            dc.DrawRectangle(Brushes.White, null, r);
        }
        else
        {
            // Horizontal bars drain right → left, upright ones top → bottom: the band runs the same way over the whole bar.
            double at = (EndFlashFrom - rem) / EndFlashFrom * EndFlashSweeps % 1, len = upright ? r.Height : r.Width, w = len * 0.4, pos = -w + (len + w) * at;
            dc.PushOpacity(GlowAlpha);
            dc.DrawRectangle(Brushes.White, null, upright ? new Rect(r.X, r.Y + pos, r.Width, w) : new Rect(r.Right - pos - w, r.Y, w, r.Height));
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>A straight band in <paramref name="brush"/> across a box, top-left → bottom-right (<paramref name="diagonal"/>)
    /// or left → right, <paramref name="at"/> of the way (0 before the box, 1 past it); null = a steady tint (reduced motion).</summary>
    void DrawBand(DrawingContext dc, Rect r, double? at, Brush brush, bool diagonal)
    {
        dc.PushClip(BoxClip(r));
        dc.PushOpacity(at is null ? GlowAlpha * 0.6 : GlowAlpha);
        if (at is not { } a) dc.DrawRectangle(brush, null, r);
        else if (!diagonal)
        {
            double w = r.Width * 0.4, x = r.X - w + (r.Width + w) * a;
            dc.DrawRectangle(brush, null, new Rect(x, r.Y, w, r.Height));
        }
        else
        {
            // Between the lines x + y = pos and pos + w (box-local), pos running from -w to W + H.
            double span = r.Width + r.Height, w = span * 0.3, pos = -w + (span + w) * a;
            var band = new StreamGeometry();
            using (var g = band.Open())
            {
                g.BeginFigure(new Point(r.X + pos, r.Y), true, true);
                g.PolyLineTo([new Point(r.X + pos + w, r.Y), new Point(r.X + pos + w - r.Height, r.Bottom), new Point(r.X + pos - r.Height, r.Bottom)], false, false);
            }
            band.Freeze();
            dc.DrawGeometry(brush, null, band);
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>White text with a dark outline, centred in <paramref name="r"/>; nothing below <see cref="MinTimerFont"/>.</summary>
    void DrawOutlined(DrawingContext dc, Rect r, string text, double size, Color? fill = null, Pen? outline = null)
    {
        if (size < MinTimerFont) return;
        var geo = TextGeometry(text, size);
        var bounds = geo.Bounds;
        dc.PushTransform(new TranslateTransform(
            Math.Round(r.X + (r.Width - bounds.Width) / 2 - bounds.X),
            Math.Round(r.Y + (r.Height - bounds.Height) / 2 - bounds.Y)));
        dc.DrawGeometry(null, outline ?? OutlinePen, geo);   // centred 2 px stroke = 1 px outline outside the glyph
        dc.DrawGeometry(Solid(fill ?? TextColor), null, geo);
        dc.Pop();
    }
}
