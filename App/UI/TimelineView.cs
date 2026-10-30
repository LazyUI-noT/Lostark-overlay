using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록's timeline (spec § D Timeline view): for each record shown (two for 비교, on one time axis lined up at
/// their offsets), one chart with each player's DPS (solid, left scale) and 누적 피해 (dashed, right scale), mine gold and on
/// top; below it one row of my skill casts in time order (my skill icon where I have one); then a line per buff (공증 red,
/// 아덴 피증 yellow, 초각스 피증 blue) lit while it was up, with its share of the fight (and of the seconds shown when zoomed).
/// Phase markers dashed. Hovering shows each player's figures, my buffs and casts at that second (as the game's 전투 분석기).
/// Wheel or +/− zooms around the cursor, dragging or ←/→ scrubs. Draws only while shown.</summary>
sealed class TimelineView : FrameworkElement
{
    const double LabelW = 96, RightW = 52, HeadH = 20, LegendH = 18, CurveH = 150, SkillH = 3 * (Icon + 2) + 4, BuffH = 16, AxisH = 18, Gap = 12, Icon = 16;
    const double BlockHeight = HeadH + LegendH + CurveH + 8 + SkillH + 4 + 3 * BuffH;
    internal static readonly (BuffKind Kind, string Name, Color Color)[] Buffs =
    [
        (BuffKind.공증, "공증", Color.FromRgb(0xE0, 0x52, 0x52)),
        (BuffKind.아덴피증, "아덴 피증", Color.FromRgb(0xF5, 0xC5, 0x42)),
        (BuffKind.초각스피증, "초각스 피증", Color.FromRgb(0x4A, 0x90, 0xE2)),
    ];
    static readonly Color Mine = Color.FromRgb(0xFF, 0xC8, 0x3C);
    static readonly Color[] Others =
    [
        Color.FromRgb(0x5B, 0x8D, 0xEF), Color.FromRgb(0x5B, 0xD1, 0x8B), Color.FromRgb(0xE0, 0x6C, 0x75), Color.FromRgb(0xC6, 0x78, 0xDD),
        Color.FromRgb(0x56, 0xB6, 0xC2), Color.FromRgb(0xD1, 0x9A, 0x66), Color.FromRgb(0x98, 0xC3, 0x79),
    ];
    static readonly Pen PhasePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(160, 0xE0, 0x60, 0x60))), 1) { DashStyle = DashStyles.Dash });
    static readonly DashStyle Dashed = Frozen(new DashStyle([4, 3], 0));

    IReadOnlyList<(FightRecord Record, double Offset, IReadOnlyDictionary<SlotId, ImageSource> Icons)> _shown = [];
    TimeWindow _window = TimeWindow.Whole(0);
    double _length;
    Point? _drag;
    double? _hover;   // the cursor's x while over the view and not dragging: values at that second

    public TimelineView() => (Focusable, ClipToBounds, Cursor) = (true, true, Cursors.SizeWE);

    /// <summary>The records to draw, each with the second that lines up at 0 and my skill icons; the whole length shows.</summary>
    public void Show(IReadOnlyList<(FightRecord Record, double Offset, IReadOnlyDictionary<SlotId, ImageSource> Icons)> shown)
    {
        _shown = shown;
        _length = shown.Count == 0 ? 0 : shown.Max(s => s.Record.Duration - s.Offset);
        _window = TimeWindow.Whole(_length);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 600 : available.Width, _shown.Count * (BlockHeight + Gap) + AxisH);

    double PlotW => Math.Max(1, ActualWidth - LabelW - RightW);
    double X(double t, double offset) => LabelW + _window.X(t - offset, PlotW);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // the whole area takes the wheel and drags
        var fg = TextElement.GetForeground(this) ?? Brushes.Gray;
        double y = 0;
        foreach (var (r, offset, icons) in _shown) y = DrawRecord(dc, r, offset, icons, y, fg) + Gap;
        if (_shown.Count > 0) DrawAxis(dc, y, fg);
    }

    double DrawRecord(DrawingContext dc, FightRecord r, double offset, IReadOnlyDictionary<SlotId, ImageSource> icons, double y, Brush fg)
    {
        var seen = new TimeWindow(_window.From + offset, _window.To + offset);   // the seconds shown, on this record's clock
        bool zoomed = _window.From > 0 || _window.To < r.Duration - offset;
        double bottom = y + BlockHeight;
        Text(dc, $"{r.StartedAt:MM-dd HH:mm} · {r.Gate ?? "알 수 없음"} · {Clock(r.Duration)}", new(0, y + 2), 12, fg, bold: true, width: ActualWidth);
        y += HeadH;
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, bottom - y)));
        foreach (double p in r.Phases) { double px = X(p, offset); dc.DrawLine(PhasePen, new(px, y), new(px, bottom)); }
        dc.Pop();

        // Who, in what colour, with their average DPS over the seconds shown.
        var players = Timeline.DpsAverages(r, seen);
        var colours = new Dictionary<string, Color>();
        int other = 0;
        foreach (var (name, _) in players) colours[name] = name == r.Me ? Mine : Others[other++ % Others.Length];
        Text(dc, "평균 DPS", new(0, y + 2), 11, fg, opacity: 0.7);
        double lx = LabelW;
        foreach (var (name, dps) in players)
        {
            dc.DrawRectangle(Frozen(new SolidColorBrush(colours[name])), null, new(lx, y + 5, 8, 8));
            lx += 11 + Text(dc, $"{name} {Amount(dps)}", new(lx + 11, y + 1), 11, fg, bold: name == r.Me) + 12;
        }
        y += LegendH;
        DrawCurves(dc, r, offset, seen, colours, y, fg);
        y += CurveH + 8;
        DrawSkills(dc, r, offset, seen, icons, y, fg);
        y += SkillH + 4;
        DrawBuffs(dc, r, offset, seen, zoomed, y, fg);
        if (_hover is { } hx && hx >= LabelW && hx <= LabelW + PlotW) DrawHover(dc, r, offset, colours, hx, bottom - BlockHeight + HeadH, bottom);
        return bottom;
    }

    // DPS solid on the left scale, 누적 피해 dashed on the right; mine drawn last, on top.
    void DrawCurves(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, Dictionary<string, Color> colours, double y, Brush fg)
    {
        var grid = Faint(fg, 0.12);
        if (r.Samples.Count == 0)   // no 전분 figures this fight: no scale to show
        {
            dc.DrawLine(grid, new(LabelW, y + CurveH - 4), new(LabelW + PlotW, y + CurveH - 4));
            Text(dc, "전분 수치 없음 (피해량 · 초당 피해량이 기록되지 않았습니다)", new(LabelW + 8, y + CurveH / 2 - 8), 11, fg, opacity: 0.6, width: PlotW - 16);
            return;
        }
        long dpsMax = Math.Max(1, r.Samples.Max(s => s.Dps));
        long dmgMax = Math.Max(1, r.Samples.Max(s => s.Damage));
        for (int g = 0; g <= 4; g++)   // five levels on both scales: 초당 피해량 left, 피해량 right
        {
            double gy = y + CurveH - 4 - (CurveH - 8) * g / 4;
            dc.DrawLine(grid, new(LabelW, gy), new(LabelW + PlotW, gy));
            Text(dc, Amount(dpsMax * g / 4.0), new(0, gy - 7), 10, fg, opacity: 0.6, width: LabelW - 6, right: true);
            Text(dc, Amount(dmgMax * g / 4.0), new(LabelW + PlotW + 4, gy - 7), 10, fg, opacity: 0.6, width: RightW - 4);
        }
        Text(dc, "— 초당 피해량   ┄ 피해량 (오른쪽)", new(LabelW + 4, y + 2), 10, fg, opacity: 0.6);
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, CurveH)));
        foreach (var g in r.Samples.GroupBy(s => s.Name).OrderBy(g => g.Key == r.Me))
        {
            var near = g.Where(s => s.T >= seen.From - 1 && s.T <= seen.To + 1).ToList();
            if (near.Count < 2) continue;
            var brush = Frozen(new SolidColorBrush(colours.GetValueOrDefault(g.Key, Others[0])));
            bool me = g.Key == r.Me;
            Line(dc, near.Select(s => new Point(X(s.T, offset), y + CurveH - 4 - (CurveH - 8) * s.Dps / dpsMax)), new Pen(brush, me ? 2 : 1.2));
            Line(dc, near.Select(s => new Point(X(s.T, offset), y + CurveH - 4 - (CurveH - 8) * s.Damage / dmgMax)),
                new Pen(brush, me ? 1.6 : 1) { DashStyle = Dashed });
        }
        dc.Pop();
    }

    static void Line(DrawingContext dc, IEnumerable<Point> points, Pen pen)
    {
        var pts = points.ToList();
        var line = new StreamGeometry();
        using (var c = line.Open()) { c.BeginFigure(pts[0], false, false); c.PolyLineTo(pts.Skip(1).ToList(), true, false); }
        line.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    // My casts on one row in time order, on two sub-rows so nearby casts don't cover each other.
    void DrawSkills(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, IReadOnlyDictionary<SlotId, ImageSource> icons, double y, Brush fg)
    {
        Text(dc, "내 스킬", new(0, y + 12), 11, fg, opacity: 0.8);
        dc.DrawLine(Faint(fg, 0.12), new(LabelW, y + SkillH), new(LabelW + PlotW, y + SkillH));
        dc.PushClip(new RectangleGeometry(new(LabelW - Icon / 2, y, PlotW + Icon, SkillH)));
        var chip = Faint(fg, 0.25).Brush;
        double[] last = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];
        foreach (var e in Timeline.Visible(r, seen).Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Enum.IsDefined((SlotId)e.What)))
        {
            double x = X(e.T, offset);
            int lane = Array.FindIndex(last, l => x - l >= Icon);
            if (lane < 0) lane = Array.IndexOf(last, last.Min());   // all three taken: over the oldest
            last[lane] = x;
            var at = new Rect(x - Icon / 2, y + 2 + lane * (Icon + 2), Icon, Icon);
            var slot = (SlotId)e.What;
            if (icons.TryGetValue(slot, out var img)) { dc.DrawImage(img, at); continue; }
            dc.DrawRoundedRectangle(chip, null, at, 3, 3);
            Text(dc, SlotShort(slot), new(at.X + 1, at.Y + 1), 9, fg, bold: true, width: Icon - 2);
        }
        dc.Pop();
    }

    // A line per buff, lit (with a soft glow) while it was up; the label has its uptime.
    void DrawBuffs(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, bool zoomed, double y, Brush fg)
    {
        var spans = Timeline.BuffSpans(r);
        var track = new Pen(Faint(fg, 0.12).Brush, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var (kind, name, color) in Buffs)
        {
            double cy = y + BuffH / 2;
            double whole = Timeline.Uptime(r, kind, new TimeWindow(0, r.Duration));
            Text(dc, $"{name} {whole * 100:0}%" + (zoomed ? $" ({Timeline.Uptime(r, kind, seen) * 100:0}%)" : ""), new(0, y), 11, fg);
            dc.DrawLine(track, new(LabelW, cy), new(LabelW + PlotW, cy));
            var glow = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B))), 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
            var lit = Frozen(new Pen(Frozen(new SolidColorBrush(color)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
            foreach (var (from, to, _) in spans.Where(s => s.Kind == kind))
            {
                double x0 = Math.Max(LabelW, X(from, offset)), x1 = Math.Min(LabelW + PlotW, X(to, offset));
                if (x1 <= x0) continue;
                dc.DrawLine(glow, new(x0, cy), new(x1, cy));
                dc.DrawLine(lit, new(x0, cy), new(x1, cy));
            }
            y += BuffH;
        }
    }

    static readonly Brush TipBack = Frozen(new SolidColorBrush(Color.FromArgb(235, 0x16, 0x18, 0x20)));
    static readonly Brush TipText = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0)));
    static readonly Pen CrossPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(150, 0xFF, 0xFF, 0xFF))), 1));

    // The figures at the hovered second: everyone's 피해량 and 초당 피해량 (full numbers, as the game shows them), my buffs, my casts.
    void DrawHover(DrawingContext dc, FightRecord r, double offset, Dictionary<string, Color> colours, double hx, double top, double bottom)
    {
        double t = _window.From + (hx - LabelW) / PlotW * _window.Span + offset;
        dc.DrawLine(CrossPen, new(hx, top), new(hx, bottom));
        var lines = new List<(string Text, Brush Brush)> { (Clock(t), TipText) };
        foreach (var g in r.Samples.GroupBy(s => s.Name).OrderBy(g => g.Key != r.Me))
            if (g.LastOrDefault(s => s.T <= t) is { } s)
                lines.Add(($"{g.Key}  피해량 {s.Damage:N0} · 초당 {s.Dps:N0}", Frozen(new SolidColorBrush(colours.GetValueOrDefault(g.Key, Others[0])))));
        var on = Timeline.BuffSpans(r).Where(b => b.From <= t && t < b.To).Select(b => Buffs.First(x => x.Kind == b.Kind).Name).Distinct().ToList();
        lines.Add(($"버프: {(on.Count == 0 ? "없음" : string.Join(", ", on))}", TipText));
        var casts = r.Events.Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Math.Abs(e.T - t) <= 0.5 && Enum.IsDefined((SlotId)e.What))
            .Select(e => SlotName((SlotId)e.What)).ToList();
        if (casts.Count > 0) lines.Add(($"스킬: {string.Join(", ", casts)}", TipText));

        var texts = lines.Select(l => Format(l.Text, 11, l.Brush, false, 400)).ToList();
        double w = texts.Max(f => f.WidthIncludingTrailingWhitespace) + 16, h = texts.Count * 16 + 10;
        double x = hx + 12 + w > ActualWidth ? hx - 12 - w : hx + 12;
        dc.DrawRoundedRectangle(TipBack, null, new Rect(x, top + 4, w, h), 4, 4);
        for (int i = 0; i < texts.Count; i++) dc.DrawText(texts[i], new(x + 8, top + 9 + i * 16));
    }

    void DrawAxis(DrawingContext dc, double y, Brush fg)
    {
        double fit = _window.Span / Math.Max(1, PlotW / 70);   // never more labels than fit, whatever the record says
        double step = new double[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200 }.FirstOrDefault(s => s >= fit, Math.Ceiling(fit));
        for (double t = Math.Ceiling(_window.From / step) * step; t <= _window.To; t += step)
            Text(dc, Clock(t), new(LabelW + _window.X(t, PlotW) - 12, y + 1), 10, fg, opacity: 0.7);
    }

    static Pen Faint(Brush fg, double opacity)
    {
        var b = fg.CloneCurrentValue();
        b.Opacity = opacity;
        return new Pen(b, 1);
    }

    static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    /// <summary>An amount in the game's units: 조 (10^12), 억 (10^8) or 만 (10^4), as the 전투 분석기 writes them.</summary>
    static string Amount(double v) => v >= 1e12 ? $"{v / 1e12:0.0}조" : v >= 1e8 ? $"{v / 1e8:0.0}억" : v >= 1e4 ? $"{v / 1e4:0.0}만" : $"{v:0}";

    static string SlotName(SlotId s) => s switch
    {
        SlotId.각성기 => "각성기",
        SlotId.초각스 => "초각스",
        >= SlotId.일반1 and <= SlotId.일반8 => $"스킬 {s - SlotId.일반1 + 1}",
        _ => $"아덴 {s - SlotId.아덴1 + 1}",
    };

    /// <summary>A slot's mark when I have no icon for it.</summary>
    static string SlotShort(SlotId s) => s switch
    {
        SlotId.각성기 => "각",
        SlotId.초각스 => "초",
        >= SlotId.일반1 and <= SlotId.일반8 => $"{s - SlotId.일반1 + 1}",
        _ => $"아{s - SlotId.아덴1 + 1}",
    };

    /// <summary>Draws one line of text; returns its width.</summary>
    double Text(DrawingContext dc, string text, Point at, double size, Brush fg, bool bold = false, double opacity = 1, double? width = null,
        bool right = false)
    {
        var ft = Format(text, size, fg, bold, width ?? (at.X < LabelW ? LabelW - 4 : 240));
        if (right) ft.TextAlignment = TextAlignment.Right;
        dc.PushOpacity(opacity);
        dc.DrawText(ft, at);
        dc.Pop();
        return ft.WidthIncludingTrailingWhitespace;
    }

    FormattedText Format(string text, double size, Brush fg, bool bold, double width) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Malgun Gothic"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, fg, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    void Move(TimeWindow w) { _window = w; InvalidateVisual(); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double at = _window.From + (e.GetPosition(this).X - LabelW) / PlotW * _window.Span;
        Move(_window.Zoom(e.Delta > 0 ? 0.8 : 1.25, Math.Clamp(at, _window.From, _window.To), _length));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); _drag = e.GetPosition(this); CaptureMouse(); e.Handled = true; }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var now = e.GetPosition(this);
        if (_drag is not { } from || e.LeftButton != MouseButtonState.Pressed)
        {
            _hover = now.X;
            InvalidateVisual();
            return;
        }
        _hover = null;
        Move(_window.Pan(-(now.X - from.X) / PlotW * _window.Span, _length));
        _drag = now;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { _drag = null; ReleaseMouseCapture(); }

    protected override void OnMouseLeave(MouseEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        double mid = (_window.From + _window.To) / 2;
        TimeWindow? next = e.Key switch
        {
            Key.Left => _window.Pan(-_window.Span / 5, _length),
            Key.Right => _window.Pan(_window.Span / 5, _length),
            Key.OemPlus or Key.Add => _window.Zoom(0.8, mid, _length),
            Key.OemMinus or Key.Subtract => _window.Zoom(1.25, mid, _length),
            _ => null,
        };
        if (next is null) return;
        Move(next);
        e.Handled = true;
    }
}
