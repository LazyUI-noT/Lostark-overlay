using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록's timeline (spec § D Timeline view): the fight opened, one chart with each player's DPS (solid, left scale)
/// and 누적 피해 (dashed, right scale), mine gold and on top; fights picked for 비교 (the same raid) add my curves on the same
/// chart, lined up at their offsets, thinner and in their own colours (user, 2026-11-03). Below it one row of my skill casts in
/// time order (my skill icon where I have one, else its key; larger as the view zooms in, up to twice); then a line per buff
/// (공증 red, 아덴 피증 yellow, 초각스 피증 blue) lit while it was up, with its share of the fight (and of the seconds shown when
/// zoomed). Phase markers dashed. Hovering shows each player's figures, my buffs and casts at that second (as the game's 전투
/// 분석기). Text and rows follow <see cref="FontScale"/>. Wheel or +/− zooms around the cursor, dragging or ←/→ scrubs. Draws
/// only while shown.</summary>
sealed class TimelineView : FrameworkElement
{
    const double CurveH = 150, Gap = 12, BaseIcon = 16;
    double _font = 1;
    double LabelW => 96 * _font;
    double RightW => 52 * _font;
    double HeadH => 20 * _font;
    double LegendH => 18 * _font;
    double BuffH => 16 * _font;
    double AxisH => 18 * _font;
    double Icon => BaseIcon * Timeline.IconScale(_length, _window.Span);
    double SkillH => 3 * (Icon + 2) + 4;
    double StripH => _regions.Count > 0 ? 50 * _font : 0;   // the regions' names and figures, over the curves
    double BlockHeight => HeadH + LegendH + StripH + CurveH + 8 + SkillH + 4 + Lanes * BuffH;   // one record's natural height
    double _curveH = CurveH;   // its curves: taller when the view is taller than its records (the full page)
    double Block => HeadH + LegendH + StripH + _curveH + 8 + SkillH + 4 + Lanes * BuffH;
    double Natural => _shown.Count * (BlockHeight + Gap) + AxisH;
    static readonly Color[] Compared = [Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x4F, 0xD1, 0xC5), Color.FromRgb(0xB7, 0x94, 0xF4)];
    static readonly Color[] ImportedColours = [Color.FromRgb(0xE8, 0xC5, 0x47), Color.FromRgb(0xD4, 0xA3, 0x73), Color.FromRgb(0xF2, 0xE3, 0x94)];   // their own family
    IReadOnlyList<ComparedFight> _compared = [];
    IReadOnlyList<FightRegion> _regions = [];   // the opened fight's (user, 2026-11-04): a compared fight's curves stay as they are

    /// <summary>A compared fight's colour: the imported ones in a family of their own.</summary>
    Color ColourOf(int i) => _compared[i].Look == CompareLook.Imported ? ImportedColours[i % ImportedColours.Length] : Compared[i % Compared.Length];

    /// <summary>A compared fight's DPS line (user, 2026-11-04): solid for the opened character's, dashed for my other characters',
    /// dotted for an imported one.</summary>
    static DashStyle? DpsDash(CompareLook look) => look switch
    {
        CompareLook.OtherCharacter => OtherDash,
        CompareLook.Imported => ImportedDash,
        _ => null,
    };
    static readonly DashStyle OtherDash = Frozen(new DashStyle([6, 3], 0)), ImportedDash = Frozen(new DashStyle([0.5, 2.5], 0));
    /// <summary>The buffs' lines, and the 암수 line when a fight shown had 암수 on (every record the same, so 비교 lines up).</summary>
    int Lanes => Buffs.Length + (_shown.Any(s => s.Record.AmsuOn) ? 1 : 0);
    static readonly (Bomb Kind, Pen Glow, Pen Lit)[] Bombs = [BombPens(Bomb.암수, Color.FromRgb(0x9B, 0x6B, 0xFF)), BombPens(Bomb.빛암, Color.FromRgb(0xD8, 0xA8, 0xFF))];

    static (Bomb, Pen, Pen) BombPens(Bomb kind, Color c) =>
        (kind, Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(70, c.R, c.G, c.B))), 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }),
         Frozen(new Pen(Frozen(new SolidColorBrush(c)), 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }));
    bool _useIcons = true;
    IReadOnlyDictionary<SlotId, string> _keys = new Dictionary<SlotId, string>();
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
    const double TimeGap = 70;   // px between the time labels (and so the time lines)

    IReadOnlyList<(FightRecord Record, double Offset, IReadOnlyDictionary<SlotId, ImageSource> Icons)> _shown = [];
    TimeWindow _window = TimeWindow.Whole(0);
    double _length;
    Point? _drag;
    double? _hover;   // the cursor's x while over the view and not dragging: values at that second

    public TimelineView() => (Focusable, ClipToBounds, Cursor) = (true, true, Cursors.SizeWE);

    /// <summary>Whose a compared fight is: the opened character's, another of mine, or imported from another user.</summary>
    public enum CompareLook { Same, OtherCharacter, Imported }

    /// <summary>A fight on the opened one's chart: lined up at <paramref name="Offset"/>, drawn per <paramref name="Look"/>, named
    /// <paramref name="Label"/> in the legend and the hover.</summary>
    public sealed record ComparedFight(FightRecord Record, double Offset, CompareLook Look, string Label);

    /// <summary>The fight to draw, with the second that lines up at 0 and my skill icons, and the fights compared with it (their
    /// curves on its chart, each lined up at its own second); the whole length shows.</summary>
    public void Show((FightRecord Record, double Offset, IReadOnlyDictionary<SlotId, ImageSource> Icons) opened, IReadOnlyList<ComparedFight> compared, IReadOnlyList<FightRegion> regions)
    {
        (_shown, _compared, _regions) = ([opened], compared, regions);
        _length = Math.Max(opened.Record.Duration - opened.Offset, compared.Count == 0 ? 0 : compared.Max(c => c.Record.Duration - c.Offset));
        _window = TimeWindow.Whole(_length);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>The opened fight is a 4-man raid: each buff line shows the party's share too (Timeline.PartyBuffShare).</summary>
    public bool SmallParty { get; set; }

    /// <summary>글자 크기: the text and the rows around it, 1 = as designed.</summary>
    public double FontScale
    {
        get => _font;
        set { if (value == _font) return; _font = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    /// <summary>My skill keys (Settings.Keys): the mark of a cast with no icon, and every mark when 키 is on.</summary>
    public IReadOnlyDictionary<SlotId, string> Keys
    {
        get => _keys;
        set { _keys = value; InvalidateVisual(); }
    }

    /// <summary>My lane shows my skill icons (아이콘) where there are any, else the keys (키).</summary>
    public bool UseIcons
    {
        get => _useIcons;
        set { _useIcons = value; InvalidateVisual(); }
    }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 600 : available.Width, Natural);

    double PlotW => Math.Max(1, ActualWidth - LabelW - RightW);
    double X(double t, double offset) => LabelW + _window.X(t - offset, PlotW);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));   // the whole area takes the wheel and drags
        _curveH = CurveH + Math.Max(0, ActualHeight - Natural) / Math.Max(1, _shown.Count);   // spare height goes to the curves
        var fg = TextElement.GetForeground(this) ?? Brushes.Gray;
        double y = 0;
        foreach (var (r, offset, icons) in _shown) y = DrawRecord(dc, r, offset, icons, y, fg) + Gap;
        if (_shown.Count > 0) DrawAxis(dc, y, fg);
    }

    double DrawRecord(DrawingContext dc, FightRecord r, double offset, IReadOnlyDictionary<SlotId, ImageSource> icons, double y, Brush fg)
    {
        var seen = new TimeWindow(_window.From + offset, _window.To + offset);   // the seconds shown, on this record's clock
        bool zoomed = _window.From > 0 || _window.To < r.Duration - offset;
        double bottom = y + Block;
        string level = string.IsNullOrEmpty(r.Difficulty) ? "" : $" [{r.Difficulty}]";
        Text(dc, $"{r.StartedAt:MM-dd HH:mm} · {r.Gate ?? "알 수 없음"}{level} · {Clock(r.Duration)}", new(0, y + 2), 12, fg, bold: true, width: ActualWidth);
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
            // Mine also has my share of the boss's damage over the fight (내 몫, as the list shows it).
            string share = name == r.Me && r.MyShare is { } s ? $" · 기여 {s * 100:0.0}%" : "";
            dc.DrawRectangle(Frozen(new SolidColorBrush(colours[name])), null, new(lx, y + 5, 8, 8));
            lx += 11 + Text(dc, $"{name} {Amount(dps)}{share}", new(lx + 11, y + 1), 11, fg, bold: name == r.Me) + 12;
        }
        for (int i = 0; i < _compared.Count; i++)   // the fights compared: who and when, my average DPS and share
        {
            var (c, cOffset, _, label) = _compared[i];
            var cSeen = new TimeWindow(_window.From + cOffset, _window.To + cOffset);
            double dps = Timeline.DpsAverages(c, cSeen).FirstOrDefault(p => p.Name == c.Me).Dps;
            string share = c.MyShare is { } s ? $" · 기여 {s * 100:0.0}%" : "";
            dc.DrawRectangle(Frozen(new SolidColorBrush(ColourOf(i))), null, new(lx, y + 5, 8, 8));
            lx += 11 + Text(dc, $"{label} {Amount(dps)}{share}", new(lx + 11, y + 1), 11, fg) + 12;
        }
        y += LegendH;
        DrawRegions(dc, r, offset, y, bottom, fg);
        y += StripH;
        DrawCurves(dc, r, offset, seen, colours, y, fg);
        y += _curveH + 8;
        DrawSkills(dc, r, offset, seen, icons, y, fg);
        y += SkillH + 4;
        DrawBuffs(dc, r, offset, seen, zoomed, y, fg);
        if (_hover is { } hx && hx >= LabelW && hx <= LabelW + PlotW) DrawHover(dc, r, offset, colours, hx, bottom - Block + HeadH, bottom);
        return bottom;
    }

    // A dashed line down the whole block at each region's start; above the curves its name, my damage and DPS in it, its length and the boss HP lost.
    void DrawRegions(DrawingContext dc, FightRecord r, double offset, double y, double bottom, Brush fg)
    {
        if (_regions.Count == 0) return;
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, bottom - y)));
        var (band, gold, faint) = (Faint(fg, 0.07).Brush, Frozen(new SolidColorBrush(Mine)), Faint(fg, 0.65).Brush);
        for (int i = 0; i < _regions.Count; i++)
        {
            var region = _regions[i];
            double x0 = Math.Max(LabelW, X(region.From, offset)), x1 = Math.Min(LabelW + PlotW, X(region.To, offset));
            if (i > 0) dc.DrawLine(PhasePen, new(X(region.From, offset), y), new(X(region.From, offset), bottom));
            if (x1 - x0 < 1) continue;
            if (i % 2 == 1) dc.DrawRectangle(band, null, new Rect(x0, y, x1 - x0, StripH - 4));
            void Centred(string text, double top, double size, Brush brush, bool bold = false)
            {
                var line = Format(text, size * _font, brush, bold, Math.Max(1, x1 - x0 - 6));
                (line.TextAlignment, line.MaxLineCount, line.Trimming) = (TextAlignment.Center, 1, TextTrimming.CharacterEllipsis);
                dc.DrawText(line, new(x0 + 3, y + top * _font));
            }
            Centred(region.Name, 1, 12, fg, bold: true);
            if (region.Players.FirstOrDefault(p => p.Name == r.Me) is { } mine)
            {
                string full = $"{Amount(mine.Damage)} · DPS {Amount(mine.Dps)}";   // a narrow region drops the word, not the number
                bool fits = Format(full, 11 * _font, gold, false, 2000).WidthIncludingTrailingWhitespace <= x1 - x0 - 6;
                Centred(fits ? full : $"{Amount(mine.Damage)} · {Amount(mine.Dps)}", 16, 11, gold);
            }
            Centred($"{Clock(region.To - region.From)} · {(region.BossTaken is { } boss ? $"보스 {Amount(boss)}" : "보스 HP ???")}", 31, 10, faint);
        }
        dc.Pop();
    }

    // DPS solid on the left scale, 누적 피해 dashed on the right; mine drawn last, on top.
    void DrawCurves(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, Dictionary<string, Color> colours, double y, Brush fg)
    {
        var grid = Faint(fg, 0.12);
        if (r.Samples.Count == 0)   // no 전분 figures this fight: no scale to show
        {
            dc.DrawLine(grid, new(LabelW, y + _curveH - 4), new(LabelW + PlotW, y + _curveH - 4));
            Text(dc, "전분 수치 없음 (피해량 · 초당 피해량이 기록되지 않았습니다)", new(LabelW + 8, y + _curveH / 2 - 8), 11, fg, opacity: 0.6, width: PlotW - 16);
            return;
        }
        var (dpsPeak, dmgPeak) = Timeline.Peaks(r, seen);   // the scales fit the seconds shown, both of them, and the fights compared
        foreach (var (c, cOffset, _, _) in _compared)
        {
            var (cDps, cDmg) = Timeline.Peaks(c with { Samples = [.. c.Samples.Where(s => s.Name == c.Me)] }, new TimeWindow(_window.From + cOffset, _window.To + cOffset));
            (dpsPeak, dmgPeak) = (Math.Max(dpsPeak, cDps), Math.Max(dmgPeak, cDmg));
        }
        var (dpsStep, dpsTop) = GridScale.Values(dpsPeak);
        var (dmgStep, dmgTop) = GridScale.Values(dmgPeak);
        DrawScaleLines(dc, grid, fg, y, dpsStep, dpsTop, "/초", right: false);   // 초당 피해량 on the left
        DrawScaleLines(dc, grid, fg, y, dmgStep, dmgTop, "", right: true);   // 피해량 on the right
        DrawTimeLines(dc, grid, y);
        Text(dc, "— 초당 피해량   ┄ 피해량 (오른쪽)", new(LabelW + 4, y + 2), 10, fg, opacity: 0.6);
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, _curveH)));
        bool comparing = _compared.Count > 0;   // the opened fight's other players step back, its own curve thickens
        void Curves(IEnumerable<RecordSample> samples, double at, TimeWindow w, Color colour, double dpsWidth, double dmgWidth, double opacity, DashStyle? dpsDash = null)
        {
            var near = samples.Where(s => s.T >= w.From - 1 && s.T <= w.To + 1).ToList();
            if (near.Count < 2) return;
            var brush = Frozen(new SolidColorBrush(colour) { Opacity = opacity });
            var dpsPen = new Pen(brush, dpsWidth) { DashStyle = dpsDash ?? DashStyles.Solid, DashCap = PenLineCap.Round };
            Line(dc, near.Select(s => new Point(X(s.T, at), y + _curveH - 4 - (_curveH - 8) * s.Dps / dpsTop)), dpsPen);
            Line(dc, near.Select(s => new Point(X(s.T, at), y + _curveH - 4 - (_curveH - 8) * s.Damage / dmgTop)), new Pen(brush, dmgWidth) { DashStyle = Dashed });
        }
        foreach (var g in r.Samples.GroupBy(s => s.Name).Where(g => g.Key != r.Me))
            Curves(g, offset, seen, colours.GetValueOrDefault(g.Key, Others[0]), 1.2, 1, comparing ? 0.35 : 1);
        for (int i = 0; i < _compared.Count; i++)
        {
            var (c, cOffset, look, _) = _compared[i];
            Curves(c.Samples.Where(s => s.Name == c.Me), cOffset, new TimeWindow(_window.From + cOffset, _window.To + cOffset), ColourOf(i),
                look == CompareLook.Same ? 1.4 : 1.8, 1.1, 0.9, DpsDash(look));   // a dashed or dotted line a little thicker, to stay readable
        }
        Curves(r.Samples.Where(s => s.Name == r.Me), offset, seen, colours.GetValueOrDefault(r.Me, Mine), comparing ? 2.8 : 2, comparing ? 2 : 1.6, 1);   // on top
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
            if (_useIcons && icons.TryGetValue(slot, out var img)) { dc.DrawImage(img, at); continue; }
            dc.DrawRoundedRectangle(chip, null, at, 3, 3);
            Text(dc, SlotKey(slot), new(at.X + 1, at.Y + 1), 9, fg, bold: true, width: Icon - 2);
        }
        dc.Pop();
    }

    // A line per buff, lit (with a soft glow) while it was up; the label has the share of my damage dealt under it.
    void DrawBuffs(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, bool zoomed, double y, Brush fg)
    {
        var spans = Timeline.BuffSpans(r);
        var track = new Pen(Faint(fg, 0.12).Brush, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var (kind, name, color) in Buffs)
        {
            double cy = y + BuffH / 2;
            double whole = Timeline.BuffShare(r, kind, new TimeWindow(0, r.Duration));
            Text(dc, $"{name} {whole * 100:0}%" + (zoomed ? $" ({Timeline.BuffShare(r, kind, seen) * 100:0}%)" : ""), new(0, y), 11, fg);
            // A 4-man raid: the party's share (the boss HP lost under it) at the line's right end, for the window shown.
            if (SmallParty && Timeline.PartyBuffShare(r, kind, zoomed ? seen : new TimeWindow(0, r.Duration)) is { } party)
                Text(dc, $"파티 {party * 100:0}%", new(LabelW + PlotW + 4, y), 10, fg, opacity: 0.8, width: RightW - 4);
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
        if (r.AmsuOn) DrawBombs(dc, r, offset, seen, zoomed, y, fg, track);
    }

    // The 암수 line: lit while a bomb was on the boss (암수 and 빛암 in their own shades); the label has what the bombs added
    // as a share of my damage (the top bar's 암수 이득 %).
    void DrawBombs(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, bool zoomed, double y, Brush fg, Pen track)
    {
        var spans = Timeline.AmsuSpans(r);
        double cy = y + BuffH / 2;
        double Gain(TimeWindow w) => Timeline.BombShare(r, w) * Timeline.AmsuShare;
        Text(dc, $"암수 {Gain(new TimeWindow(0, r.Duration)) * 100:0.0}%" + (zoomed ? $" ({Gain(seen) * 100:0.0}%)" : ""), new(0, y), 11, fg);
        dc.DrawLine(track, new(LabelW, cy), new(LabelW + PlotW, cy));
        foreach (var (from, to, kind) in spans)
        {
            double x0 = Math.Max(LabelW, X(from, offset)), x1 = Math.Min(LabelW + PlotW, X(to, offset));
            if (x1 <= x0) continue;
            var (_, glow, lit) = Bombs.First(b => b.Kind == kind);
            dc.DrawLine(glow, new(x0, cy), new(x1, cy));
            dc.DrawLine(lit, new(x0, cy), new(x1, cy));
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
        if (_regions.LastOrDefault(x => x.From <= t) is { } region)   // everyone's figures in the region under the cursor
        {
            lines.Add(($"{region.Name} 구간 ({Clock(region.To - region.From)})", TipText));
            foreach (var p in region.Players)
                lines.Add(($"{p.Name}  구간 피해량 {Amount(p.Damage)} · DPS {Amount(p.Dps)}", Frozen(new SolidColorBrush(colours.GetValueOrDefault(p.Name, Others[0])))));
        }
        for (int i = 0; i < _compared.Count; i++)   // the fights compared, at the same second on their own clocks
        {
            var (c, cOffset, _, label) = _compared[i];
            if (c.Samples.LastOrDefault(s => s.Name == c.Me && s.T <= t - offset + cOffset) is { } s)
                lines.Add(($"{label}  피해량 {s.Damage:N0} · 초당 {s.Dps:N0}", Frozen(new SolidColorBrush(ColourOf(i)))));
        }
        var on = Timeline.BuffSpans(r).Where(b => b.From <= t && t < b.To)
            .SelectMany(b => Buffs.Where(x => x.Kind == b.Kind).Select(x => x.Name)).Distinct().ToList();   // a kind without a line (뎀증) has no name here
        lines.Add(($"버프: {(on.Count == 0 ? "없음" : string.Join(", ", on))}", TipText));
        if (r.AmsuOn)
            lines.Add(($"암수: {(Timeline.AmsuSpans(r).FirstOrDefault(s => s.From <= t && t < s.To) is { To: > 0 } b ? b.Kind.ToString() : "없음")}", TipText));
        var casts = r.Events.Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Math.Abs(e.T - t) <= 0.5 && Enum.IsDefined((SlotId)e.What))
            .Select(e => SlotName((SlotId)e.What)).ToList();
        if (casts.Count > 0) lines.Add(($"스킬: {string.Join(", ", casts)}", TipText));

        var texts = lines.Select(l => Format(l.Text, 11 * _font, l.Brush, false, 400 * _font)).ToList();
        double lineH = 16 * _font, w = texts.Max(f => f.WidthIncludingTrailingWhitespace) + 16, h = texts.Count * lineH + 10;
        double x = hx + 12 + w > ActualWidth ? hx - 12 - w : hx + 12;
        dc.DrawRoundedRectangle(TipBack, null, new Rect(x, top + 4, w, h), 4, 4);
        for (int i = 0; i < texts.Count; i++) dc.DrawText(texts[i], new(x + 8, top + 9 + i * lineH));
    }

    void DrawAxis(DrawingContext dc, double y, Brush fg)
    {
        double step = TimeStep();
        for (double t = Math.Ceiling(_window.From / step) * step; t <= _window.To; t += step)
            Text(dc, Clock(t), new(LabelW + _window.X(t, PlotW) - 12, y + 1), 10, fg, opacity: 0.7);
    }

    /// <summary>The seconds between the time lines and labels: the finest round step that keeps them apart.</summary>
    double TimeStep() => GridScale.Seconds(_window.Span, PlotW, TimeGap);

    // A vertical line at each time label, through the curves of every record (the axis's times, on the shown clock).
    void DrawTimeLines(DrawingContext dc, Pen grid, double y)
    {
        double step = TimeStep();
        for (double t = Math.Ceiling(_window.From / step) * step; t <= _window.To; t += step)
        {
            double x = LabelW + _window.X(t, PlotW);
            dc.DrawLine(grid, new(x, y), new(x, y + _curveH));
        }
    }

    // Horizontal lines at each round step up to the scale's top, labelled on their side in the game's units (the baseline reads 0).
    void DrawScaleLines(DrawingContext dc, Pen grid, Brush fg, double y, double step, double top, string suffix, bool right)
    {
        int lines = (int)Math.Round(top / step);
        for (int k = 0; k <= lines; k++)
        {
            double v = k * step, gy = y + _curveH - 4 - (_curveH - 8) * v / top;
            dc.DrawLine(grid, new(LabelW, gy), new(LabelW + PlotW, gy));
            string label = k == 0 ? "0" : GridScale.Label(v) + suffix;
            if (right) Text(dc, label, new(LabelW + PlotW + 4, gy - 7), 10, fg, opacity: 0.6, width: RightW - 4);
            else Text(dc, label, new(0, gy - 7), 10, fg, opacity: 0.6, width: LabelW - 6, right: true);
        }
    }


    static Pen Faint(Brush fg, double opacity)
    {
        var b = fg.CloneCurrentValue();
        b.Opacity = opacity;
        return new Pen(b, 1);
    }

    static string Clock(double seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    /// <summary>An amount in the game's units: 조 (10^12), 억 (10^8) or 만 (10^4), as the 전투 분석기 writes them.</summary>
    internal static string Amount(double v) => v >= 1e12 ? $"{v / 1e12:0.0}조" : v >= 1e8 ? $"{v / 1e8:0.0}억" : v >= 1e4 ? $"{v / 1e4:0.0}만" : $"{v:0}";

    /// <summary>A cast's name in the hover box: the skill's key (Q, W…), as the lane shows it.</summary>
    string SlotName(SlotId s) => s switch
    {
        SlotId.각성기 => "각성기",
        SlotId.초각스 => "초각스",
        >= SlotId.일반1 and <= SlotId.일반8 => SlotKey(s),
        _ => $"아덴 {s - SlotId.아덴1 + 1}",
    };

    /// <summary>A slot's key as set in 키·단축키 (Settings.Keys): its mark when I have no icon for it, or always with 키.</summary>
    string SlotKey(SlotId s) => _keys.TryGetValue(s, out var key) ? key : SlotInfo.DefaultKey(s);

    /// <summary>Draws one line of text; returns its width.</summary>
    double Text(DrawingContext dc, string text, Point at, double size, Brush fg, bool bold = false, double opacity = 1, double? width = null,
        bool right = false)
    {
        var ft = Format(text, size * _font, fg, bold, width ?? (at.X < LabelW ? LabelW - 4 : 240 * _font));
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

    void Move(TimeWindow w) { _window = w; InvalidateMeasure(); InvalidateVisual(); }   // the icons' rows grow with the zoom

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
