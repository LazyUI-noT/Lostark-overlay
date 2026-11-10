using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록's timeline (spec § D Timeline view): the fight opened, one chart with each player's DPS (solid, left scale)
/// and 누적 피해 (dashed, right scale), mine gold and on top; fights picked for 비교 (the same raid) add my curves on the same
/// chart, lined up at their offsets, thinner and in their own colours (user, 2026-11-03). Below it one row of my skill casts in
/// time order (my skill icon where I have one, else its key; each from its cast, 1 s wide at the zoom between 16 px and the row's
/// height's half (25 px), in 3 lanes zoomed out and 2 fully zoomed, the ones under the line on top: Timeline.IconSize / IconLanes / IconsUnder); then a line per buff
/// (공증 red, 아덴 피증 yellow, 초각스 피증 blue) lit while it was up, with its share of the fight (and of the seconds shown when
/// zoomed). Phase markers dashed. Hovering shows each figure beside what it reads at that second; a click pins the line, which can
/// then be dragged (double click or Esc lets it go). Text and rows follow <see cref="FontScale"/>. Wheel or +/− zooms around the cursor, dragging or ←/→ scrubs. Draws
/// only while shown.</summary>
sealed class TimelineView : FrameworkElement
{
    const double CurveH = 150, Gap = 12;
    double _font = 1;
    double LabelW => 96 * _font;
    double RightW => 52 * _font;
    double HeadH => 20 * _font;
    double LegendH => 18 * _font;
    double BuffH => 16 * _font;
    double AxisH => 20 * _font;
    const string UnhittableLabel = "보스 타격 불가";
    static readonly Brush UnhittableBand = Frozen(new SolidColorBrush(Color.FromArgb(52, 0x5A, 0x8F, 0xE6))), UnhittableInk = Frozen(new SolidColorBrush(Color.FromRgb(0x7F, 0xB0, 0xF5)));
    static readonly Color BossBad = Color.FromRgb(0xE2, 0x4B, 0x4A);   // a heal or a shield on the boss: bad news for the player
    static readonly Brush BossBadInk = Frozen(new SolidColorBrush(BossBad)), BossShieldFill = Frozen(new SolidColorBrush(Color.FromArgb(90, BossBad.R, BossBad.G, BossBad.B)));
    static readonly Pen HealPen = Frozen(new Pen(BossBadInk, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }), ShieldPen = Frozen(new Pen(BossBadInk, 1.4));
    // A cast's icon: 1 s wide at the zoom (Timeline.IconSize), 16 px in 3 lanes zoomed out up to 25 px in 2 fully zoomed (user,
    // 2026-11-05: half the row's height, which caps the zoom); the row holds 3 lanes of 16.
    const double MinIcon = 16, MaxIcon = 25, IconGap = 2, LanesH = 3 * MinIcon + 2 * IconGap;
    static double SkillH => LanesH + 4;

    double PxPerSecond => PlotW / Math.Max(1e-9, _window.Span);
    double IconPx => Timeline.IconSize(PxPerSecond, MinIcon, MaxIcon);
    double NarrowestSpan => Timeline.NarrowestSpan(PlotW, MaxIcon);
    double? LineX => _pinned is { } pin ? LabelW + _window.X(pin, PlotW) : _hover;   // the hover line, pinned or under the mouse
    double StripH => _regions.Count > 0 ? 50 * _font : 0;   // the regions' names and figures, over the curves
    double BlockHeight => HeadH + LegendH + StripH + CurveH + 8 + SkillH + 4 + Lanes * BuffH;   // one record's natural height
    double _curveH = CurveH;   // its curves: taller when the view is taller than its records (the full page)
    double Block => HeadH + LegendH + StripH + _curveH + 8 + SkillH + 4 + Lanes * BuffH;
    double Natural => _shown.Count * (BlockHeight + Gap) + AxisH;
    static readonly Color[] Compared = [Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x4F, 0xD1, 0xC5), Color.FromRgb(0xB7, 0x94, 0xF4)];
    static readonly Color[] ImportedColours = [Color.FromRgb(0xE8, 0xC5, 0x47), Color.FromRgb(0xD4, 0xA3, 0x73), Color.FromRgb(0xF2, 0xE3, 0x94)];   // their own family
    IReadOnlyList<ComparedFight> _compared = [];
    IReadOnlyList<(double From, double To)> _unhittable = [];
    IReadOnlyList<BossHpBar> _bossHp = [];
    Func<long, double?> _hpPerLine = _ => null;
    IReadOnlyList<(double T, bool Enter)> _gimmicks = [];
    IReadOnlyList<(double From, double To)> _gimmickZones = [];
    static readonly Brush GimmickZone = Frozen(new SolidColorBrush(Color.FromArgb(40, 0xE2, 0x4B, 0x4A)));
    const double SamePhase = 1.5;   // s: a timer stop this close to 대난투's mark is its line, not another
    ChartParts _parts = ChartParts.All;
    readonly HashSet<string> _hiddenPlayers = [];
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
    SlotId? _ringSlot;
    static readonly Pen GoldRing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xA8, 0x00)), 2));

    static Pen Frozen(Pen pen) { pen.Freeze(); return pen; }

    /// <summary>The skill picked to average: its casts wear a gold ring, drawn over the others.</summary>
    public SlotId? RingSlot
    {
        get => _ringSlot;
        set { _ringSlot = value; InvalidateVisual(); }
    }
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

    IReadOnlyList<(FightRecord Record, double Offset, IReadOnlyDictionary<SlotId, SlotFace> Icons)> _shown = [];
    TimeWindow _window = TimeWindow.Whole(0);
    double _length;
    Point? _drag;
    double _pressedAt;
    double? _pinned;   // a clicked second, on the shown clock: the line stays there, and can be dragged
    bool _movingLine, _dragged;
    double _curveTop, _skillTop, _buffTop, _dpsTop, _dmgTop, _bossTop = 1;
    const double TagH = 17, TagGap = 2, TagSample = 14, FlipAfter = 0.6, GrabLine = 6, DragStart = 3;
    static readonly Pen PinnedPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))), 1.5));
    double? _hover;   // the cursor's x while over the view and not dragging: values at that second

    public TimelineView() => (Focusable, ClipToBounds, Cursor) = (true, true, Cursors.SizeWE);

    /// <summary>The chart's parts the page can switch off.</summary>
    [Flags]
    public enum ChartParts { None = 0, Dps = 1, Damage = 2, BossHp = 4, Unhittable = 8, All = Dps | Damage | BossHp | Unhittable }

    /// <summary>Whose a compared fight is: the opened character's, another of mine, or imported from another user.</summary>
    public enum CompareLook { Same, OtherCharacter, Imported }

    /// <summary>A fight on the opened one's chart: lined up at <paramref name="Offset"/>, drawn per <paramref name="Look"/>, named
    /// <paramref name="Label"/> in the legend and the hover.</summary>
    /// <summary>What a slot's casts draw: an icon (saved or downloaded), else, for a skill the fight knows, a chip with its name.</summary>
    public sealed record SlotFace(ImageSource? Image, RecordSkill? Skill);

    public sealed record ComparedFight(FightRecord Record, double Offset, CompareLook Look, string Label);

    /// <summary>The fight to draw, with the second that lines up at 0 and my skill icons, and the fights compared with it (their
    /// curves on its chart, each lined up at its own second); the whole length shows.</summary>
    public void Show((FightRecord Record, double Offset, IReadOnlyDictionary<SlotId, SlotFace> Icons) opened, IReadOnlyList<ComparedFight> compared, IReadOnlyList<FightRegion> regions,
        IReadOnlyList<(double From, double To)> unhittable, Func<long, double?> hpPerLine, IReadOnlyList<(double T, bool Enter)> gimmicks)
    {
        (_hpPerLine, _gimmicks, _gimmickZones) = (hpPerLine, gimmicks, Timeline.GimmickZones(opened.Record));
        _pinned = null;
        (_shown, _compared, _regions, _unhittable, _bossHp) = ([opened], compared, regions, unhittable, BossHpGraph.Of(opened.Record));
        _length = Math.Max(opened.Record.Duration - opened.Offset, compared.Count == 0 ? 0 : compared.Max(c => c.Record.Duration - c.Offset));
        _window = TimeWindow.Whole(_length);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>What the chart draws (the page's checkboxes).</summary>
    public ChartParts Parts
    {
        get => _parts;
        set { _parts = value; InvalidateVisual(); }
    }

    /// <summary>Whether a player's curves are drawn (the page's checkboxes); the choice stays from fight to fight.</summary>
    public bool Shows(string player) => !_hiddenPlayers.Contains(player);

    public void SetShown(string player, bool on)
    {
        if (on) _hiddenPlayers.Remove(player); else _hiddenPlayers.Add(player);
        InvalidateVisual();
    }

    /// <summary>버프 적용률 isn't off (MeterSettings.BuffShare): the buff lines show their %.</summary>
    public bool BuffShareShown { get; set; } = true;

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
        _hoverAt = null;
        foreach (var (r, offset, icons) in _shown) y = DrawRecord(dc, r, offset, icons, y, fg) + Gap;
        if (_shown.Count > 0) DrawAxis(dc, y, fg);
    }

    double DrawRecord(DrawingContext dc, FightRecord r, double offset, IReadOnlyDictionary<SlotId, SlotFace> icons, double y, Brush fg)
    {
        var seen = new TimeWindow(_window.From + offset, _window.To + offset);   // the seconds shown, on this record's clock
        bool zoomed = _window.From > 0 || _window.To < r.Duration - offset;
        double bottom = y + Block;
        // date · [logo] raid [difficulty] · length (user, 2026-11-10; RaidBadgeView)
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double x = Text(dc, $"{r.StartedAt:MM-dd HH:mm} · ", new(0, y + 2), 12, fg, bold: true, width: ActualWidth);
        x += RaidBadgeView.Draw(dc, new(x, y + 1), r.Gate, null, 11 * _font, HeadH - 2, dpi);
        x += Text(dc, $"{r.Gate ?? "알 수 없음"} ", new(x, y + 2), 12, fg, bold: true, width: Math.Max(1, ActualWidth - x));
        x += RaidBadgeView.Draw(dc, new(x, y + 1), null, r.Difficulty, 11 * _font, HeadH - 2, dpi);
        Text(dc, $" · {Clock(r.Duration)}", new(x, y + 2), 12, fg, bold: true, width: Math.Max(1, ActualWidth - x));
        y += HeadH;
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, bottom - y)));
        foreach (double p in r.Phases.Where(p => !_gimmicks.Any(m => Math.Abs(m.T - p) <= SamePhase))) { double px = X(p, offset); dc.DrawLine(PhasePen, new(px, y), new(px, bottom)); }
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
        (_curveTop, _dpsTop, _dmgTop) = (y, 0, 0);
        DrawGimmickZones(dc, offset, y, bottom);
        if (_parts.HasFlag(ChartParts.Unhittable)) DrawUnhittable(dc, offset, y, bottom);
        DrawCurves(dc, r, offset, seen, colours, y, fg);
        if (_parts.HasFlag(ChartParts.BossHp)) DrawBossHp(dc, offset, seen, y, fg);
        DrawGimmicks(dc, offset, y, bottom);
        y += _curveH + 8;
        _skillTop = y;
        DrawSkills(dc, r, offset, seen, icons, y, fg);
        y += SkillH + 4;
        _buffTop = y;
        DrawBuffs(dc, r, offset, seen, zoomed, y, fg);
        if ((_pinned is { } pin ? LabelW + _window.X(pin, PlotW) : _hover) is { } hx && hx >= LabelW && hx <= LabelW + PlotW) DrawHover(dc, r, offset, colours, hx, bottom - Block + HeadH, bottom);
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

    // 대난투: a see-through red zone down the block, under the curves.
    void DrawGimmickZones(DrawingContext dc, double offset, double y, double bottom)
    {
        foreach (var (from, to) in _gimmickZones)
        {
            double x0 = Math.Max(LabelW, X(from, offset)), x1 = Math.Min(LabelW + PlotW, X(to, offset));
            if (x1 > x0) dc.DrawRectangle(GimmickZone, null, new Rect(x0, y, x1 - x0, bottom - y));
        }
    }

    // 대난투's start and end: a phase's dashed line down the block, named at the top of the curves (user, 2026-11-05): a zone's
    // "대난투" centred over it, "입장" right of its start line and "종료" left of its end line a row below, all inside it; an older
    // fight's start without a zone as "대난투 입장".
    void DrawGimmicks(DrawingContext dc, double offset, double y, double bottom)
    {
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, bottom - y)));
        double size = 10 * _font, title = y + 14 * _font, row = y + 28 * _font;
        foreach (var (from, to) in _gimmickZones)
        {
            var name = Format("대난투", size, BossBadInk, false, 200);
            double mid = (Math.Max(LabelW, X(from, offset)) + Math.Min(LabelW + PlotW, X(to, offset))) / 2;
            dc.DrawText(name, new(mid - name.Width / 2, title));
        }
        foreach (var (t, enter) in _gimmicks)
        {
            double x = X(t, offset);
            dc.DrawLine(PhasePen, new(x, y), new(x, bottom));
            bool zoned = _gimmickZones.Any(z => z.From == t || z.To == t);
            var text = Format(!zoned ? "대난투 입장" : enter ? "입장" : "종료", size, BossBadInk, false, 200);
            dc.DrawText(text, !zoned ? new(x + 3, title) : enter ? new(x + 3, row) : new(x - 3 - text.Width, row));
        }
        dc.Pop();
    }

    // The boss's HP on its own scale, 100% at the top (or the tallest HP + shield): one line per bar over the curves, a heal in red,
    // a shield a red block stacked on the line until it is gone.
    void DrawBossHp(DrawingContext dc, double offset, TimeWindow seen, double y, Brush fg)
    {
        var pen = new Pen(Faint(fg, 0.85).Brush, 1.6);
        double top = _bossTop = Math.Max(1, _bossHp.SelectMany(b => b.Points).Select(p => p.Hp + p.Shield).DefaultIfEmpty(1).Max());
        Point At(double t, double share) => new(X(t, offset), y + _curveH - 4 - (_curveH - 8) * share / top);
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, _curveH)));
        foreach (var bar in _bossHp)
        {
            var near = bar.Points.Where(p => p.T >= seen.From - 1 && p.T <= seen.To + 1).ToList();
            for (int i = 0; i < near.Count; i++)
            {
                if (near[i].Shield <= 0) continue;
                int end = i;
                while (end + 1 < near.Count && near[end + 1].Shield > 0) end++;
                DrawShield(dc, near.GetRange(i, end - i + 1), At);
                i = end;
            }
            if (near.Count >= 2) Line(dc, near.Select(p => At(p.T, p.Hp)), pen);
            foreach (var heal in bar.Heals)
            {
                var rise = near.Where(p => p.T >= heal.From && p.T <= heal.To).ToList();
                if (rise.Count >= 2) Line(dc, rise.Select(p => At(p.T, p.Hp)), HealPen);
            }
        }
        dc.Pop();
    }

    static void DrawShield(DrawingContext dc, List<BossHpPoint> run, Func<double, double, Point> at)
    {
        var block = new StreamGeometry();
        using (var c = block.Open())
        {
            c.BeginFigure(at(run[0].T, run[0].Hp), true, true);
            c.PolyLineTo([.. run.Select(p => at(p.T, p.Hp + p.Shield))], false, false);
            c.PolyLineTo([.. Enumerable.Reverse(run).Select(p => at(p.T, p.Hp))], false, false);
        }
        block.Freeze();
        dc.DrawGeometry(BossShieldFill, null, block);
        if (run.Count >= 2) Line(dc, run.Select(p => at(p.T, p.Hp + p.Shield)), ShieldPen);
    }

    // Where nobody could hit the boss: a band down the chart, skills and buffs, under the curves (Timeline.BossUnhittable).
    void DrawUnhittable(DrawingContext dc, double offset, double y, double bottom)
    {
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, bottom - y)));
        foreach (var (from, to) in _unhittable)
        {
            double x0 = Math.Max(LabelW, X(from, offset)), x1 = Math.Min(LabelW + PlotW, X(to, offset));
            if (x1 <= x0) continue;
            dc.DrawRectangle(UnhittableBand, null, new Rect(x0, y, x1 - x0, bottom - y));
            var label = Format(UnhittableLabel, 10 * _font, UnhittableInk, false, 400);
            if (label.WidthIncludingTrailingWhitespace + 6 <= x1 - x0) dc.DrawText(label, new(x0 + 3, y + 2));
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
        bool dps = _parts.HasFlag(ChartParts.Dps), damage = _parts.HasFlag(ChartParts.Damage);
        var (dpsPeak, dmgPeak) = Timeline.Peaks(r with { Samples = [.. r.Samples.Where(s => Shows(s.Name))] }, seen);   // the scales fit the seconds shown, both of them, and the fights compared
        foreach (var (c, cOffset, _, _) in _compared)
        {
            var (cDps, cDmg) = Timeline.Peaks(c with { Samples = [.. c.Samples.Where(s => s.Name == c.Me)] }, new TimeWindow(_window.From + cOffset, _window.To + cOffset));
            (dpsPeak, dmgPeak) = (Math.Max(dpsPeak, cDps), Math.Max(dmgPeak, cDmg));
        }
        var (dpsStep, dpsTop) = GridScale.Values(dpsPeak);
        var (dmgStep, dmgTop) = GridScale.Values(dmgPeak);
        (_dpsTop, _dmgTop) = (dpsTop, dmgTop);
        if (dps) DrawScaleLines(dc, grid, fg, y, dpsStep, dpsTop, "/초", right: false);   // 초당 피해량 on the left
        if (damage) DrawScaleLines(dc, grid, fg, y, dmgStep, dmgTop, "", right: true);   // 피해량 on the right
        DrawTimeLines(dc, grid, y);
        Text(dc, string.Join("   ", new[] { dps ? "— 초당 피해량" : "", damage ? "┄ 피해량 (오른쪽)" : "", _parts.HasFlag(ChartParts.BossHp) ? "— 보스 HP (맨 위 100%, 빨강: 회복 · 실드)" : "" }.Where(t => t != "")),
            new(LabelW + 4, y + 2), 10, fg, opacity: 0.6, width: PlotW - 8);
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, _curveH)));
        bool comparing = _compared.Count > 0;   // the opened fight's other players step back, its own curve thickens
        void Curves(IEnumerable<RecordSample> samples, double at, TimeWindow w, Color colour, double dpsWidth, double dmgWidth, double opacity, DashStyle? dpsDash = null)
        {
            var near = samples.Where(s => s.T >= w.From - 1 && s.T <= w.To + 1).ToList();
            if (near.Count < 2) return;
            var brush = Frozen(new SolidColorBrush(colour) { Opacity = opacity });
            var dpsPen = new Pen(brush, dpsWidth) { DashStyle = dpsDash ?? DashStyles.Solid, DashCap = PenLineCap.Round };
            if (dps) Line(dc, near.Select(s => new Point(X(s.T, at), y + _curveH - 4 - (_curveH - 8) * s.Dps / dpsTop)), dpsPen);
            if (damage) Line(dc, near.Select(s => new Point(X(s.T, at), y + _curveH - 4 - (_curveH - 8) * s.Damage / dmgTop)), new Pen(brush, dmgWidth) { DashStyle = Dashed });
        }
        foreach (var g in r.Samples.GroupBy(s => s.Name).Where(g => g.Key != r.Me && Shows(g.Key)))
            Curves(g, offset, seen, colours.GetValueOrDefault(g.Key, Others[0]), 1.2, 1, comparing ? 0.35 : 1);
        for (int i = 0; i < _compared.Count; i++)
        {
            var (c, cOffset, look, _) = _compared[i];
            Curves(c.Samples.Where(s => s.Name == c.Me), cOffset, new TimeWindow(_window.From + cOffset, _window.To + cOffset), ColourOf(i),
                look == CompareLook.Same ? 1.4 : 1.8, 1.1, 0.9, DpsDash(look));   // a dashed or dotted line a little thicker, to stay readable
        }
        if (Shows(r.Me)) Curves(r.Samples.Where(s => s.Name == r.Me), offset, seen, colours.GetValueOrDefault(r.Me, Mine), comparing ? 2.8 : 2, comparing ? 2 : 1.6, 1);   // on top
        dc.Pop();
    }

    /// <summary>My casts in time order: the skill row's icons.</summary>
    static List<RecordEvent> MyCasts(FightRecord r) =>
        [.. r.Events.Where(e => e.Name == r.Me && e.Kind == RecordEventKind.사용 && Enum.IsDefined((SlotId)e.What)).OrderBy(e => e.T)];

    static void Line(DrawingContext dc, IEnumerable<Point> points, Pen pen)
    {
        var pts = points.ToList();
        var line = new StreamGeometry();
        using (var c = line.Open()) { c.BeginFigure(pts[0], false, false); c.PolyLineTo(pts.Skip(1).ToList(), true, false); }
        line.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    // My casts on one row in time order (user, 2026-11-05): each icon from its cast, 2 s wide at the zoom, over the ones before it;
    // the one under the line is drawn last, on top, so its skill can be seen.
    void DrawSkills(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, IReadOnlyDictionary<SlotId, SlotFace> icons, double y, Brush fg)
    {
        Text(dc, "내 스킬", new(0, y + 12), 11, fg, opacity: 0.8);
        // A [Test] fight's skills come from a class proposed from its icons, not yet confirmed in 편집 (plan, decided 3).
        if (r.MyClassName == RecordCharacter.TestClass && r.ProposedClass is { } proposed)
            Text(dc, $"추천 {proposed}", new(0, y + 28), 9, fg, opacity: 0.6, width: LabelW - 4);
        dc.DrawLine(Faint(fg, 0.12), new(LabelW, y + SkillH), new(LabelW + PlotW, y + SkillH));
        dc.PushClip(new RectangleGeometry(new(LabelW, y, PlotW, SkillH)));
        var chip = Faint(fg, 0.25).Brush;
        var casts = MyCasts(r);
        var lefts = casts.Select(e => X(e.T, offset)).ToList();
        double size = IconPx;
        var places = Timeline.IconLanes(lefts, size, IconGap, LanesH);
        var hovered = LineX is { } hx ? Timeline.IconsUnder(lefts, size, hx) : [];
        void Draw(int i)
        {
            var at = new Rect(lefts[i], y + 2 + places[i].Top, size, size);
            var slot = (SlotId)casts[i].What;
            Face(at, slot);
            if (slot == _ringSlot) dc.DrawRoundedRectangle(null, GoldRing, at, 3, 3);
        }
        void Face(Rect at, SlotId slot)
        {
            var face = _useIcons ? icons.GetValueOrDefault(slot) : null;
            if (face?.Image is { } img) { dc.DrawImage(img, at); return; }
            if (face?.Skill is { } skill)   // a name chip, coloured by kind (user, 2026-11-05)
            {
                dc.DrawRoundedRectangle(KindChip(skill.Kind), null, at, 3, 3);
                Text(dc, SkillLibrary.Short(skill.Name), new(at.X + 1, at.Y + 1), 9, Brushes.White, bold: true, width: Math.Max(1, size - 2));
                return;
            }
            dc.DrawRoundedRectangle(chip, null, at, 3, 3);
            Text(dc, SlotKey(slot), new(at.X + 1, at.Y + 1), 9, fg, bold: true, width: Math.Max(1, size - 2));
        }
        var shown = Enumerable.Range(0, casts.Count).Where(i => !hovered.Contains(i) && casts[i].T >= seen.From - Timeline.IconSeconds - 1 && casts[i].T <= seen.To + 1);
        foreach (int i in shown.OrderBy(i => casts[i].What == (int?)_ringSlot)) Draw(i);   // stable: time order, the ringed ones on top
        foreach (int i in hovered) Draw(i);
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
            Text(dc, BuffShareShown ? $"{name} {whole * 100:0}%" + (zoomed ? $" ({Timeline.BuffShare(r, kind, seen) * 100:0}%)" : "") : name, new(0, y), 11, fg);
            // A 4-man raid: the party's share (the boss HP lost under it) at the line's right end, for the window shown.
            if (BuffShareShown && SmallParty && Timeline.PartyBuffShare(r, kind, zoomed ? seen : new TimeWindow(0, r.Duration)) is { } party)
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

    // The 암수 line: lit while a bomb was on the boss (암수 and 빛암 in their own shades); the label has the share of the time a
    // bomb was up and, in brackets, what the bombs added as a share of my damage (the top bar's 암수 이득 %), for the part shown.
    void DrawBombs(DrawingContext dc, FightRecord r, double offset, TimeWindow seen, bool zoomed, double y, Brush fg, Pen track)
    {
        var spans = Timeline.AmsuSpans(r);
        double cy = y + BuffH / 2;
        double Gain(TimeWindow w) => Timeline.BombShare(r, w) * Timeline.AmsuShare;
        var shown = zoomed && seen.From < r.Duration ? new TimeWindow(seen.From, Math.Min(seen.To, r.Duration)) : new TimeWindow(0, r.Duration);   // this fight's part
        Text(dc, $"암수 {Timeline.AmsuUptime(r, shown) * 100:0}% ({Gain(shown) * 100:0.0}%)", new(0, y), 11, fg);
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

    enum TagLine { None, Solid, Dashed }

    /// <summary>A figure beside the line (user, 2026-11-04): its text, its ink and the style of the curve it reads.</summary>
    readonly record struct LineTag(double Y, string Text, Brush Ink, TagLine Line = TagLine.None, double? Dot = null);

    // The figures at the second under the line, each beside what it reads: a curve's value on that curve, a buff on its row, in its colour.
    void DrawHover(DrawingContext dc, FightRecord r, double offset, Dictionary<string, Color> colours, double hx, double top, double bottom)
    {
        double t = _window.From + (hx - LabelW) / PlotW * _window.Span + offset;
        dc.DrawLine(_pinned is null ? CrossPen : PinnedPen, new(hx, top), new(hx, bottom));
        bool flip = hx > LabelW + PlotW * FlipAfter;
        _hoverAt ??= (hx, t);   // its time goes on the axis (DrawAxis), where the times are (user, 2026-11-05)
        var tags = new List<LineTag>();
        double CurveY(double share) => _curveTop + _curveH - 4 - (_curveH - 8) * share;
        void Figures(string who, RecordSample s, Brush ink)
        {
            if (_parts.HasFlag(ChartParts.Damage) && _dmgTop > 0) { double at = CurveY(s.Damage / _dmgTop); tags.Add(new(at, $"{who}피해량 {BossHpGraph.AmountText(s.Damage)}", ink, TagLine.Dashed, at)); }
            if (_parts.HasFlag(ChartParts.Dps) && _dpsTop > 0) { double at = CurveY(s.Dps / _dpsTop); tags.Add(new(at, $"{who}초당 {BossHpGraph.AmountText(s.Dps)}", ink, TagLine.Solid, at)); }
        }
        foreach (var g in r.Samples.GroupBy(s => s.Name).Where(g => Shows(g.Key)).OrderBy(g => g.Key != r.Me))
            if (g.LastOrDefault(s => s.T <= t) is { } s)
                Figures(g.Key == r.Me ? "" : $"{g.Key} ", s, Frozen(new SolidColorBrush(colours.GetValueOrDefault(g.Key, Others[0]))));
        for (int i = 0; i < _compared.Count; i++)   // the fights compared, at the same second on their own clocks
        {
            var (c, cOffset, _, label) = _compared[i];
            if (c.Samples.LastOrDefault(s => s.Name == c.Me && s.T <= t - offset + cOffset) is { } s)
                Figures($"{label} ", s, Frozen(new SolidColorBrush(ColourOf(i))));
        }
        if (_parts.HasFlag(ChartParts.BossHp) && _bossHp.SelectMany(b => b.Points.Select(p => (b.Max, Point: p))).LastOrDefault(x => x.Point.T <= t) is { Point: { } hp, Max: var max })
        {
            long left = (long)(hp.Hp * max);
            string count = _hpPerLine(max) is { } size ? $" · {BossHpGraph.LinesLeft(left, size)}줄" : "";
            double at = CurveY(hp.Hp / _bossTop);
            tags.Add(new(at, $"보스 HP {BossHpGraph.AmountText(left)}{count} ({hp.Hp * 100:0.0}%)", TipText, TagLine.Solid, at));
            if (hp.Shield > 0) tags.Add(new(at, $"보스 보호막 +{hp.Shield * 100:0.0}%", BossBadInk));
            foreach (var heal in _bossHp.SelectMany(b => b.Heals).Where(h => h.From <= t && t <= h.To))
                tags.Add(new(at, $"보스 회복 +{heal.Gain * 100:0.0}%", BossBadInk));
        }
        if (_parts.HasFlag(ChartParts.Unhittable) && _unhittable.Any(q => q.From <= t && t <= q.To)) tags.Add(new(_curveTop, UnhittableLabel, UnhittableInk));
        if (_gimmickZones.Any(z => z.From <= t && t <= z.To)) tags.Add(new(_curveTop, "대난투 중", BossBadInk));

        double height = TagH * _font;
        var ys = Timeline.SpreadTags([.. tags.Select(x => x.Y)], height + TagGap, _curveTop + height / 2, _skillTop + SkillH - height / 2);
        for (int i = 0; i < tags.Count; i++)
        {
            if (tags[i].Dot is { } dot) dc.DrawEllipse(tags[i].Ink, null, new(hx, dot), 3.5, 3.5);
            DrawTag(dc, hx, ys[i], tags[i], flip);
        }
        DrawRowTags(dc, r, t, hx, flip);
        DrawRegionBox(dc, colours, t, hx, top, flip);
    }

    // On their own rows, in their own colours: my casts at this second, each buff up and the bomb.
    void DrawRowTags(DrawingContext dc, FightRecord r, double t, double hx, bool flip)
    {
        // The skills of the icons under the line (drawn on top), oldest first.
        var casts = MyCasts(r);
        double offset = _shown.FirstOrDefault(x => ReferenceEquals(x.Record, r)).Offset;
        var under = Timeline.IconsUnder([.. casts.Select(e => X(e.T, offset))], IconPx, hx);
        var faces = _shown.FirstOrDefault(x => ReferenceEquals(x.Record, r)).Icons;
        string Named(SlotId s) => faces?.GetValueOrDefault(s)?.Skill?.Name ?? r.Skills?.GetValueOrDefault(s)?.Name ?? SlotName(s);
        if (under.Count > 0) DrawTag(dc, hx, _skillTop + SkillH / 2, new(0, string.Join(", ", under.Select(i => Named((SlotId)casts[i].What))), TipText), flip);
        var spans = Timeline.BuffSpans(r);
        for (int i = 0; i < Buffs.Length; i++)
        {
            var (kind, name, color) = Buffs[i];
            if (spans.Any(b => b.Kind == kind && b.From <= t && t < b.To))
                DrawTag(dc, hx, _buffTop + i * BuffH + BuffH / 2, new(0, name, Frozen(new SolidColorBrush(color))), flip);
        }
        if (r.AmsuOn && Timeline.AmsuSpans(r).FirstOrDefault(s => s.From <= t && t < s.To) is { To: > 0 } bomb)
            DrawTag(dc, hx, _buffTop + Buffs.Length * BuffH + BuffH / 2, new(0, bomb.Kind.ToString(), Bombs.First(b => b.Kind == bomb.Kind).Lit.Brush), flip);
    }

    void DrawTag(DrawingContext dc, double hx, double y, LineTag tag, bool flip)
    {
        var text = Format(tag.Text, 11 * _font, TipText, false, 400 * _font);
        double sample = tag.Line == TagLine.None ? 0 : TagSample + 4, width = text.WidthIncludingTrailingWhitespace + sample + 10, height = TagH * _font;
        double x = flip ? hx - 8 - width : hx + 8;
        dc.DrawRoundedRectangle(TipBack, new Pen(tag.Ink, 1), new Rect(x, y - height / 2, width, height), 4, 4);
        if (tag.Line != TagLine.None)
            dc.DrawLine(new Pen(tag.Ink, 2) { DashStyle = tag.Line == TagLine.Dashed ? Dashed : DashStyles.Solid }, new(x + 5, y), new(x + 5 + TagSample, y));
        dc.DrawText(text, new(x + 5 + sample, y - text.Height / 2));
    }

    // Everyone's figures in the region under the line, on the side the tags leave free.
    void DrawRegionBox(DrawingContext dc, Dictionary<string, Color> colours, double t, double hx, double top, bool flip)
    {
        if (_regions.LastOrDefault(x => x.From <= t) is not { } region) return;
        var lines = new List<(string Text, Brush Brush)> { ($"{region.Name} 구간 ({Clock(region.To - region.From)})", TipText) };
        foreach (var p in region.Players.Where(p => Shows(p.Name)))
            lines.Add(($"{p.Name}  구간 피해량 {BossHpGraph.AmountText((long)p.Damage)} · DPS {BossHpGraph.AmountText((long)p.Dps)}", Frozen(new SolidColorBrush(colours.GetValueOrDefault(p.Name, Others[0])))));
        var texts = lines.Select(l => Format(l.Text, 11 * _font, l.Brush, false, 400 * _font)).ToList();
        double lineH = 16 * _font, w = texts.Max(f => f.WidthIncludingTrailingWhitespace) + 16, h = texts.Count * lineH + 10;
        double x = flip ? hx + 8 : hx - 8 - w;
        if (x < LabelW) return;   // no room on that side
        dc.DrawRoundedRectangle(TipBack, null, new Rect(x, top + 4, w, h), 4, 4);
        for (int i = 0; i < texts.Count; i++) dc.DrawText(texts[i], new(x + 8, top + 9 + i * lineH));
    }

    (double X, double T)? _hoverAt;   // the line's place and second this render, for its time on the axis

    void DrawAxis(DrawingContext dc, double y, Brush fg)
    {
        double step = TimeStep();
        for (double t = Math.Ceiling(_window.From / step) * step; t <= _window.To; t += step)
            Text(dc, Clock(t), new(LabelW + _window.X(t, PlotW) - 14, y + 1), AxisFont, fg, opacity: 0.7);
        if (_hoverAt is not var (hx, at)) return;
        // The line's time on the axis, over the labels it passes.
        var text = Format(Clock(at), AxisFont * _font, TipText, false, 200);
        double w = text.WidthIncludingTrailingWhitespace + 10, h = text.Height + 2;
        dc.DrawRoundedRectangle(TipBack, new Pen(TipText, 1), new Rect(hx - w / 2, y, w, h), 4, 4);
        dc.DrawText(text, new(hx - w / 2 + 5, y + 1));
    }

    const double AxisFont = 11.5;   // the time labels (user, 2026-11-05: a little bigger than the 10 before)

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
    static readonly Brush ChipNormal = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x70, 0x78))),
        ChipTechnique = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x6F, 0xC9))), ChipAwakening = Frozen(new SolidColorBrush(Color.FromRgb(0xB8, 0x8A, 0x2E)));

    /// <summary>A name chip's fill: 일반 grey, 초각스 blue, 각성기 gold.</summary>
    static Brush KindChip(SlotGroup kind) => kind switch { SlotGroup.초각스 => ChipTechnique, SlotGroup.각성기 => ChipAwakening, _ => ChipNormal };

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

    void Move(TimeWindow w) { _window = w; InvalidateVisual(); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double at = _window.From + (e.GetPosition(this).X - LabelW) / PlotW * _window.Span;
        Move(_window.Zoom(e.Delta > 0 ? 0.8 : 1.25, Math.Clamp(at, _window.From, _window.To), _length, NarrowestSpan));
        e.Handled = true;
    }

    double SecondAt(double x) => _window.From + Math.Clamp((x - LabelW) / PlotW, 0, 1) * _window.Span;

    // A click pins the line, a double click lets it go; dragging the pinned line moves it, dragging anywhere else moves the chart.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        e.Handled = true;
        _movingLine = false;   // a press lost to another window never let go
        var at = e.GetPosition(this);
        if (e.ClickCount == 2) { _pinned = null; InvalidateVisual(); return; }
        CaptureMouse();
        if (_pinned is { } pin && Math.Abs(LabelW + _window.X(pin, PlotW) - at.X) <= GrabLine) { _movingLine = true; return; }
        (_drag, _dragged, _pressedAt) = (at, false, at.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var now = e.GetPosition(this);
        if (_movingLine && e.LeftButton == MouseButtonState.Pressed)
        {
            _pinned = SecondAt(now.X);
            InvalidateVisual();
            return;
        }
        if (_drag is not { } from || e.LeftButton != MouseButtonState.Pressed)
        {
            _hover = now.X;
            InvalidateVisual();
            return;
        }
        _hover = null;
        _dragged |= Math.Abs(now.X - _pressedAt) >= DragStart;
        if (!_dragged) return;
        Move(_window.Pan(-(now.X - from.X) / PlotW * _window.Span, _length));
        _drag = now;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag is not null && !_dragged) { _pinned = SecondAt(e.GetPosition(this).X); InvalidateVisual(); }
        (_drag, _movingLine) = (null, false);
        ReleaseMouseCapture();
    }

    protected override void OnMouseLeave(MouseEventArgs e) { _hover = null; InvalidateVisual(); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _pinned is not null) { _pinned = null; InvalidateVisual(); e.Handled = true; return; }
        double mid = (_window.From + _window.To) / 2;
        TimeWindow? next = e.Key switch
        {
            Key.Left => _window.Pan(-_window.Span / 5, _length),
            Key.Right => _window.Pan(_window.Span / 5, _length),
            Key.OemPlus or Key.Add => _window.Zoom(0.8, mid, _length, NarrowestSpan),
            Key.OemMinus or Key.Subtract => _window.Zoom(1.25, mid, _length, NarrowestSpan),
            _ => null,
        };
        if (next is null) return;
        Move(next);
        e.Handled = true;
    }
}
