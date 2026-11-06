using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 표시's map (Phase 4, mockup v59): <see cref="FeatureMap.Boxes"/> in four columns, coloured by group, with
/// <see cref="FeatureMap.Edges"/> between them. A click anywhere on a box (not on its switches or ⚙) folds or opens it and
/// keeps its lines thick; a click on empty map lets go. Switches are the mockup's pills (green 켜기, blue 표시 안 함, grey
/// 끄기, dark 읽기; a gold border on a character's own part). Lights run along the lines in use; hovering a party line offers
/// 끊기. Everything shown comes from Core; this only draws and wires.</summary>
internal sealed class FeatureMapView : Canvas
{
    static readonly double[] ColumnWidths = [220, 200, 220, 210];
    /// <summary>Wider between 파티's column and the next: the party's lines both ways run there and are cut there (user, 2026-11-05).</summary>
    static readonly double[] ColumnGaps = [54, 68, 64];
    /// <summary>Room right of the last column: a same-column arrow (control points 40 px out) bulges about 30 px past it.</summary>
    const double SameColumnArc = 32;
    const double FoldedHeight = 34, Pad = 8, InnerIndent = 10, Spacing = 10;
    /// <summary>파티 stands taller, its five lines apart; the top row's dashed line and the room under it follow (user, 2026-11-05).</summary>
    const double PartyHeight = 62, TopRowGap = 30;
    /// <summary>Between lines on a box's side; wider between party lines (a 12 px hit line each), and between lines going in and out.</summary>
    const double PortStep = 6, PartyPortStep = 9, PortGap = 14;
    /// <summary>One speed on every line, one light per <see cref="LightGap"/> px (user, 2026-11-05: half the lights, half the speed).</summary>
    const double LightSpeed = 125, LightGap = 110;
    /// <summary>A reader that can't read: dark orange lights back to the game settings it depends on, at 70% of the speed.</summary>
    const double FailSpeed = LightSpeed * 0.7;
    const string Segment = "map-seg";
    static readonly Color Green = Color.FromRgb(0x2F, 0x9E, 0x44), Blue = Color.FromRgb(0x3B, 0x5B, 0xDB), Grey = Color.FromRgb(0x86, 0x8E, 0x96),
        Dark = Color.FromRgb(0x49, 0x50, 0x57), FailLight = Color.FromRgb(0xD9, 0x48, 0x0F), Gold = Color.FromRgb(0xF0, 0xA8, 0x00), Cut = Color.FromRgb(0xE0, 0x31, 0x31);
    /// <summary>Boxes with a per-character 표시 (the gold mark on the dot).</summary>
    static readonly HashSet<string> PerCharacter = ["skillview", "aden", "judg", "buff", "meter"];
    /// <summary>The character's own switch parts (gold border): the rest are the profile's.</summary>
    static readonly HashSet<(string, int)> CharacterPart = [("skillview", 0), ("aden", 1), ("judg", 1), ("buff", 1)];
    /// <summary>At the very top of their columns, with a dashed line under them (user, 2026-11-05).</summary>
    static readonly HashSet<string> Top = ["party", "peers"];

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
    readonly Action<string?> _pinnedChanged;
    readonly HashSet<string> _open;
    readonly Dictionary<string, Rect> _at = [];
    readonly Dictionary<string, MapBox> _byId = FeatureMap.Boxes.ToDictionary(b => b.Id);
    readonly List<(MapEdge Edge, Path Line, TextBlock? Tag)> _lines = [];
    /// <summary>Each line's height where it meets a box side, spread so lines sharing a side don't meet in one point.</summary>
    readonly Dictionary<(MapEdge, bool AtStart), double> _ports = [];
    string? _pinned, _hover, _pressed;
    IReadOnlyDictionary<string, Detection> _detect = DetectBoard.Empty;
    TimeSpan _now;
    string _drawnFrom = "";   // the marks and flowing lines the map was last built with

    /// <summary>감지 중 from the engine: the map is rebuilt only when a mark or a flowing line changed.</summary>
    public void SetDetect(IReadOnlyDictionary<string, Detection> board, TimeSpan now)
    {
        (_detect, _now) = (board, now);
        if (IsLoaded && Signature() != _drawnFrom) Build();
    }

    string Signature() => string.Join(",", FeatureMap.Boxes.Select(b => $"{b.Id}:{_detect.GetValueOrDefault(b.Id)?.Mark}:{Flowing(b.Id)}"));

    /// <summary>A reader's lines carry lights while it keeps finding (DetectBoard.Flowing); a box without a reader of its own
    /// (DPS 미터기, 스킬창 표시, …) while a reader feeding it does.</summary>
    bool Flowing(string id) => _detect.ContainsKey(id) ? DetectBoard.Flowing(_detect, id, _now)
        : FeatureMap.Edges.Any(e => e.To == id && !e.Party && _detect.ContainsKey(e.From) && DetectBoard.Flowing(_detect, e.From, _now));

    /// <param name="open">The boxes opened by hand, kept by 설정 across rebuilds.</param>
    /// <param name="changed">After a switch: 설정 saves and refills the page (the map with it).</param>
    public FeatureMapView(Settings s, HashSet<string> open, string? pinned, Action changed, Action<string> go, Action<string> note, Action<string?> pinnedChanged)
    {
        (_s, _open, _pinned, _changed, _go, _note, _pinnedChanged) = (s, open, pinned, changed, go, note, pinnedChanged);
        Background = Brushes.Transparent;   // empty map takes the click that lets go
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == this) Pin(null); };
        Loaded += (_, _) => Build();   // attached: theme styles resolve
    }

    void Build()
    {
        Children.Clear();
        _at.Clear();
        _lines.Clear();
        _cpu.Clear();
        _hover = null;
        _drawnFrom = Signature();
        double x = 0, height = 0;
        for (int col = 0; col < ColumnWidths.Length; col++)
        {
            double y = 0;
            var column = FeatureMap.Boxes.Where(b => b.Column == col && b.Inside is null).OrderBy(b => Top.Contains(b.Id) ? 0 : 1);
            foreach (var box in column)
            {
                y = Place(box, x, y, ColumnWidths[col]) + Spacing;
                if (!Top.Contains(box.Id)) continue;
                y = PartyHeight + TopRowGap;
                Children.Add(Dashed(x, PartyHeight + TopRowGap / 2, ColumnWidths[col]));
            }
            height = Math.Max(height, y);
            x += ColumnWidths[col] + (col < ColumnGaps.Length ? ColumnGaps[col] : 0);
        }
        Width = x + SameColumnArc;
        Height = height;
        Ports();
        foreach (var edge in FeatureMap.Edges) DrawEdge(edge);
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
        if (_detect.GetValueOrDefault(box.Id) is { } seen)
        {
            var chip = Chip(seen, box.Id);
            DockPanel.SetDock(chip, Dock.Right);
            head.Children.Add(chip);
        }
        head.Children.Add(new TextBlock { Text = box.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var body = new StackPanel();
        body.Children.Add(head);
        if (Value?.Invoke(box.Id) is { } value) body.Children.Add(new TextBlock { Text = value, Opacity = 0.75, FontSize = 11.5, Margin = new(14, -2, 0, 2) });
        if (ShowResources && ResourceCheck.PartRows.Values.Contains(box.Id))
            body.Children.Add(_cpu[box.Id] = new TextBlock { Text = Cpu(box.Id), FontSize = 11.5, Opacity = 0.8, Margin = new(14, 0, 0, 0), ToolTip = "한 코어를 100%로 본 값 (전체 코어로 나눈 값). 이 PC에서만 재고, 어디에도 보내거나 기록하지 않습니다." });
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
        face.MouseEnter += (_, _) => { _hover = box.Id; Highlight(); };
        face.MouseLeave += (_, _) => { if (_hover == box.Id) _hover = null; Highlight(); };
        return face;
    }

    /// <summary>One switch part as a pill: its choices side by side, the chosen one filled by meaning. A part with no setting
    /// yet is drawn faint and does nothing (its ⚙ page sets it).</summary>
    UIElement Pill(MapBox box, int part)
    {
        int? selected = FeatureMap.Selected(_s, box.Id, part);
        bool own = CharacterPart.Contains((box.Id, part));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var pill = new Border
        {
            Child = row, CornerRadius = new(7), BorderThickness = new(own ? 1.5 : 1), Margin = new(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = new SolidColorBrush(own ? Gold : Dark), ClipToBounds = true, Opacity = selected is null ? 0.45 : 1,
            ToolTip = selected is null ? "이 스위치는 다음 단계에서 연결됩니다. ⚙에서 설정하세요." : own ? "이 캐릭터에만 적용됩니다." : null,
        };
        var choices = box.Choices[part];
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            bool on = selected == i;
            var seg = new Border
            {
                Tag = Segment, Padding = new(8, 2, 8, 2), Cursor = selected is null ? Cursors.Arrow : Cursors.Hand,
                Background = on ? new SolidColorBrush(Meaning(choices[i])) : Brushes.Transparent,
                Child = on ? new TextBlock { Text = choices[i], FontSize = 11.5, Foreground = Brushes.White } : new TextBlock { Text = choices[i], FontSize = 11.5 },
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

    /// <summary>감지 중: ✓ green read for certain, ? yellow nothing yet, ! red should have read; the mark goes with the colour.</summary>
    UIElement Chip(Detection d, string id)
    {
        var (mark, colour, what) = d.Mark switch
        {
            DetectMark.Concrete => ("✓", Green, "감지됨"),
            DetectMark.Failed => ("!", Cut, "감지 안 됨"),
            _ => ("?", Color.FromRgb(0xE0, 0xA8, 0x00), "아직 찾는 중"),
        };
        string since = Ago(_now - d.At);
        var chip = new Border
        {
            CornerRadius = new(4), Padding = new(4, 0, 4, 0), Margin = new(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(colour), Tag = Segment,
            Child = new TextBlock { Text = $"감지 중 {mark}", FontSize = 10.5, Foreground = Brushes.White },
            ToolTip = (d.Why is { } why ? $"{what} ({since})\n{why}" : $"{what} ({since})") + "\n누르면 감지 화면에서 읽는 자리를 봅니다.",
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        chip.MouseLeftButtonUp += (_, e) => { ShowDetect?.Invoke(id); e.Handled = true; };
        return chip;
    }

    /// <summary>How long ago, for a mark's time (the engine's clock counts from the PC's start, so a clock time means nothing).</summary>
    internal static string Ago(TimeSpan d) => d < TimeSpan.FromSeconds(5) ? "방금" : d < TimeSpan.FromMinutes(1) ? $"{(int)d.TotalSeconds}초 전"
        : d < TimeSpan.FromHours(1) ? $"{(int)d.TotalMinutes}분 전" : $"{(int)d.TotalHours}시간 전";

    static Color Meaning(string choice) => choice switch { "켜기" => Green, "표시 안 함" => Blue, "끄기" => Grey, _ => Dark };

    /// <summary>An arrow between the boxes' facing sides (to the outer box while that is folded); grey unless both ends are
    /// on, party lines dotted pink, a cut one red and dashed. Lights run along lines in use; a party line offers 끊기.</summary>
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
        var colour = cut ? Cut : edge.Party ? ColourOf("party") : live ? ColourOf(edge.From) : Grey;
        string? label = edge.Label is { } l ? l + (cut ? edge.From == "party" ? " (못 받음)" : " (안 보냄)" : "") : null;
        var line = new Path
        {
            Data = geometry, Stroke = new SolidColorBrush(colour), StrokeThickness = 1.5, IsHitTestVisible = false,
            StrokeDashArray = cut ? [4, 3] : edge.Party ? [1.5, 2.5] : edge.Optional ? [5, 3] : null,
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
        _lines.Add((edge, line, tag));
        if (live && !edge.Party && !cut && Flowing(edge.From)) Lights(geometry, Colors.White, colour, LightSpeed);   // what is being read now (감지 중)
        if (FeatureMap.FlowsBack(edge, _detect))
        {
            var back = new PathFigure { StartPoint = end };
            back.Segments.Add(new BezierSegment(c2, c1, start, true));
            var reversed = new PathGeometry([back]);
            reversed.Freeze();
            Lights(reversed, FailLight, FailLight, FailSpeed);
        }
        if (edge.Party) CutHandle(edge, geometry, cut);
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

    /// <summary>끊기 on a party line: hovering shows a red slash and what a click does; the click cuts or reconnects it.</summary>
    void CutHandle(MapEdge edge, PathGeometry geometry, bool cut)
    {
        var hit = new Path { Data = geometry, Stroke = Brushes.Transparent, StrokeThickness = 12, Cursor = Cursors.Hand, ToolTip = cut ? "다시 잇기" : "끊기" };
        geometry.GetPointAtFractionLength(0.5, out var mid, out _);
        var slash = new Line { X1 = mid.X - 8, Y1 = mid.Y + 9, X2 = mid.X + 8, Y2 = mid.Y - 9, Stroke = new SolidColorBrush(Cut), StrokeThickness = 3, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var word = new TextBlock { Text = cut ? "다시 잇기" : "끊기", Foreground = new SolidColorBrush(Cut), FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        SetLeft(word, mid.X + 10);
        SetTop(word, mid.Y - 18);
        hit.MouseEnter += (_, _) => slash.Visibility = word.Visibility = Visibility.Visible;
        hit.MouseLeave += (_, _) => slash.Visibility = word.Visibility = Visibility.Collapsed;
        hit.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            FeatureMap.SetCut(_s, edge, !cut);
            _changed();
        };
        foreach (var part in new UIElement[] { hit, slash, word }) { Panel.SetZIndex(part, 4); Children.Add(part); }
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
        foreach (var (edge, line, tag) in _lines)
        {
            bool mine = focus is not null && (Shown(edge.From) == focus || Shown(edge.To) == focus);
            bool live = On(edge.From) && On(edge.To);
            line.StrokeThickness = mine ? 3 : 1.5;
            line.Opacity = (focus is null || mine ? 1 : 0.5) * (live || edge.Party ? 1 : 0.6);
            if (tag is not null) tag.Visibility = mine ? Visibility.Visible : Visibility.Collapsed;
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
