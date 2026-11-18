using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 기능 맵's map (metro-map.plan.md): <see cref="FeatureMap.Boxes"/> in four columns, coloured by group, placed and
/// routed by <see cref="MetroRoute"/>: lines across, 45° and down in the source's colour, a station where a line meets a box.
/// A click anywhere on a box (not on its switches or ⚙) folds or opens it (the map reroutes) and keeps its lines thick; a click
/// on empty map lets go. Switches are the mockup's pills (green 켜기, blue 표시 안 함, grey 끄기, dark 읽기; a gold border on a
/// character's own part). Lights run along the lines of ✓ readers. 파티 and 파티원 창 stand in a row under the columns; a box that
/// talks to 파티 has a pink P, and each party line is a tag left of 파티 that a click cuts or reconnects, its dotted line to the
/// box only in focus. Hovering a switched-off box rings what it needs (<see cref="FeatureMap.Needs"/>); hovering any box shows
/// its card (<see cref="Preview"/>). Left-drag on empty map, or the middle button anywhere, pans it (<see cref="StartPan"/>).
/// Everything shown comes from Core; this only draws and wires.</summary>
internal sealed partial class FeatureMapView : Canvas
{
    static readonly double[] ColumnWidths = [220, 200, 220, 210];
    /// <summary>A box's frame opacity: off, and working in part below a yellow ! reader (text keeps its contrast in both).</summary>
    const double OffFrame = 0.4, PartialFrame = 0.6;
    const double FoldedHeight = MetroRoute.Head, Pad = Tokens.Space.Gap, InnerIndent = Tokens.Space.Gap;   // box height is the map's own
    /// <summary>파티 stands taller, its tags beside it, in a row of its own under the columns (user, 2026-11-06).</summary>
    const double PartyHeight = 62;
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
    readonly Dictionary<string, Border> _faces = [];
    readonly Dictionary<string, MapBox> _byId = FeatureMap.Boxes.ToDictionary(b => b.Id);
    /// <summary>A party line's parts: <paramref name="Strokes"/> share <paramref name="Brush"/>, <paramref name="InFocus"/> show
    /// only in focus, <paramref name="Slash"/> marks a cut about to happen; <paramref name="Chip"/>: its tag left of 파티.</summary>
    sealed record PartyLine(MapEdge Edge, bool Cut, SolidColorBrush Brush, Shape[] Strokes, UIElement[] InFocus, Line? Slash, TextBlock? Tag, string? Text, Border? Chip = null);
    readonly List<PartyLine> _party = [];
    Window? _owner;
    MapEdge? _hoverLine;
    bool _hoverCuts;   // on the part a click cuts, not the far box's short arrow
    readonly Popup _preview = new() { AllowsTransparency = true, Placement = PlacementMode.Right, HorizontalOffset = 8, IsHitTestVisible = false };
    /// <summary>Crossing the map opens nothing: a card only for a box the mouse rests on.</summary>
    readonly DispatcherTimer _previewWait = new() { Interval = TimeSpan.FromMilliseconds(350) };
    (string Id, UIElement Face, bool Detect)? _previewFor;
    string? _liveFor;
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
        Grid.SetIsSharedSizeScope(this, true);   // every switch key on the map shares one width (Pill)
        // Empty map: a left drag pans, a left click lets go of the pinned box. The middle button pans from anywhere.
        MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left && e.OriginalSource == this) StartPan(e); };
        MouseMove += (_, e) => Pan(e);
        MouseUp += (_, e) =>
        {
            if (_pan is not { } p || e.ChangedButton != p.Button) return;
            bool click = !_panMoved && p.Button == MouseButton.Left;
            StopPan();
            if (click) Pin(null);
        };
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

    /// <summary>Faces first, each top box at the top-left with its open inner boxes, to measure them; then <see cref="MetroRoute.Layout"/>
    /// places the top boxes (the inner ones move with theirs) and routes the lines.</summary>
    void Build()
    {
        HidePreview();
        Children.Clear();
        _at.Clear();
        _faces.Clear();
        _lines.Clear();
        _party.Clear();
        _rings.Clear();
        _ringsFor = null;
        _cpu.Clear();
        (_hover, _hoverLine, _hoverCuts) = (_demoHover, null, false);
        _drawnFrom = Signature();
        var boxes = new List<MetroBox>();
        foreach (var box in FeatureMap.Boxes.Where(b => b.Inside is null))
        {
            Place(box, 0, 0, ColumnWidths[box.Column]);
            boxes.Add(new(box.Id, box.Column, _at[box.Id].Height, PartyRow.Contains(box.Id)));
        }
        List<MetroInner> inner = [.. _at.Where(kv => _byId[kv.Key].Inside is not null)
            .Select(kv => new MetroInner(kv.Key, TopOf(kv.Key), kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height))];
        var links = MetroRoute.Links(FeatureMap.Edges, Shown);
        var layout = MetroRoute.Layout(ColumnWidths, boxes, inner, [.. links.Select(l => l.Link)]);
        Arrange(layout, links);
        Highlight();
    }

    string TopOf(string id)
    {
        while (_byId[id].Inside is { } outer) id = outer;
        return id;
    }

    /// <summary>Places <paramref name="box"/> and, while it is open, the boxes inside it; returns its bottom.</summary>
    double Place(MapBox box, double x, double y, double width)
    {
        var inner = FeatureMap.Boxes.Where(b => b.Inside == box.Id).ToList();
        bool open = _open.Contains(box.Id);
        var face = _faces[box.Id] = Face(box, open, inner.Count);
        face.Width = width;
        if (box.Id == "party") { face.MinHeight = PartyHeight; if (face.Child is FrameworkElement body) body.VerticalAlignment = VerticalAlignment.Center; }
        SetLeft(face, x);
        SetTop(face, y);
        Children.Add(face);
        face.Measure(new Size(width, double.PositiveInfinity));
        double bottom = y + face.DesiredSize.Height;
        int innerFrom = Children.Count;
        if (open)
            foreach (var b in inner) bottom = Place(b, x + InnerIndent, bottom + 6, width - 2 * InnerIndent);
        if (box.Id == _justOpened) Arrive(Children, innerFrom);
        if (open && inner.Count > 0) { bottom += Pad; face.Height = bottom - y; }
        _at[box.Id] = new Rect(x, y, width, bottom - y);
        Panel.SetZIndex(face, box.Inside is null ? 1 : 2);
        return bottom;
    }

    Border Face(MapBox box, bool open, int innerCount)
    {
        var state = FeatureMap.State(_s, box.Id);
        var head = new DockPanel { LastChildFill = true, Height = FoldedHeight - 8 };
        // The keyboard's way to fold: the mouse still folds from anywhere on the box.
        var fold = UiBuild.Bare<ToggleButton>(UiBuild.Muted(new TextBlock { Text = open ? "▾" : "▸", Padding = new(Tokens.Space.Gap, 0, Tokens.Space.Hair, 0) }), box.Title, $"fold:{box.Id}");
        (fold.IsChecked, fold.Tag, fold.VerticalAlignment) = (open, Segment, VerticalAlignment.Center);
        AutomationProperties.SetHelpText(fold, open ? "접기" : "펼치기");
        fold.Click += (_, _) => Toggle(box.Id);
        DockPanel.SetDock(fold, Dock.Right);
        head.Children.Add(fold);
        if (open)
        {
            string tip = $"{FeatureMap.Page(box.Id)} 설정 열기";
            var gear = new Button { Content = "⚙", Padding = new(Tokens.Space.Tight, 0, Tokens.Space.Tight, 0), Margin = new(Tokens.Space.Tight, 0, 0, 0), ToolTip = tip, Tag = Segment };
            AutomationProperties.SetName(gear, tip);
            gear.Click += (_, e) => { e.Handled = true; _go(FeatureMap.Page(box.Id)); };
            DockPanel.SetDock(gear, Dock.Right);
            head.Children.Add(gear);
        }
        if (state is { } st)
        {
            bool gold = PerCharacter.Contains(box.Id);
            var dot = new Ellipse
            {
                Width = 10, Height = 10, Margin = new(0, 0, Tokens.Space.Gap, 0), StrokeThickness = 2, Stroke = gold ? new SolidColorBrush(Gold) : null,
                Fill = new SolidColorBrush(st switch { BoxState.On => Green, BoxState.Hidden => Blue, _ => Grey }),
                ToolTip = (st switch { BoxState.On => "켜짐", BoxState.Hidden => "표시 안 함 (읽기는 함)", _ => "꺼짐" }) + (gold ? " · 금색 테: 캐릭터마다 따로" : ""),
            };
            DockPanel.SetDock(dot, Dock.Left);
            head.Children.Add(dot);
        }
        if (TalksToParty.Contains(box.Id))
        {
            var badge = PartyBadge();
            DockPanel.SetDock(badge, Dock.Right);
            head.Children.Add(badge);
        }
        if (!open && innerCount > 0)
        {
            var more = UiBuild.Muted(new TextBlock { Text = $"+{innerCount}", Margin = new(Tokens.Space.Gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(more, Dock.Right);
            head.Children.Add(more);
        }
        UIElement? chip = null;
        var impact = state != BoxState.Off ? FeatureMap.ImpactOf(box.Id, _detect) : null;   // a reader has none: it shows its own mark
        if (_detect.GetValueOrDefault(box.Id) is { } seen)
        {
            chip = Chip(seen);
            DockPanel.SetDock(chip, Dock.Right);
            head.Children.Add(chip);
        }
        // A hit chip names a whole reader: too long beside the title in a narrow box (it cut the title to one letter), so it gets its own line.
        UIElement? hitLine = impact is { } hit && chip is null ? HitChip(hit) : null;
        chip ??= hitLine;
        bool off = state == BoxState.Off;
        var title = new TextBlock { Text = box.Title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(off ? UiBuild.Muted(title) : title);

        var body = new StackPanel();
        body.Children.Add(head);
        if (hitLine is FrameworkElement line)
        {
            (line.HorizontalAlignment, line.Margin) = (HorizontalAlignment.Left, new(Tokens.Space.Block, Tokens.Space.Hair, 0, Tokens.Space.Hair));
            body.Children.Add(line);
        }
        if (Value?.Invoke(box.Id) is { } value) body.Children.Add(UiBuild.Muted(new TextBlock { Text = value, FontSize = Tokens.Font.Hint, Margin = new(Tokens.Space.Block, -2, 0, Tokens.Space.Hair) }));
        if (ShowResources && ResourceCheck.PartRows.Values.Contains(box.Id))
            body.Children.Add(_cpu[box.Id] = UiBuild.Muted(new TextBlock { Text = Cpu(box.Id), FontSize = Tokens.Font.Hint, Margin = new(Tokens.Space.Block, 0, 0, 0), ToolTip = "CPU 한 스레드를 100%로 본 값 (전체 스레드로 나눈 값). 이 PC에서만 재고, 어디에도 보내거나 기록하지 않습니다." }));
        if (box.Note is { } note) body.Children.Add(UiBuild.Muted(new TextBlock { Text = note, FontSize = Tokens.Font.Hint, TextWrapping = TextWrapping.Wrap, Margin = new(0, -2, 0, Tokens.Space.Hair) }));
        if (open)
        {
            int openedFrom = body.Children.Count;
            for (int part = 0; part < box.Choices.Length; part++) body.Children.Add(Pill(box, part));
            if (Body?.Invoke(box.Id) is { } settings) body.Children.Add(settings);
            if (box.Id == _justOpened) Arrive(body.Children, openedFrom);
        }

        var face = new Border
        {
            Child = body, Padding = new(Pad, Tokens.Space.Tight, Pad, Tokens.Space.Tight), CornerRadius = new(Tokens.Radius.Card), BorderThickness = new(4, 1, 1, 1),   // the group's colour as a bar, as on the rail
            // Off: only the frame fades and the title goes muted; text keeps its contrast (audit 2026-11-12: whole-box 0.55 was ~3:1).
            // Working in part below a ! reader: the frame dims too, text keeps its contrast (user, 2026-11-17).
            BorderBrush = new SolidColorBrush(ColourOf(box.Id)) { Opacity = off ? OffFrame : impact?.Level == Impact.Partial ? PartialFrame : 1 }, Cursor = Cursors.Hand, MinHeight = FoldedHeight,
        };
        face.SetResourceReference(Border.BackgroundProperty, Themes.CardBrushKey);
        face.MouseLeftButtonDown += (_, e) => { if (!OnSegment(e)) _pressed = box.Id; };
        face.MouseLeftButtonUp += (_, e) =>
        {
            if (OnSegment(e) || _pressed != box.Id) return;   // a switch, ⚙, or a press that began elsewhere
            e.Handled = true;
            _pressed = null;
            Toggle(box.Id);
        };
        face.MouseEnter += (_, _) =>
        {
            _hover = _demoHover ?? box.Id;   // --map-hover holds against the real mouse
            Highlight();
            bool onChip = chip is { IsMouseOver: true };   // came straight onto the chip: its card, whichever enter comes first
            WaitForPreview(box.Id, onChip ? chip! : face, onChip);
        };
        face.MouseLeave += (_, _) => { if (_hover == box.Id && _demoHover is null) _hover = null; Highlight(); HidePreview(); };
        if (chip is not null)
        {
            chip.MouseEnter += (_, _) => WaitForPreview(box.Id, chip, true);
            chip.MouseLeave += (_, _) => { if (face.IsMouseOver) WaitForPreview(box.Id, face, false); else HidePreview(); };   // back on the box: its own card
        }
        return face;
    }

    /// <summary>The box a click just opened, for the one <see cref="Build"/> that shows it (its new content moves in once).</summary>
    string? _justOpened;

    internal void DemoToggle(string id) => Toggle(id);   // --demo --motion=map-open

    void Toggle(string id)
    {
        if (!_open.Remove(id))
        {
            _open.Add(id);
            _justOpened = id;
        }
        Pin(id);
        Build();
        _justOpened = null;
    }

    /// <summary>Opened content moves in: the children of <paramref name="panel"/> from <paramref name="first"/> on.</summary>
    static void Arrive(UIElementCollection panel, int first)
    {
        for (int i = first; i < panel.Count; i++) UiMotion.Arrive(panel[i], -UiMotion.OpenDrop, UiMotion.OpenTime);
    }

    /// <summary>One switch part as a square well: its choices side by side as bevelled keys, the chosen one lit by meaning. A part with no setting
    /// yet is drawn faint and does nothing (its ⚙ page sets it).</summary>
    UIElement Pill(MapBox box, int part)
    {
        int? selected = FeatureMap.Selected(_s, box.Id, part);
        bool own = CharacterPart.Contains((box.Id, part));
        // Keys as wide as the map's longest choice (표시 안 함), so 끄기|읽기 splits where 켜기|표시 안 함 does (user, 2026-11-12).
        var row = new Grid();
        var pill = new Border
        {
            Child = row, BorderThickness = new(own ? 1.5 : 1), Margin = new(0, Tokens.Space.Tight, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = new SolidColorBrush(own ? Gold : Dark), ClipToBounds = true, Opacity = selected is null ? 0.45 : 1,
            ToolTip = selected is null ? "이 스위치는 여기서 켜고 끌 수 없습니다. ⚙에서 설정하세요." : own ? "이 캐릭터에만 적용됩니다." : null,
        };
        pill.SetResourceReference(Border.BackgroundProperty, WellKey);
        var choices = box.Choices[part];
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            bool on = selected == i;
            var key = new Border
            {
                Padding = new(7, 1, 7, 1), BorderThickness = new(1),   // map geometry, off the grid: three segments must fit a box's fixed width
                Background = on ? Lit(Meaning(choices[i])) : null, BorderBrush = on ? LitEdge : null,
                Child = on ? new TextBlock { Text = choices[i], FontSize = Tokens.Font.Hint, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(RaidBadgeView.InkOn(Meaning(choices[i]))) }
                    : new TextBlock { Text = choices[i], FontSize = Tokens.Font.Hint, TextAlignment = TextAlignment.Center },
            };
            if (!on)
            {
                key.SetResourceReference(Border.BackgroundProperty, KeyFaceKey);
                key.SetResourceReference(Border.BorderBrushProperty, KeyEdgeKey);
            }
            // A radio button per choice (one group per row): Tab, arrows and Space choose, and Narrator says which one is on.
            var seg = UiBuild.Bare<RadioButton>(key, $"{box.Title} {choices[i]}", $"seg:{box.Id}:{part}:{i}");
            (seg.Tag, seg.IsChecked, seg.IsEnabled) = (Segment, on, selected is not null);
            (seg.MinWidth, seg.HorizontalAlignment) = (0, HorizontalAlignment.Stretch);   // the theme's radio button asks 120 px, left-aligned
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = KeyWidthGroup });
            Grid.SetColumn(seg, i);
            if (selected is null) seg.Cursor = Cursors.Arrow;
            seg.Click += (_, _) =>
            {
                if (on) return;
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
    // Per theme, by resource key, so a theme switch redraws them (audit 2026-11-12: on a light card the dark well was a grey slab).
    const string WellKey = "MapWell", KeyFaceKey = "MapKeyFace", KeyEdgeKey = "MapKeyEdge";
    const string KeyWidthGroup = "MapKey";   // SharedSizeGroup of every switch key's column (scope: this Canvas)
    static readonly (Brush Well, Brush Face, Brush Edge) DarkKeys = (
        Vertical(Color.FromArgb(0x70, 0, 0, 0), Color.FromArgb(0x28, 0, 0, 0)),
        Vertical(Color.FromArgb(0x24, 255, 255, 255), Color.FromArgb(0x06, 255, 255, 255)),
        Vertical(Color.FromArgb(0x38, 255, 255, 255), Color.FromArgb(0x60, 0, 0, 0)));
    static readonly (Brush Well, Brush Face, Brush Edge) LightKeys = (
        Vertical(Color.FromArgb(0x30, 0, 0, 0), Color.FromArgb(0x12, 0, 0, 0)),
        Vertical(Color.FromArgb(0xE0, 255, 255, 255), Color.FromArgb(0x90, 255, 255, 255)),
        Vertical(Color.FromArgb(0x18, 0, 0, 0), Color.FromArgb(0x50, 0, 0, 0)));
    static readonly Brush LitEdge = Vertical(Color.FromArgb(0x90, 255, 255, 255), Color.FromArgb(0x90, 0, 0, 0));

    /// <summary>Called by <see cref="Themes.Apply"/>: the switch well and key brushes for a dark or light card.</summary>
    internal static void SetTheme(ResourceDictionary r, bool dark)
    {
        var k = dark ? DarkKeys : LightKeys;
        (r[WellKey], r[KeyFaceKey], r[KeyEdgeKey]) = (k.Well, k.Face, k.Edge);
    }

    /// <summary>감지 중: ✓ green read for certain, ? yellow nothing yet, ! red should have read; the mark goes with the colour.
    /// On a folded box the chip is part of the box: a click there opens the box (user, 2026-11-10), only an open box's chip
    /// leads to 감지.</summary>
    UIElement Chip(Detection d)
    {
        var (mark, colour, _) = Look(d);
        return Badge($"읽는 중 {mark}", colour);
    }

    static readonly Color Waiting = Color.FromRgb(0xE0, 0xA8, 0x00);

    /// <summary>A block below a reader marked ! (<see cref="FeatureMap.ImpactOf"/>): red when it stops, yellow when it works in part
    /// (user, 2026-11-17), naming that reader. No link, as the read chips (rail-split.plan.md 4).</summary>
    UIElement HitChip(BoxImpact hit)
    {
        string root = _byId[hit.Root].Title;
        var (text, colour) = hit.Level == Impact.Stopped ? ($"{root} 못 읽음 !", Cut) : ($"{root} 없이 동작 !", Waiting);
        return Badge(text, colour);
    }

    static Border Badge(string text, Color colour)
    {
        var chip = new Border   // no tooltip: hovering shows the Live card (DetectPreview), which says the same
        {
            CornerRadius = new(Tokens.Radius.Control), Padding = new(Tokens.Space.Tight, 0, Tokens.Space.Tight, 0), Margin = new(Tokens.Space.Tight, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(colour),
            Child = new TextBlock { Text = text, FontSize = Tokens.Font.Hint, Foreground = new SolidColorBrush(RaidBadgeView.InkOn(colour)) },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        return chip;   // no link: the read chips above the map open 화면 인식 설정 (rail-split.plan.md 4)
    }

    /// <summary>A 읽는 중 mark's look: ✓ green, ? yellow, ! red; a reader that is off is grey with no mark.</summary>
    internal static (string Mark, Color Colour, string State) Look(Detection? d) => d?.Mark switch
    {
        null => ("", Grey, "꺼짐"),
        DetectMark.Concrete => ("✓", Green, "읽는 중"),
        DetectMark.Failed => ("!", Cut, "읽어야 하는데 못 읽음"),
        _ => ("?", Waiting, "아직 찾는 중"),
    };

    /// <summary>Box colour for a row with no mark of its own (디버프 줄).</summary>
    internal static Color Unmarked => Dark;

    /// <summary>How long ago, for a mark's time (the engine's clock counts from the PC's start, so a clock time means nothing).</summary>
    internal static string Ago(TimeSpan d) => d < TimeSpan.FromSeconds(5) ? "방금" : d < TimeSpan.FromMinutes(1) ? $"{(int)d.TotalSeconds}초 전"
        : d < TimeSpan.FromHours(1) ? $"{(int)d.TotalMinutes}분 전" : $"{(int)d.TotalHours}시간 전";

    static Color Meaning(string choice) => choice switch { "켜기" => Green, "표시 안 함" => Blue, "끄기" => Grey, _ => Dark };

    /// <summary>Lights travelling the line at <paramref name="speed"/> px/s, one per <see cref="LightGap"/> px. With motion off
    /// (<see cref="UiMotion.On"/>) they stand still along the line: a lit line still reads as lit.</summary>
    void Lights(PathGeometry geometry, Color centre, Color colour, double speed)
    {
        double length = Length(geometry);
        int count = Math.Max(1, (int)Math.Round(length / LightGap));
        var duration = TimeSpan.FromSeconds(length / speed);
        var glow = new RadialGradientBrush(centre, Color.FromArgb(0, colour.R, colour.G, colour.B)) { GradientOrigin = new(0.5, 0.5) };
        glow.Freeze();
        bool moves = UiMotion.On;
        for (int k = 0; k < count; k++)
        {
            var light = new Ellipse { Width = 8, Height = 8, Fill = glow, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
            SetLeft(light, -4);
            SetTop(light, -4);
            Panel.SetZIndex(light, 0);
            if (!moves)
            {
                geometry.GetPointAtFractionLength((double)k / count, out var at, out _);
                light.RenderTransform = new TranslateTransform(at.X, at.Y);
                Children.Add(light);
                continue;
            }
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

    /// <summary>끊기 on a line at 파티: hovering (or Tab onto it) shows what a click makes it; a click or Space cuts or reconnects
    /// it. A toggle button drawn as the line alone, so Narrator says 연결됨 / 끊김 (audit 2026-11-12).</summary>
    void CutHandle(PartyLine line, Geometry geometry)
    {
        var hit = new Path { Data = geometry, Stroke = Brushes.Transparent, StrokeThickness = 12 };
        string name = $"{_byId[line.Edge.From].Title} → {_byId[line.Edge.To].Title} 파티 연결";
        var toggle = UiBuild.Bare<ToggleButton>(hit, name, $"cut:{line.Edge.From}:{line.Edge.To}");
        (toggle.IsChecked, toggle.FocusVisualStyle) = (!line.Cut, null);   // the lit line and its slash show focus
        toggle.MouseEnter += (_, _) => HoverLine(line.Edge, cuts: true);
        // Focus lights the line only when it came by keyboard: a click focuses the button too (and the rebuild refocuses it).
        static bool ByKey() => InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        toggle.MouseLeave += (_, _) => { if (!(toggle.IsKeyboardFocused && ByKey())) HoverLine(null, cuts: false); };
        toggle.GotKeyboardFocus += (_, _) => { if (ByKey()) HoverLine(line.Edge, cuts: true); };
        toggle.LostKeyboardFocus += (_, _) => HoverLine(null, cuts: false);
        toggle.Click += (_, _) =>
        {
            FeatureMap.SetCut(_s, line.Edge, !FeatureMap.Cut(_s, line.Edge));   // now, not at build: a second click before the rebuild flips back
            _changed();
        };
        Panel.SetZIndex(toggle, 4);
        Children.Add(toggle);
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

    /// <summary>A drag in progress (<see cref="StartPan"/>): the button, the pointer's place at the start in the viewer (which
    /// doesn't move as its content scrolls), and where the viewer stood then.</summary>
    sealed record PanStart(MouseButton Button, Point At, ScrollViewer Viewer, double Left, double Top);
    PanStart? _pan;
    bool _panMoved;
    /// <summary>A left press that moves less than this is a click (lets go), not a drag.</summary>
    const double PanSlop = 4;

    /// <summary>The button down grabs the map: it moves with the pointer, both ways, in the viewer it sits in (설정 → 기능 맵,
    /// height fitted to the page). The mouse is captured until the button comes up or the capture is lost.</summary>
    void StartPan(MouseButtonEventArgs e)
    {
        if (Ancestor<ScrollViewer>(this) is not { } viewer) return;
        HidePreview();   // the capture stops the box's MouseLeave, which would close it
        _pan = new(e.ChangedButton, e.GetPosition(viewer), viewer, viewer.HorizontalOffset, viewer.VerticalOffset);
        _panMoved = false;
        CaptureMouse();
        if (e.ChangedButton == MouseButton.Middle) Mouse.OverrideCursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    void Pan(MouseEventArgs e)
    {
        if (_pan is not { } p) return;
        if ((p.Button == MouseButton.Middle ? e.MiddleButton : e.LeftButton) != MouseButtonState.Pressed)
        {
            StopPan();   // let go where the map didn't see it
            return;
        }
        var moved = e.GetPosition(p.Viewer) - p.At;
        if (!_panMoved && moved.Length < PanSlop) return;
        if (!_panMoved) Mouse.OverrideCursor = Cursors.ScrollAll;
        _panMoved = true;
        p.Viewer.ScrollToHorizontalOffset(p.Left - moved.X);   // the pointer's whole distance from the press: no drift
        p.Viewer.ScrollToVerticalOffset(p.Top - moved.Y);
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
