using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 기능 Map's map (Phase 4, mockup v59): <see cref="FeatureMap.Boxes"/> in four columns, coloured by group, with
/// <see cref="FeatureMap.Edges"/> between them. A click anywhere on a box (not on its switches or ⚙) folds or opens it and
/// keeps its lines thick; a click on empty map lets go. Switches are the mockup's pills (green 켜기, blue 표시 안 함, grey
/// 끄기, dark 읽기; a gold border on a character's own part). Lights run along the lines of ✓ readers. 파티 and 파티원 창 stand in a row under
/// the columns; a party line is a short arrow to a small square at the far box and a square with a line at 파티, the long line
/// between them and all text only in focus (user, 2026-11-06). Hovering a box shows its preview (<see cref="Preview"/>).
/// The middle button drags the map: it moves with the pointer, through the sideways viewer it sits in and the page (<see cref="StartPan"/>).
/// Everything shown comes from Core; this only draws and wires.</summary>
internal sealed class FeatureMapView : Canvas
{
    static readonly double[] ColumnWidths = [220, 200, 220, 210];
    static readonly double[] ColumnGaps = [54, 68, 64];
    /// <summary>Room right of the last column: a same-column arrow (control points 40 px out) bulges about 30 px past it.</summary>
    const double SameColumnArc = 32;
    const double FoldedHeight = 34, Pad = 8, InnerIndent = 10, Spacing = 10;
    /// <summary>파티 stands taller, its lines apart, in a row of its own under the columns (user, 2026-11-06).</summary>
    const double PartyHeight = 62, PartyRowGap = 30;
    /// <summary>Between lines on a box's side; wider at 파티, where each is pointed at and clicked, and between lines going in and out.</summary>
    const double PortStep = 6, PartyPortStep = 15, PortGap = 14;
    /// <summary>The clickable line at 파티, the short arrow at the far box, and the square that ends each.</summary>
    const double NearLine = 110, StubLine = 20, PortSize = 10, ArrowSize = 5, LongBend = 70;
    /// <summary>One speed on every line, one light per <see cref="LightGap"/> px (user, 2026-11-05: half the lights, half the speed).</summary>
    const double LightSpeed = 125, LightGap = 110;
    /// <summary>A reader that can't read: dark orange lights back to the game settings it depends on, at 70% of the speed.</summary>
    const double FailSpeed = LightSpeed * 0.7;
    const string Segment = "map-seg";
    static readonly Color Green = Color.FromRgb(0x2F, 0x9E, 0x44), Blue = Color.FromRgb(0x3B, 0x5B, 0xDB), Grey = Color.FromRgb(0x86, 0x8E, 0x96),
        Dark = Color.FromRgb(0x49, 0x50, 0x57), FailLight = Color.FromRgb(0xD9, 0x48, 0x0F), Gold = Color.FromRgb(0xF0, 0xA8, 0x00), Cut = Color.FromRgb(0xE0, 0x31, 0x31),
        PortColour = Color.FromRgb(0x94, 0xD8, 0xE8);
    /// <summary>Boxes with a per-character 표시 (the gold mark on the dot).</summary>
    static readonly HashSet<string> PerCharacter = ["skillview", "aden", "judg", "buff", "meter"];
    /// <summary>The character's own switch parts (gold border): the rest are the profile's.</summary>
    static readonly HashSet<(string, int)> CharacterPart = [("skillview", 0), ("aden", 1), ("judg", 1), ("buff", 1)];
    /// <summary>The row under the columns, each box in its own column (user, 2026-11-06).</summary>
    static readonly string[] PartyRow = ["party", "peers"];

    readonly Settings _s;
    readonly Action _changed;
    readonly Action<string> _go, _note;
    /// <summary>리소스 검사 on: each box that has measured parts gets a CPU line, filled by <see cref="SetResources"/>.</summary>
    public bool ShowResources { get; init; }
    readonly Dictionary<string, TextBlock> _cpu = [];
    IReadOnlyDictionary<string, string> _cpuLabels = new Dictionary<string, string>();

    public void SetResources(IReadOnlyDictionary<string, string> labels)
    {
        _cpuLabels = labels;
        foreach (var (id, line) in _cpu) line.Text = Cpu(id);
    }

    string Cpu(string id) => "CPU " + _cpuLabels.GetValueOrDefault(id, _cpuLabels.Count == 0 ? "…" : "없음");

    /// <summary>A click on a box's 감지 중 chip: opens 화면·인식 → 감지 on that box.</summary>
    public Action<string>? ShowDetect { get; init; }
    /// <summary>A 게임 설정 box's folded text (해상도 · 비율, a size in %) and, while it is open, its settings; null for any other box.</summary>
    public Func<string, string?>? Value { get; init; }
    public Func<string, UIElement?>? Body { get; init; }
    /// <summary>A box's hover card (its window's GIF, the reader's capture); null for a box with neither.</summary>
    public Func<string, UIElement?>? Preview { get; init; }
    /// <summary>The 감지 중 chip's hover card: the reader's area only, no GIF, folded or open (user, 2026-11-10).</summary>
    public Func<string, UIElement?>? DetectPreview { get; init; }
    /// <summary>The box whose chip card is showing (Live on for it), null when it closes.</summary>
    public Action<string?>? DetectHover { get; init; }
    readonly Action<string?> _pinnedChanged;
    readonly HashSet<string> _open;
    readonly Dictionary<string, Rect> _at = [];
    readonly Dictionary<string, MapBox> _byId = FeatureMap.Boxes.ToDictionary(b => b.Id);
    readonly List<(MapEdge Edge, Path Line, TextBlock? Tag)> _lines = [];
    /// <summary>A party line's parts: <paramref name="Strokes"/> share <paramref name="Brush"/>, <paramref name="InFocus"/> show
    /// only in focus, <paramref name="Slash"/> marks a cut about to happen.</summary>
    sealed record PartyLine(MapEdge Edge, bool Cut, SolidColorBrush Brush, Shape[] Strokes, UIElement[] InFocus, Line? Slash, TextBlock? Tag, string? Text);
    readonly List<PartyLine> _party = [];
    Window? _owner;
    MapEdge? _hoverLine;
    bool _hoverCuts;   // on the part a click cuts, not the far box's short arrow
    readonly Popup _preview = new() { AllowsTransparency = true, Placement = PlacementMode.Right, HorizontalOffset = 8, IsHitTestVisible = false };
    /// <summary>Crossing the map opens nothing: a card only for a box the mouse rests on.</summary>
    readonly DispatcherTimer _previewWait = new() { Interval = TimeSpan.FromMilliseconds(350) };
    (string Id, UIElement Face, bool Detect)? _previewFor;
    string? _liveFor;
    /// <summary>Each line's height where it meets a box side, spread so lines sharing a side don't meet in one point.</summary>
    readonly Dictionary<(MapEdge, bool AtStart), double> _ports = [];
    string? _pinned, _hover, _pressed;
    IReadOnlyDictionary<string, Detection> _detect = DetectBoard.Empty;
    TimeSpan _now;
    string _drawnFrom = "";   // the marks the map was last built with

    /// <summary>감지 중 from the engine: the map is rebuilt only when a mark changed (the lights follow the marks).</summary>
    public void SetDetect(IReadOnlyDictionary<string, Detection> board, TimeSpan now)
    {
        (_detect, _now) = (board, now);
        if (IsLoaded && Signature() != _drawnFrom) Build();
    }

    string Signature() => string.Join(",", FeatureMap.Boxes.Select(b => $"{b.Id}:{_detect.GetValueOrDefault(b.Id)?.Mark}"));

    /// <param name="open">The boxes opened by hand, kept by 설정 across rebuilds.</param>
    /// <param name="changed">After a switch: 설정 saves and refills the page (the map with it).</param>
    public FeatureMapView(Settings s, HashSet<string> open, string? pinned, Action changed, Action<string> go, Action<string> note, Action<string?> pinnedChanged)
    {
        (_s, _open, _pinned, _changed, _go, _note, _pinnedChanged) = (s, open, pinned, changed, go, note, pinnedChanged);
        Background = Brushes.Transparent;   // empty map takes the click that lets go
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == this) Pin(null); };
        MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) StartPan(e); };
        MouseMove += (_, e) => Pan(e);
        MouseUp += (_, e) => { if (e.ChangedButton == MouseButton.Middle) StopPan(); };
        LostMouseCapture += (_, _) => StopPan();
        Loaded += (_, _) =>
        {
            Build();   // attached: theme styles resolve
            if (_owner is not null) _owner.Deactivated -= HideOnDeactivated;   // Loaded comes again on a re-parent
            _owner = Window.GetWindow(this);
            if (_owner is not null) _owner.Deactivated += HideOnDeactivated;
        };
        AddLogicalChild(_preview);   // the card takes 설정's theme
        Unloaded += (_, _) =>
        {
            HidePreview();
            StopPan();
            if (_owner is not null) _owner.Deactivated -= HideOnDeactivated;
        };
        _previewWait.Tick += (_, _) => ShowPreview();
    }

    void Build()
    {
        HidePreview();
        Children.Clear();
        _at.Clear();
        _lines.Clear();
        _party.Clear();
        _cpu.Clear();
        (_hover, _hoverLine, _hoverCuts) = (null, null, false);
        _drawnFrom = Signature();
        double x = 0, height = 0;
        var lefts = new double[ColumnWidths.Length];
        for (int col = 0; col < ColumnWidths.Length; col++)
        {
            double y = 0;
            foreach (var box in FeatureMap.Boxes.Where(b => b.Column == col && b.Inside is null && !PartyRow.Contains(b.Id)))
                y = Place(box, x, y, ColumnWidths[col]) + Spacing;
            height = Math.Max(height, y);
            lefts[col] = x;
            x += ColumnWidths[col] + (col < ColumnGaps.Length ? ColumnGaps[col] : 0);
        }
        Children.Add(Dashed(0, height + PartyRowGap / 2, lefts[^1] + ColumnWidths[^1]));
        double row = height + PartyRowGap, bottom = row;
        foreach (var box in PartyRow.Select(id => _byId[id]))
            bottom = Math.Max(bottom, Place(box, lefts[box.Column], row, ColumnWidths[box.Column]));
        Width = x + SameColumnArc;
        Height = bottom;
        Ports();
        foreach (var edge in FeatureMap.Edges)
            if (FeatureMap.UsesPorts(edge)) DrawPorted(edge);
            else DrawEdge(edge);
        Highlight();
    }

    /// <summary>Places <paramref name="box"/> and, while it is open, the boxes inside it; returns its bottom.</summary>
    double Place(MapBox box, double x, double y, double width)
    {
        var inner = FeatureMap.Boxes.Where(b => b.Inside == box.Id).ToList();
        bool open = _open.Contains(box.Id);
        var face = Face(box, open, inner.Count);
        face.Width = width;
        if (box.Id == "party") { face.MinHeight = PartyHeight; if (face.Child is FrameworkElement body) body.VerticalAlignment = VerticalAlignment.Center; }
        SetLeft(face, x);
        SetTop(face, y);
        Children.Add(face);
        face.Measure(new Size(width, double.PositiveInfinity));
        double bottom = y + face.DesiredSize.Height;
        if (open)
            foreach (var b in inner) bottom = Place(b, x + InnerIndent, bottom + 6, width - 2 * InnerIndent);
        if (open && inner.Count > 0) { bottom += Pad; face.Height = bottom - y; }
        _at[box.Id] = new Rect(x, y, width, bottom - y);
        Panel.SetZIndex(face, box.Inside is null ? 1 : 2);
        return bottom;
    }

    Border Face(MapBox box, bool open, int innerCount)
    {
        var state = FeatureMap.State(_s, box.Id);
        var head = new DockPanel { LastChildFill = true, Height = FoldedHeight - 8 };
        var fold = new TextBlock { Text = open ? "▾" : "▸", Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        DockPanel.SetDock(fold, Dock.Right);
        head.Children.Add(fold);
        if (open)
        {
            var gear = new Button { Content = "⚙", Padding = new(4, 0, 4, 0), Margin = new(4, 0, 0, 0), ToolTip = $"{FeatureMap.Page(box.Id)} 설정 열기", Tag = Segment };
            gear.Click += (_, e) => { e.Handled = true; _go(FeatureMap.Page(box.Id)); };
            DockPanel.SetDock(gear, Dock.Right);
            head.Children.Add(gear);
        }
        if (state is { } st)
        {
            bool gold = PerCharacter.Contains(box.Id);
            var dot = new Ellipse
            {
                Width = 10, Height = 10, Margin = new(0, 0, 8, 0), StrokeThickness = 2, Stroke = gold ? new SolidColorBrush(Gold) : null,
                Fill = new SolidColorBrush(st switch { BoxState.On => Green, BoxState.Hidden => Blue, _ => Grey }),
                ToolTip = (st switch { BoxState.On => "켜짐", BoxState.Hidden => "표시 안 함 (읽기는 함)", _ => "꺼짐" }) + (gold ? " · 금색 테: 캐릭터마다 따로" : ""),
            };
            DockPanel.SetDock(dot, Dock.Left);
            head.Children.Add(dot);
        }
        if (!open && innerCount > 0)
        {
            var more = new TextBlock { Text = $"+{innerCount}", Opacity = 0.6, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
        }
        UIElement? chip = null;
        if (_detect.GetValueOrDefault(box.Id) is { } seen)
        {
            chip = Chip(seen, box.Id, open);
            DockPanel.SetDock(chip, Dock.Right);
            head.Children.Add(chip);
        }
        head.Children.Add(new TextBlock { Text = box.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var body = new StackPanel();
        body.Children.Add(head);
        if (Value?.Invoke(box.Id) is { } value) body.Children.Add(new TextBlock { Text = value, Opacity = 0.75, FontSize = 12, Margin = new(14, -2, 0, 2) });
        if (ShowResources && ResourceCheck.PartRows.Values.Contains(box.Id))
            body.Children.Add(_cpu[box.Id] = new TextBlock { Text = Cpu(box.Id), FontSize = 12, Opacity = 0.8, Margin = new(14, 0, 0, 0), ToolTip = "CPU 한 스레드를 100%로 본 값 (전체 스레드로 나눈 값). 이 PC에서만 재고, 어디에도 보내거나 기록하지 않습니다." });
        if (box.Note is { } note) body.Children.Add(new TextBlock { Text = note, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new(0, -2, 0, 2) });
        if (open)
        {
            for (int part = 0; part < box.Choices.Length; part++) body.Children.Add(Pill(box, part));
            if (Body?.Invoke(box.Id) is { } settings) body.Children.Add(settings);
        }

        var face = new Border
        {
            Child = body, Padding = new(Pad, 4, Pad, 4), CornerRadius = new(6), BorderThickness = new(4, 1, 1, 1),   // the group's colour as a bar, as on the rail
            BorderBrush = new SolidColorBrush(ColourOf(box.Id)), Cursor = Cursors.Hand, MinHeight = FoldedHeight,
            Opacity = state == BoxState.Off ? 0.55 : 1,   // off: faded on the theme's own card, readable in light and dark themes
        };
        face.SetResourceReference(Border.BackgroundProperty, Themes.CardBrushKey);
        face.MouseLeftButtonDown += (_, e) => { if (!OnSegment(e)) _pressed = box.Id; };
        face.MouseLeftButtonUp += (_, e) =>
        {
            if (OnSegment(e) || _pressed != box.Id) return;   // a switch, ⚙, or a press that began elsewhere
            e.Handled = true;
            _pressed = null;
            if (!_open.Remove(box.Id)) _open.Add(box.Id);
            Pin(box.Id);
            Build();
        };
        face.MouseEnter += (_, _) =>
        {
            _hover = box.Id;
            Highlight();
            bool onChip = chip is { IsMouseOver: true };   // came straight onto the chip: its card, whichever enter comes first
            WaitForPreview(box.Id, onChip ? chip! : face, onChip);
        };
        face.MouseLeave += (_, _) => { if (_hover == box.Id) _hover = null; Highlight(); HidePreview(); };
        if (chip is not null)
        {
            chip.MouseEnter += (_, _) => WaitForPreview(box.Id, chip, true);
            chip.MouseLeave += (_, _) => { if (face.IsMouseOver) WaitForPreview(box.Id, face, false); else HidePreview(); };   // back on the box: its own card
        }
        return face;
    }

    /// <summary>One switch part as a square well: its choices side by side as bevelled keys, the chosen one lit by meaning. A part with no setting
    /// yet is drawn faint and does nothing (its ⚙ page sets it).</summary>
    UIElement Pill(MapBox box, int part)
    {
        int? selected = FeatureMap.Selected(_s, box.Id, part);
        bool own = CharacterPart.Contains((box.Id, part));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var pill = new Border
        {
            Child = row, Background = Well, BorderThickness = new(own ? 1.5 : 1), Margin = new(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = new SolidColorBrush(own ? Gold : Dark), ClipToBounds = true, Opacity = selected is null ? 0.45 : 1,
            ToolTip = selected is null ? "이 스위치는 여기서 켜고 끌 수 없습니다. ⚙에서 설정하세요." : own ? "이 캐릭터에만 적용됩니다." : null,
        };
        var choices = box.Choices[part];
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            bool on = selected == i;
            var seg = new Border
            {
                Tag = Segment, Padding = new(7, 1, 7, 1), Cursor = selected is null ? Cursors.Arrow : Cursors.Hand, BorderThickness = new(1),
                Background = on ? Lit(Meaning(choices[i])) : KeyFace, BorderBrush = on ? LitEdge : KeyEdge,
                Child = on ? new TextBlock { Text = choices[i], FontSize = 12, Foreground = Brushes.White } : new TextBlock { Text = choices[i], FontSize = 12 },
            };
            if (selected is not null && !on)
                seg.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    if (FeatureMap.Choose(_s, box.Id, part, index) is { } note) _note(note);
                    _changed();   // 설정 refills the page, this map with it
                };
            row.Children.Add(seg);
        }
        return pill;
    }

    static LinearGradientBrush Vertical(Color top, Color bottom)
    {
        var brush = new LinearGradientBrush(top, bottom, 90);
        brush.Freeze();
        return brush;
    }

    static Color Shade(Color c, double by) => Color.FromRgb((byte)Math.Clamp(c.R * by, 0, 255), (byte)Math.Clamp(c.G * by, 0, 255), (byte)Math.Clamp(c.B * by, 0, 255));

    static LinearGradientBrush Lit(Color c) => Vertical(Shade(c, 1.2), Shade(c, 0.8));

    // The well is sunk (dark at its top), the keys stand out of it: light along their top edge, dark along the bottom.
    static readonly Brush Well = Vertical(Color.FromArgb(0x70, 0, 0, 0), Color.FromArgb(0x28, 0, 0, 0));
    static readonly Brush KeyFace = Vertical(Color.FromArgb(0x24, 255, 255, 255), Color.FromArgb(0x06, 255, 255, 255));
    static readonly Brush KeyEdge = Vertical(Color.FromArgb(0x38, 255, 255, 255), Color.FromArgb(0x60, 0, 0, 0));
    static readonly Brush LitEdge = Vertical(Color.FromArgb(0x90, 255, 255, 255), Color.FromArgb(0x90, 0, 0, 0));

    /// <summary>감지 중: ✓ green read for certain, ? yellow nothing yet, ! red should have read; the mark goes with the colour.
    /// On a folded box the chip is part of the box: a click there opens the box (user, 2026-11-10), only an open box's chip
    /// leads to 감지.</summary>
    UIElement Chip(Detection d, string id, bool open)
    {
        var (mark, colour) = d.Mark switch
        {
            DetectMark.Concrete => ("✓", Green),
            DetectMark.Failed => ("!", Cut),
            _ => ("?", Color.FromRgb(0xE0, 0xA8, 0x00)),
        };
        var chip = new Border   // no tooltip: hovering shows the Live card (DetectPreview), which says the same
        {
            CornerRadius = new(4), Padding = new(4, 0, 4, 0), Margin = new(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(colour), Tag = open ? Segment : null,
            Child = new TextBlock { Text = $"감지 중 {mark}", FontSize = 11, Foreground = Brushes.White },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        if (open) chip.MouseLeftButtonUp += (_, e) => { ShowDetect?.Invoke(id); e.Handled = true; };
        return chip;
    }

    /// <summary>How long ago, for a mark's time (the engine's clock counts from the PC's start, so a clock time means nothing).</summary>
    internal static string Ago(TimeSpan d) => d < TimeSpan.FromSeconds(5) ? "방금" : d < TimeSpan.FromMinutes(1) ? $"{(int)d.TotalSeconds}초 전"
        : d < TimeSpan.FromHours(1) ? $"{(int)d.TotalMinutes}분 전" : $"{(int)d.TotalHours}시간 전";

    static Color Meaning(string choice) => choice switch { "켜기" => Green, "표시 안 함" => Blue, "끄기" => Grey, _ => Dark };

    /// <summary>An arrow between the boxes' facing sides (to the outer box while that is folded); grey unless both ends are
    /// on. Lights run along the lines of ✓ readers. Party lines here are 파티 → 파티원 창, which offers 끊기, and
    /// DPS 미터기 → 타임라인, only in focus; the rest go by ports (<see cref="DrawPorted"/>).</summary>
    void DrawEdge(MapEdge edge)
    {
        if (Shown(edge.From) is not { } from || Shown(edge.To) is not { } to || from == to) return;
        Rect a = _at[from], b = _at[to];
        double ya = _ports[(edge, true)], yb = _ports[(edge, false)];
        Point start, end, c1, c2;
        if (Math.Abs(a.Left - b.Left) < 1)   // same column: out to the right and back
            (start, end, c1, c2) = (new(a.Right, ya), new(b.Right, yb), new(a.Right + 40, ya), new(b.Right + 40, yb));
        else if (b.Left > a.Right)
        {
            double bend = Math.Max(30, (b.Left - a.Right) / 2);
            (start, end, c1, c2) = (new(a.Right, ya), new(b.Left, yb), new(a.Right + bend, ya), new(b.Left - bend, yb));
        }
        else
        {
            double bend = Math.Max(30, (a.Left - b.Right) / 2);
            (start, end, c1, c2) = (new(a.Left, ya), new(b.Right, yb), new(a.Left - bend, ya), new(b.Right + bend, yb));
        }
        var figure = new PathFigure { StartPoint = start };
        figure.Segments.Add(new BezierSegment(c1, c2, end, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        bool cut = FeatureMap.Cut(_s, edge), live = On(edge.From) && On(edge.To);
        var colour = edge.Party ? ColourOf("party") : live ? ColourOf(edge.From) : Grey;
        string? label = LabelOf(edge, cut);
        var line = new Path
        {
            Data = geometry, Stroke = new SolidColorBrush(colour), StrokeThickness = 1.5, IsHitTestVisible = false,
            StrokeDashArray = edge.Party ? [1.5, 2.5] : edge.Optional ? [5, 3] : null,
        };
        Children.Add(line);
        TextBlock? tag = null;
        if (label is not null)   // shown with the box's lines only (hover or pin): all at once they pile up
        {
            tag = new TextBlock { Text = label, FontSize = 11, IsHitTestVisible = false };
            tag.SetResourceReference(TextBlock.BackgroundProperty, Themes.BackgroundBrushKey);
            tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(tag, (start.X + end.X) / 2 - tag.DesiredSize.Width / 2);
            tag.Visibility = Visibility.Collapsed;   // after measuring: collapsed measures as 0
            SetTop(tag, (start.Y + end.Y) / 2 - 8);
            Panel.SetZIndex(tag, 3);
            Children.Add(tag);
        }
        if (edge.Party)
        {
            var brush = new SolidColorBrush(colour);
            line.Stroke = brush;
            bool always = FeatureMap.Cuttable(edge);   // beside 파티: stays, as the lines at 파티 do
            UIElement[] inFocus = always ? tag is null ? [] : [tag] : tag is null ? [line] : [line, tag];
            var party = new PartyLine(edge, cut, brush, [line], inFocus, always ? Slash(geometry) : null, tag, label);
            _party.Add(party);
            if (always) CutHandle(party, geometry);
            return;
        }
        _lines.Add((edge, line, tag));
        if (live && FeatureMap.Lit(edge, _detect)) Lights(geometry, Colors.White, colour, LightSpeed);   // a reader that works (감지 중 ✓)
        if (FeatureMap.FlowsBack(edge, _detect))
        {
            var back = new PathFigure { StartPoint = end };
            back.Segments.Add(new BezierSegment(c2, c1, start, true));
            var reversed = new PathGeometry([back]);
            reversed.Freeze();
            Lights(reversed, FailLight, FailLight, FailSpeed);
        }
    }

    /// <summary>Lights travelling the line at <paramref name="speed"/> px/s, one per <see cref="LightGap"/> px.</summary>
    void Lights(PathGeometry geometry, Color centre, Color colour, double speed)
    {
        double length = Length(geometry);
        int count = Math.Max(1, (int)Math.Round(length / LightGap));
        var duration = TimeSpan.FromSeconds(length / speed);
        var glow = new RadialGradientBrush(centre, Color.FromArgb(0, colour.R, colour.G, colour.B)) { GradientOrigin = new(0.5, 0.5) };
        glow.Freeze();
        for (int k = 0; k < count; k++)
        {
            var light = new Ellipse { Width = 8, Height = 8, Fill = glow, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
            SetLeft(light, -4);
            SetTop(light, -4);
            Panel.SetZIndex(light, 0);
            var begin = TimeSpan.FromSeconds(-duration.TotalSeconds * k / count);   // spread along the line from the start
            foreach (var (source, property) in new[] { (PathAnimationSource.X, TranslateTransform.XProperty), (PathAnimationSource.Y, TranslateTransform.YProperty) })
                light.RenderTransform.BeginAnimation(property, new DoubleAnimationUsingPath
                {
                    PathGeometry = geometry, Source = source, Duration = duration, BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever,
                });
            Children.Add(light);
        }
    }

    static double Length(PathGeometry geometry)
    {
        double length = 0;
        geometry.GetPointAtFractionLength(0, out var last, out _);
        for (int i = 1; i <= 40; i++)
        {
            geometry.GetPointAtFractionLength(i / 40.0, out var p, out _);
            length += (p - last).Length;
            last = p;
        }
        return length;
    }

    static string? LabelOf(MapEdge edge, bool cut) => edge.Label is { } l ? l + (cut ? edge.From == "party" ? " (못 받음)" : " (안 보냄)" : "") : null;

    /// <summary>A party line by ports: the far box's short arrow and square, always there; the square and the clickable line at 파티,
    /// always there; the long line between the squares and the text, only in focus (<see cref="FeatureMap.LinkShown"/>).</summary>
    void DrawPorted(MapEdge edge)
    {
        bool sends = edge.To == "party";
        string far = sends ? edge.From : edge.To;
        if (Shown(far) != far) return;
        double farY = _ports[(edge, sends)], partyY = _ports[(edge, !sends)], side = sends ? 1 : -1;
        var boxEnd = new Point(sends ? _at[far].Right : _at[far].Left, farY);
        var farPort = new Point(boxEnd.X + side * StubLine, farY);
        var partyEnd = new Point(_at["party"].Left, partyY);
        var partyPort = new Point(partyEnd.X - NearLine, partyY);
        bool cut = FeatureMap.Cut(_s, edge);
        var brush = new SolidColorBrush(ColourOf("party"));

        var near = Stroke(Straight(partyPort, partyEnd), brush);
        var stub = Stroke(Straight(boxEnd, farPort), brush);
        var nearHead = Head(sends ? partyEnd : partyPort, sends ? 1 : -1, brush);
        var stubHead = Head(sends ? farPort : boxEnd, 1, brush);
        var curve = new PathFigure { StartPoint = farPort };
        curve.Segments.Add(new BezierSegment(new(farPort.X + side * LongBend, farY), new(partyPort.X - LongBend, partyY), partyPort, true));
        var link = Stroke(new PathGeometry([curve]), brush);
        link.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(link, 3);   // over the boxes it crosses: it is there only to be followed

        string? text = LabelOf(edge, cut);
        var tag = new TextBlock { Text = text, FontSize = 11, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        tag.SetResourceReference(TextBlock.BackgroundProperty, Themes.BackgroundBrushKey);
        SetRight(tag, Width - partyPort.X + PortSize);
        SetTop(tag, partyY - 8);
        Panel.SetZIndex(tag, 3);

        foreach (var part in new UIElement[] { near, stub, nearHead, stubHead, link, tag, Port(farPort), Port(partyPort) }) Children.Add(part);
        var line = new PartyLine(edge, cut, brush, [near, stub, nearHead, stubHead, link], [link, tag], Slash(near.Data), tag, text);
        _party.Add(line);
        CutHandle(line, near.Data);
        var farHit = new Path { Data = stub.Data, Stroke = Brushes.Transparent, StrokeThickness = 12 };
        farHit.MouseEnter += (_, _) => HoverLine(edge, cuts: false);
        farHit.MouseLeave += (_, _) => HoverLine(null, cuts: false);
        Panel.SetZIndex(farHit, 4);
        Children.Add(farHit);
    }

    static Path Stroke(Geometry data, Brush brush) => new() { Data = data, Stroke = brush, StrokeThickness = 1.5, StrokeDashArray = [1.5, 2.5], IsHitTestVisible = false };

    static PathGeometry Straight(Point from, Point to)
    {
        var line = new PathFigure { StartPoint = from };
        line.Segments.Add(new LineSegment(to, true));
        return new PathGeometry([line]);
    }

    /// <summary>The open arrow head of the drawing, its tip at <paramref name="tip"/>, pointing along x.</summary>
    static Path Head(Point tip, double direction, Brush brush)
    {
        var head = new PathFigure { StartPoint = new(tip.X - direction * ArrowSize, tip.Y - ArrowSize) };
        head.Segments.Add(new LineSegment(tip, true));
        head.Segments.Add(new LineSegment(new(tip.X - direction * ArrowSize, tip.Y + ArrowSize), true));
        return new Path { Data = new PathGeometry([head]), Stroke = brush, StrokeThickness = 1.5, IsHitTestVisible = false };
    }

    /// <summary>The small square that ends a party line's visible part.</summary>
    static Rectangle Port(Point at)
    {
        var port = new Rectangle { Width = PortSize, Height = PortSize, Stroke = new SolidColorBrush(PortColour), StrokeThickness = 2.5, IsHitTestVisible = false };
        port.SetResourceReference(Shape.FillProperty, Themes.BackgroundBrushKey);
        SetLeft(port, at.X - PortSize / 2);
        SetTop(port, at.Y - PortSize / 2);
        Panel.SetZIndex(port, 2);
        return port;
    }

    /// <summary>The red slash across a line's middle, hidden until a click would cut it.</summary>
    Line Slash(Geometry geometry)
    {
        PathGeometry.CreateFromGeometry(geometry).GetPointAtFractionLength(0.5, out var mid, out _);
        var slash = new Line { X1 = mid.X - 6, Y1 = mid.Y + 7, X2 = mid.X + 6, Y2 = mid.Y - 7, Stroke = new SolidColorBrush(Cut), StrokeThickness = 3, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Panel.SetZIndex(slash, 4);
        Children.Add(slash);
        return slash;
    }

    /// <summary>--map-line: <paramref name="from"/>'s line to 파티 as if the mouse were on it.</summary>
    internal void DemoLine(string from) => HoverLine(FeatureMap.Edges.First(e => e.From == from && FeatureMap.Cuttable(e)), cuts: true);

    void HoverLine(MapEdge? edge, bool cuts)
    {
        (_hoverLine, _hoverCuts) = (edge, cuts);
        Highlight();
    }

    /// <summary>끊기 on a line at 파티: hovering shows what a click makes it; the click cuts or reconnects it.</summary>
    void CutHandle(PartyLine line, Geometry geometry)
    {
        var hit = new Path { Data = geometry, Stroke = Brushes.Transparent, StrokeThickness = 12, Cursor = Cursors.Hand };
        hit.MouseEnter += (_, _) => HoverLine(line.Edge, cuts: true);
        hit.MouseLeave += (_, _) => HoverLine(null, cuts: false);
        hit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            FeatureMap.SetCut(_s, line.Edge, !line.Cut);
            _changed();
        };
        Panel.SetZIndex(hit, 4);
        Children.Add(hit);
    }

    void WaitForPreview(string id, UIElement face, bool detect)
    {
        ClosePreview();   // box ↔ chip: the other card goes at once
        if ((detect ? DetectPreview : Preview) is null) return;
        _previewFor = (id, face, detect);
        _previewWait.Start();
    }

    void ShowPreview()
    {
        _previewWait.Stop();
        if (_previewFor is not var (id, face, detect) || (detect ? DetectPreview : Preview)?.Invoke(id) is not { } card) return;
        (_preview.Child, _preview.PlacementTarget, _preview.IsOpen) = (card, face, true);
        string? live = detect ? id : null;
        if (_liveFor != live) DetectHover?.Invoke(_liveFor = live);
    }

    /// <summary>The card is a window of its own: no MouseLeave comes when another app takes over.</summary>
    void HideOnDeactivated(object? sender, EventArgs e) => HidePreview();

    /// <summary>A middle-button drag in progress (<see cref="StartPan"/>): the pointer's place at the start, in the frame it is measured
    /// in, and where the sideways viewer (<paramref name="Across"/>) and the page (<paramref name="Page"/>) stood then.</summary>
    sealed record PanStart(Point At, IInputElement Frame, ScrollViewer Across, double Left, ScrollViewer? Page, double Top);
    PanStart? _pan;

    /// <summary>The scroll key down on the map grabs it: the map moves with the pointer, through the sideways viewer it sits in and the
    /// page's scroll. The mouse is captured until the key comes up or the capture is lost.</summary>
    void StartPan(MouseButtonEventArgs e)
    {
        if (Ancestor<ScrollViewer>(this) is not { } across) return;
        HidePreview();   // the capture stops the box's MouseLeave, which would close it
        var page = Ancestor<ScrollViewer>(across);
        IInputElement frame = page ?? across;   // the viewer the map scrolls in moves with the page: measure where the page doesn't move it
        _pan = new(e.GetPosition(frame), frame, across, across.HorizontalOffset, page, page?.VerticalOffset ?? 0);
        CaptureMouse();
        Mouse.OverrideCursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    void Pan(MouseEventArgs e)
    {
        if (_pan is not { } p) return;
        if (e.MiddleButton != MouseButtonState.Pressed)
        {
            StopPan();   // let go where the map didn't see it
            return;
        }
        var at = e.GetPosition(p.Frame);
        p.Across.ScrollToHorizontalOffset(p.Left - (at.X - p.At.X));   // the pointer's whole distance from the press: no drift
        p.Page?.ScrollToVerticalOffset(p.Top - (at.Y - p.At.Y));
    }

    void StopPan()
    {
        if (_pan is null) return;
        _pan = null;
        Mouse.OverrideCursor = null;
        ReleaseMouseCapture();
    }

    static T? Ancestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (var at = VisualTreeHelper.GetParent(from); at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is T found) return found;
        return null;
    }

    void HidePreview()
    {
        ClosePreview();
        if (_liveFor is not null) DetectHover?.Invoke(_liveFor = null);
    }

    /// <summary>Closes the card but leaves Live running: box ↔ chip doesn't stop and restart the engine's capture.</summary>
    void ClosePreview()
    {
        _previewWait.Stop();
        _previewFor = null;
        (_preview.IsOpen, _preview.Child) = (false, null);
    }

    /// <summary>Where each line leaves and enters: the lines on one side of a box spread over its folded height, in the order of
    /// the boxes they lead to, so they don't cross at the box. Party lines stand apart from the rest, those going in apart from
    /// those going out, so one can be pointed at and cut (user, 2026-11-05).</summary>
    void Ports()
    {
        _ports.Clear();
        var ends = new List<(string Box, bool Right, double Other, MapEdge Edge, bool AtStart)>();
        foreach (var edge in FeatureMap.Edges)
        {
            if (FeatureMap.UsesPorts(edge))   // leaves the far box towards its square; every one meets 파티's left side
            {
                bool sends = edge.To == "party";
                string far = sends ? edge.From : edge.To;
                if (Shown(far) != far) continue;
                ends.Add((far, sends, _at["party"].Top, edge, sends));
                ends.Add(("party", false, _at[far].Top, edge, !sends));
                continue;
            }
            if (Shown(edge.From) is not { } from || Shown(edge.To) is not { } to || from == to) continue;
            Rect a = _at[from], b = _at[to];
            bool same = Math.Abs(a.Left - b.Left) < 1, forward = b.Left > a.Right;
            ends.Add((from, same || forward, b.Top, edge, true));
            ends.Add((to, same || !forward, a.Top, edge, false));
        }
        foreach (var side in ends.GroupBy(e => (e.Box, e.Right)))
        {
            bool isParty = side.Key.Box == "party";   // its lines out on top, towards the meter's in: the two don't cross
            int Group((string Box, bool Right, double Other, MapEdge Edge, bool AtStart) e) => !e.Edge.Party ? 2 : e.AtStart == isParty ? 0 : 1;
            var lines = side.OrderBy(Group).ThenBy(e => e.Other).ToList();
            double band = isParty ? PartyHeight : FoldedHeight, mid = _at[side.Key.Box].Top + band / 2;
            var at = FeatureMap.SpreadPorts([.. lines.Select(Group)], lines.All(e => e.Edge.Party) ? PartyPortStep : PortStep, PortGap, band - 10);
            for (int i = 0; i < lines.Count; i++) _ports[(lines[i].Edge, lines[i].AtStart)] = mid + at[i];
        }
    }

    Color ColourOf(string id) => (Color)ColorConverter.ConvertFromString(_byId[id].Color);

    /// <summary>The box drawn for <paramref name="id"/>: itself, or the outer box it is folded into.</summary>
    string? Shown(string id)
    {
        for (string? at = id; at is not null; at = _byId[at].Inside)
            if (_at.ContainsKey(at)) return at;
        return null;
    }

    bool On(string id) => FeatureMap.State(_s, id) is not BoxState.Off;

    void Pin(string? id)
    {
        _pinned = id;
        _pinnedChanged(id);
        Highlight();
    }

    /// <summary>The hovered or pinned box's own lines thick with their labels, the rest at half.</summary>
    /// <summary>--demo --map-hover-test: moves the mouse over every box 20 times (what a hover does) and logs the process CPU per hover, render thread included.</summary>
    internal void HoverCost()
    {
        var cpu0 = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        int hovers = 0;
        for (int round = 0; round < 20; round++)
            foreach (var id in _at.Keys.ToList())
            {
                _hover = id;
                Highlight();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                hovers++;
            }
        _hover = null;
        Highlight();
        System.Threading.Thread.Sleep(500);   // the render thread finishes its frames
        var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - cpu0;
        System.Diagnostics.Trace.WriteLine($"[map] hover test: {hovers} hovers, {cpu.TotalMilliseconds / hovers:0.00} ms CPU each ({_lines.Count} lines), {System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds / hovers:0.0} ms wall each");
    }

    void Highlight()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var focus = _hover ?? _pinned;
        bool anyFocus = focus is not null || _hoverLine is not null;
        foreach (var (edge, line, tag) in _lines)
        {
            bool mine = focus is not null && (Shown(edge.From) == focus || Shown(edge.To) == focus);
            bool live = On(edge.From) && On(edge.To);
            line.StrokeThickness = mine ? 3 : 1.5;
            line.Opacity = (!anyFocus || mine ? 1 : 0.5) * (live ? 1 : 0.6);
            if (tag is not null) tag.Visibility = mine ? Visibility.Visible : Visibility.Collapsed;
        }
        var pink = ColourOf("party");
        foreach (var p in _party)
        {
            bool shown = FeatureMap.LinkShown(p.Edge, focus, _hoverLine), clicks = _hoverCuts && p.Edge == _hoverLine;
            var look = FeatureMap.LookOf(p.Cut, clicks);
            p.Brush.Color = look == PartyLineLook.Pink ? pink : Grey;
            foreach (var stroke in p.Strokes) (stroke.StrokeThickness, stroke.Opacity) = (shown ? 3 : 1.5, !anyFocus || shown ? 1 : 0.5);
            foreach (var part in p.InFocus) part.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (p.Slash is not null) p.Slash.Visibility = look == PartyLineLook.GreySlashed ? Visibility.Visible : Visibility.Collapsed;
            if (p.Tag is not null) p.Tag.Text = p.Text + (clicks ? p.Cut ? " · 다시 잇기" : " · 끊기" : "");
        }
        Services.Perf.Add("지도 선 강조", System.Diagnostics.Stopwatch.GetTimestamp() - started);
    }

    static Line Dashed(double x, double y, double width) => new()
    {
        X1 = x, X2 = x + width, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Grey), StrokeThickness = 1, StrokeDashArray = [4, 4], Opacity = 0.6,
    };

    /// <summary>The click landed on a switch segment or ⚙ (marked <see cref="Segment"/>), which handle it themselves.</summary>
    static bool OnSegment(MouseButtonEventArgs e)
    {
        // A drop-down list is its own visual tree (a popup): its items lead back to the combo box by the logical tree.
        for (var at = e.OriginalSource as DependencyObject; at is not null; at = (at is Visual ? VisualTreeHelper.GetParent(at) : null) ?? LogicalTreeHelper.GetParent(at))
            if (at is FrameworkElement { Tag: Segment }) return true;
        return false;
    }

    /// <summary>모두 접기 / 모두 펼치기: every box, overriding the ones folded or opened by hand.</summary>
    public void SetAll(bool open)
    {
        _open.Clear();
        if (open) _open.UnionWith(FeatureMap.Boxes.Select(b => b.Id));
        Build();
    }
}
