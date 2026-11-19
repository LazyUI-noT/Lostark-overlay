using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The metro drawing (metro-map.plan.md steps 2–4): <see cref="MetroRoute"/>'s lines with stations, the party row with
/// its P badges and tags, and the rings round what a switched-off box needs.</summary>
internal sealed partial class FeatureMapView
{
    /// <summary>Line width, in focus, the dark casing under each line, and the 45° corner (user, 2026-11-18).</summary>
    const double LineWidth = 3, FocusWidth = 4, CasingWidth = 6, Corner = 8, StationSize = 7;
    /// <summary>The party tags: right edge this far left of 파티, one per row, the first a little above its top.</summary>
    const double TagGap = 14, TagPitch = 17, TagTop = -4, TagHeight = 15;
    /// <summary>A focus line's own lane right of its box's column (the channel's lanes start at <see cref="MetroRoute.Margin"/>).</summary>
    const double FocusLane = 4;
    /// <summary>The line 파티 → 파티원 창 leaves this far down 파티's side.</summary>
    const double PeersAt = 31;
    /// <summary>Faded in focus: other lines, their casing and stations, and in a needs view the boxes not needed.</summary>
    const double FadedLine = 0.18, FadedCasing = 0.3, FadedBox = 0.3;
    /// <summary>Space above the top box for its ring's label (<see cref="Ring"/>), which the viewer would clip.</summary>
    const double RingRoom = 16;

    /// <summary>The far box of each party line that is a tag (<see cref="FeatureMap.UsesPorts"/>): its header has the P badge.</summary>
    static readonly HashSet<string> TalksToParty = [.. FeatureMap.Edges.Where(FeatureMap.UsesPorts).Select(e => e.To == "party" ? e.From : e.To)];

    /// <summary>A role's ring colour and name on the map (공통 red, 기본 blue, 대체 purple, 선택 yellow).</summary>
    static readonly Dictionary<MapNeedRole, Color> RoleColour = new()
    {
        [MapNeedRole.Common] = Color.FromRgb(0xFF, 0x6B, 0x6B), [MapNeedRole.Main] = Color.FromRgb(0x74, 0xC0, 0xFC),
        [MapNeedRole.Fallback] = Color.FromRgb(0xB1, 0x97, 0xFC), [MapNeedRole.Optional] = Color.FromRgb(0xFC, 0xC4, 0x19),
    };
    static readonly Color RingInk = Color.FromRgb(0x1B, 0x1C, 0x20), HoverRing = Color.FromRgb(0xE9, 0xEA, 0xEE);

    sealed record Drawn(MetroLink Link, Path Line, Path Casing, Ellipse[] Stations, TextBlock? Tag);
    readonly List<Drawn> _lines = [];
    readonly List<UIElement> _rings = [];
    string? _ringsFor;   // the box whose needs _rings show

    /// <summary>Whether a key is saved (설정 → 로아 API), for what a box needs; off without one.</summary>
    public Func<bool> HasKey { get; init; } = () => false;
    /// <summary>The hovered box changed (null: none), for the read chips above the map.</summary>
    public Action<string?>? HoverChanged { get; set; }
    string? _hoverSaid;

    /// <summary>Moves each face to its place (a street above the first box can sit above 0: the map shifts down for it), then draws.</summary>
    void Arrange(MetroLayout layout, IReadOnlyList<(MetroLink Link, IReadOnlyList<MapEdge> Edges)> links)
    {
        double top = layout.Lines.SelectMany(l => l.Points).Select(p => p.Y).DefaultIfEmpty(0).Min();
        double dy = Math.Max(top < 0 ? -top + Tokens.Space.Gap : 0, RingRoom);
        foreach (var (id, r) in layout.At)
        {
            SetLeft(_faces[id], r.X);
            SetTop(_faces[id], r.Y + dy);
            _at[id] = new Rect(r.X, r.Y + dy, r.Width, r.Height);
        }
        double separator = layout.Separator + dy;
        Children.Add(Dashed(0, separator, layout.Width));
        Width = layout.Width;
        Height = layout.Height + dy;
        var taken = layout.At.Values.Select(r => r with { Y = r.Y + dy }).ToList();   // tags keep off boxes and each other
        foreach (var (link, edges) in links)
            if (layout.Lines.FirstOrDefault(l => l.Link == link) is { } line)
                DrawLine(link, edges, [.. line.Points.Select(p => new Point(p.X, p.Y + dy))], taken);
        DrawPeers(taken);
        int i = 0;
        foreach (var edge in FeatureMap.Edges.Where(FeatureMap.UsesPorts))
            if (DrawTag(edge, i, separator, layout.ColumnLeft)) i++;   // no gap in the stack for a tag not drawn
    }

    void DrawLine(MetroLink link, IReadOnlyList<MapEdge> edges, IReadOnlyList<Point> points, List<MetroRect> taken)
    {
        var corners = MetroRoute.Chamfer([.. points.Select(p => new MetroPoint(p.X, p.Y))], Corner).Select(p => new Point(p.X, p.Y)).ToList();
        var geometry = Poly(corners);
        bool live = On(link.From) && On(link.To);
        var colour = live ? ColourOf(link.From) : Grey;
        var casing = new Path { Data = geometry, StrokeThickness = CasingWidth, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        casing.SetResourceReference(Shape.StrokeProperty, Themes.BackgroundBrushKey);
        var line = new Path
        {
            Data = geometry, Stroke = new SolidColorBrush(colour), StrokeThickness = LineWidth, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
            StrokeDashArray = link.Optional ? [2, 4 / 3.0] : null,   // 6 4 px at 3 px wide
        };
        Children.Add(casing);
        Children.Add(line);
        Ellipse[] stations = [Station(corners[0], colour), Station(corners[^1], colour)];
        var labels = edges.Select(e => e.Label).OfType<string>().Distinct().ToList();
        TextBlock? tag = labels.Count == 0 ? null : LineTag(string.Join(" · ", labels), corners, taken);
        _lines.Add(new Drawn(link, line, casing, stations, tag));
        if (live && edges.Any(e => FeatureMap.Lit(e, _detect))) Lights(geometry, Colors.White, colour, LightSpeed);   // a reader that works (감지 중 ✓)
        if (edges.Any(e => FeatureMap.FlowsBack(e, _detect)))
        {
            corners.Reverse();
            Lights(Poly(corners), FailLight, FailLight, FailSpeed);
        }
    }

    static PathGeometry Poly(IReadOnlyList<Point> points)
    {
        var figure = new PathFigure { StartPoint = points[0] };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    Ellipse Station(Point at, Color colour)
    {
        var station = new Ellipse { Width = StationSize, Height = StationSize, Stroke = new SolidColorBrush(colour), StrokeThickness = 2, IsHitTestVisible = false };
        station.SetResourceReference(Shape.FillProperty, Themes.BackgroundBrushKey);
        SetLeft(station, at.X - StationSize / 2);
        SetTop(station, at.Y - StationSize / 2);
        Panel.SetZIndex(station, 3);   // over the box edge it sits on
        Children.Add(station);
        return station;
    }

    /// <summary>A line's words, in focus only, on a level stretch clear of boxes and earlier words (<see cref="MetroRoute.TagSpot"/>),
    /// longest first: lines from one box part there, so their words don't pile up. Its spot is added to <paramref name="taken"/>.</summary>
    TextBlock LineTag(string text, IReadOnlyList<Point> corners, List<MetroRect> taken)
    {
        var tag = new TextBlock { Text = text, FontSize = Tokens.Font.Hint, IsHitTestVisible = false };
        tag.SetResourceReference(TextBlock.BackgroundProperty, Themes.BackgroundBrushKey);
        tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = tag.DesiredSize;
        var at = MetroRoute.TagSpot([.. corners.Select(p => new MetroPoint(p.X, p.Y))], size.Width, size.Height, taken);
        taken.Add(new MetroRect(at.X, at.Y, size.Width, size.Height, -1));
        SetLeft(tag, at.X);
        SetTop(tag, at.Y);
        tag.Visibility = Visibility.Collapsed;   // after measuring: collapsed measures as 0
        Panel.SetZIndex(tag, 3);
        Children.Add(tag);
        return tag;
    }

    /// <summary>파티 → 파티원 창: a short dotted line beside 파티 that a click cuts or reconnects.</summary>
    void DrawPeers(List<MetroRect> taken)
    {
        var edge = FeatureMap.Edges.Single(e => e is { From: "party", To: "peers" });
        Rect party = _at["party"], peers = _at["peers"];
        var geometry = Poly([new(party.Right, party.Top + PeersAt), new(peers.Left, party.Top + PeersAt)]);
        var brush = new SolidColorBrush(ColourOf("party"));
        var line = new Path { Data = geometry, Stroke = brush, StrokeThickness = LineWidth, StrokeDashArray = [2 / 3.0, 1], IsHitTestVisible = false };
        Children.Add(line);
        Station(geometry.Figures[0].StartPoint, ColourOf("party"));
        Station(new(peers.Left, party.Top + PeersAt), ColourOf("party"));
        bool cut = FeatureMap.Cut(_s, edge);
        string? text = LabelOf(edge, cut);
        var tag = text is null ? null : LineTag(text, [geometry.Figures[0].StartPoint, new(peers.Left, party.Top + PeersAt)], taken);
        var drawn = new PartyLine(edge, cut, brush, [line], tag is null ? [] : [tag], Slash(geometry), tag, text);
        _party.Add(drawn);
        CutHandle(drawn, geometry);
    }

    /// <summary>A party line as a tag left of 파티 (스킬쿨 →, ← 파티원 전분): a click cuts or reconnects it; grey and struck through when
    /// cut. Its dotted line from the box shows only in focus (<see cref="FeatureMap.LinkShown"/>): out of the box's right edge near
    /// its bottom, down its own lane right of the column, along the separator, down beside the tag.</summary>
    bool DrawTag(MapEdge edge, int index, double separator, IReadOnlyList<double> columnLeft)
    {
        bool sends = edge.To == "party";
        string far = sends ? edge.From : edge.To;
        if (Shown(far) != far) return false;
        bool cut = FeatureMap.Cut(_s, edge);
        var text = new TextBlock { Text = sends ? $"{edge.Label} →" : $"← {edge.Label}", FontSize = Tokens.Font.Hint, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border { Child = text, Height = TagHeight, Padding = new(Tokens.Space.Gap, 0, Tokens.Space.Gap, 0), CornerRadius = new(TagHeight / 2) };
        string name = $"{_byId[edge.From].Title} → {_byId[edge.To].Title} 파티 연결";
        var toggle = UiBuild.Bare<ToggleButton>(chip, name, $"cut:{edge.From}:{edge.To}");
        (toggle.IsChecked, toggle.FocusVisualStyle, toggle.Cursor) = (!cut, null, Cursors.Hand);
        toggle.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Rect party = _at["party"], box = _at[far];
        double right = party.Left - TagGap, y = party.Top + TagTop + index * TagPitch;
        SetLeft(toggle, right - toggle.DesiredSize.Width);
        SetTop(toggle, y);
        Panel.SetZIndex(toggle, 4);
        Children.Add(toggle);

        int own = FeatureMap.Edges.Where(FeatureMap.UsesPorts).TakeWhile(e => e != edge).Count(e => (e.To == "party" ? e.From : e.To) == far);
        int column = _byId[far].Column;
        double lane = columnLeft[column] + ColumnWidths[column] + FocusLane + own * 2;
        double level = separator - 3 + index * 2, beside = party.Left - FocusLane - index * 2, mid = y + TagHeight / 2;
        var corners = MetroRoute.Chamfer([new(box.Right, box.Bottom - 6 - own * 4), new(lane, box.Bottom - 6 - own * 4), new(lane, level), new(beside, level), new(beside, mid), new(right, mid)], 4);
        var brush = new SolidColorBrush(ColourOf("party"));
        var link = new Path { Data = Poly([.. corners.Select(p => new Point(p.X, p.Y))]), Stroke = brush, StrokeThickness = 1.5, StrokeDashArray = [1.5, 2.5], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Panel.SetZIndex(link, 3);
        Children.Add(link);

        var drawn = new PartyLine(edge, cut, brush, [link], [link], null, text, text.Text, chip);
        _party.Add(drawn);
        static bool ByKey() => InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        toggle.MouseEnter += (_, _) => HoverLine(edge, cuts: true);
        toggle.MouseLeave += (_, _) => { if (!(toggle.IsKeyboardFocused && ByKey())) HoverLine(null, cuts: false); };
        toggle.GotKeyboardFocus += (_, _) => { if (ByKey()) HoverLine(edge, cuts: true); };
        toggle.LostKeyboardFocus += (_, _) => HoverLine(null, cuts: false);
        toggle.Click += (_, _) =>
        {
            FeatureMap.SetCut(_s, edge, !FeatureMap.Cut(_s, edge));   // now, not at build: a second click before the rebuild flips back
            _changed();
        };
        return true;
    }

    static Border PartyBadge()
    {
        var party = Color.FromRgb(0xD6, 0x33, 0x6C);
        return new Border
        {
            Background = new SolidColorBrush(party), CornerRadius = new(3), Padding = new(5, 0, 5, 0), Margin = new(Tokens.Space.Tight, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "P", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
            ToolTip = "파티와 주고받습니다. 파티 옆 표를 누르면 끊거나 잇습니다.",
        };
    }

    /// <summary>--map-hover: this box as if the mouse were on it (its lines, or the rings of what it needs), through rebuilds.</summary>
    internal string? DemoHover { init => _demoHover = value; }

    readonly string? _demoHover;

    /// <summary>The hovered box is off and needs something: each shown box it needs, with the first section that names it
    /// (공통 &gt; 기본 &gt; 대체 &gt; 선택), whether one of them there is off and whether its row is optional (대체's 대난투 체력). Null otherwise.</summary>
    (IReadOnlyList<MapNeedSection> Sections, Dictionary<string, (MapNeedRole Role, bool Off, bool Optional)> Boxes)? NeedFocus()
    {
        if (_hover is not { } id || _byId[id].Column == 0) return null;   // on or off: what it needs is ringed (user, 2026-11-19)
        var sections = FeatureMap.Needs(_s, id, HasKey());
        var boxes = new Dictionary<string, (MapNeedRole Role, bool Off, bool Optional)>();
        foreach (var section in sections)
            foreach (var row in section.Rows)
                foreach (var alt in row.Alternatives.Where(a => !a.StartsWith('#')))
                {
                    if (Shown(alt) is not { } shown || shown == id) continue;
                    bool off = !FeatureMap.Satisfied(_s, alt, HasKey()), optional = row.Optional || section.Role == MapNeedRole.Optional;
                    if (!boxes.TryGetValue(shown, out var known)) boxes[shown] = (section.Role, off, optional);
                    else if (known.Role == section.Role) boxes[shown] = (known.Role, known.Off || off, known.Optional && optional);
                }
        return sections.Count == 0 ? null : (sections, boxes);
    }

    /// <summary>The hovered or pinned box's own lines thick with their words, the rest faded; over a box, what it
    /// needs ringed (✓ on, · 꺼짐 off) and the rest faded, whether the box is on or off.</summary>
    void Highlight()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var focus = _hover ?? _pinned;
        bool anyFocus = focus is not null || _hoverLine is not null;
        var need = NeedFocus();
        if (_hover != _hoverSaid) { _hoverSaid = _hover; HoverChanged?.Invoke(_hover); }
        foreach (var d in _lines)
        {
            bool mine = need is { } n ? n.Boxes.ContainsKey(d.Link.From) && (d.Link.To == _hover || n.Boxes.ContainsKey(d.Link.To))
                : !anyFocus || focus is not null && (d.Link.From == focus || d.Link.To == focus);
            d.Line.StrokeThickness = mine && focus is not null ? FocusWidth : LineWidth;
            d.Line.Opacity = mine ? 1 : FadedLine;
            d.Casing.Opacity = mine ? 1 : FadedCasing;
            foreach (var s in d.Stations) s.Opacity = mine ? 1 : FadedCasing;
            if (d.Tag is not null) d.Tag.Visibility = mine && focus is not null && need is null ? Visibility.Visible : Visibility.Collapsed;   // the rings say it in a needs view
        }
        var pink = ColourOf("party");
        foreach (var p in _party)
        {
            bool shown = FeatureMap.LinkShown(p.Edge, focus, _hoverLine), clicks = _hoverCuts && p.Edge == _hoverLine;
            var look = FeatureMap.LookOf(p.Cut, clicks);
            p.Brush.Color = look == PartyLineLook.Pink ? pink : Grey;
            foreach (var stroke in p.Strokes) (stroke.StrokeThickness, stroke.Opacity) = (p.Chip is null ? shown ? FocusWidth : LineWidth : 1.5, !anyFocus || shown ? 1 : 0.5);
            foreach (var part in p.InFocus) part.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (p.Slash is not null) p.Slash.Visibility = look == PartyLineLook.GreySlashed ? Visibility.Visible : Visibility.Collapsed;
            if (p.Chip is not null)
            {
                var chipColour = look == PartyLineLook.Pink ? pink : Grey;
                if ((p.Chip.Background as SolidColorBrush)?.Color != chipColour) p.Chip.Background = new SolidColorBrush(chipColour);
                p.Tag!.TextDecorations = look == PartyLineLook.Pink ? null : TextDecorations.Strikethrough;
            }
            if (p.Tag is not null) p.Tag.Text = p.Text + (clicks ? p.Cut ? " · 다시 잇기" : " · 끊기" : "");
        }
        foreach (var (id, face) in _faces) face.Opacity = need is not { } n || id == _hover || n.Boxes.ContainsKey(id) ? 1 : FadedBox;
        string? ringsFor = need is null ? null : _hover;
        if (ringsFor != _ringsFor)   // the rings only change with the box (a switch rebuilds the map, and Build clears them)
        {
            foreach (var ring in _rings) Children.Remove(ring);
            _rings.Clear();
            _ringsFor = ringsFor;
        }
        if (need is { } needs && _rings.Count == 0)
        {
            Ring(_hover!, HoverRing, false, FeatureMap.Satisfied(_s, _hover!, HasKey()) ? "켜짐" : "꺼짐");
            bool oneWay = !needs.Sections.Any(s => s.Role == MapNeedRole.Main);
            foreach (var (id, (role, off, optional)) in needs.Boxes)
                Ring(id, RoleColour[role], optional, $"{(role == MapNeedRole.Common && oneWay ? "필수" : RoleName(role))} {(off ? "· 꺼짐" : "✓")}");
        }
        Services.Perf.Add("지도 선 강조", System.Diagnostics.Stopwatch.GetTimestamp() - started);
    }

    internal static Color RoleColourOf(MapNeedRole role) => RoleColour[role];

    internal static string RoleName(MapNeedRole role) => role switch
    {
        MapNeedRole.Common => "공통", MapNeedRole.Main => "기본", MapNeedRole.Fallback => "대체", _ => "선택",
    };

    /// <summary>A ring round box <paramref name="id"/> with its label on the top edge, right.</summary>
    void Ring(string id, Color colour, bool dashed, string label)
    {
        var r = _at[id];
        var ring = new Rectangle
        {
            Width = r.Width + 8, Height = r.Height + 8, RadiusX = 9, RadiusY = 9, Stroke = new SolidColorBrush(colour), StrokeThickness = 2,
            StrokeDashArray = dashed ? [2.5, 2] : null, IsHitTestVisible = false,
        };
        SetLeft(ring, r.Left - 4);
        SetTop(ring, r.Top - 4);
        var pill = new Border
        {
            Background = new SolidColorBrush(colour), CornerRadius = new(8), Padding = new(6, 0, 6, 0), IsHitTestVisible = false,
            Child = new TextBlock { Text = label, FontSize = Tokens.Font.Hint, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(RingInk) },
        };
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(pill, r.Right - pill.DesiredSize.Width);
        SetTop(pill, r.Top - pill.DesiredSize.Height + 3);
        Panel.SetZIndex(ring, 5);
        Panel.SetZIndex(pill, 6);   // over the ring of the box above
        foreach (var part in new UIElement[] { ring, pill })
        {
            Children.Add(part);
            _rings.Add(part);
        }
    }
}
