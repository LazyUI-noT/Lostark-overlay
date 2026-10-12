using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;


/// <summary>Draws one player's overlay (plate, boxes, sweeps, timers, gauge; a party member's also emblem and name) in one OnRender.
/// Reads only the snapshots and <see cref="Settings"/>; slot times are on <see cref="Clock"/>. See OVERLAY_DESIGN.md.</summary>
public sealed partial class OverlayRenderer : FrameworkElement
{
    // ── Design constants (DIPs at scale 1.0 unless noted) ────────────────────────────
    const double Box = FreeLayout.Box;     // skill box edge
    const double Gap = FreeLayout.Gap;     // between boxes, same at every scale
    const double Pad = FreeLayout.PlatePad, PlateRadius = 4, BoxRadius = 2;
    // Margin around the plate inside the window (edit-mode chrome sits in it).
    public const double Bleed = 10;
    public const double MinScale = ClassProfiles.MinScale, MaxScale = ClassProfiles.MaxScale;   // Width/Height settings (box width, height)
    public const double MinOpacity = 0.1;   // floor for the plate, box and gauge opacity settings
    const double ChangedShare = FreeLayout.ChangedShare;   // 초각기 표시 box height, share of a box
    const double PeerShare = 0.6;          // peer box size vs yours
    const double MinIconScale = 0.5, MaxIconScale = 2.0;   // 파티 → 아이콘 크기
    const double CooldownLevel = CooldownLook.CooldownLevel, SweepLevel = CooldownLook.SweepLevel;
    const double LineWidth = 2;            // white progress line of a stack skill with a use left
    const double MaxCooldownSweep = 0.99;  // sweep holds here until Ready arrives
    const double ChangedOffLevel = 0.20;   // 초각기 표시 off
    const double SideBuffWidth = 6;       // an upright buff bar's width at box size 1.0 (버프 막대 위치 왼쪽 바깥)
    const double KeyShare = 0.45;   // the key letter on a 아덴 block: its top-left corner, this share of each side
    const double PulsePeriod = 1.2;   // 쿨 돈 동안 계속 빛남: seconds per pulse, the gauge glow's default
    const double GlowAlpha = 0.55;          // gauge glow: white over the fill at its brightest
    const int EndFlashSweeps = 3;           // 마지막 1초 깜빡임: sweeps from EndFlashFrom to 0
    const double EndFlashFrom = 0.8;        // below this many seconds left (shown 0.7s and down)
    const double ReadyOutline = 2;          // 계속 빛남 테두리: its width, DIP
    const int MaxGlowPens = 16;             // 테두리 pens kept (4 groups in use; the rest are colours tried and dropped)
    const double ShineMs = 220, ShineAlpha = 0.6;
    const double ShineDelayMs = 0;         // the flash starts with Ready; a re-cast still cancels it (a.ShineAt = -1 above)
    const double FadeMs = 120;             // 변화 fade, gauge, reduction catch-up, reduced-motion shine
    const double ColorFadeMs = 400;        // a gauge section changing colour (바드: gold → blue once full)
    const double CatchUpMinJumpFraction = 0.01;   // share of Total; smaller EndsAt corrections are re-locks, not reductions
    public const double MinTimerFont = 6, TimerFontStep = 0.5;   // below this the timer is hidden (too small to read)
    const double TimerInset = 4;           // "88" must fit the box width minus this
    const double PeerNameShare = 0.55, MinNameFont = 8;   // names stay readable at small scales
    const double FastFrameMs = 1000.0 / 60, SlowFrameMs = 1000.0 / 30;   // 60 fps for shine/tweens, 30 for sweeps only
    const int ChangedKey = (int)Part.변화;  // box key of 초각기 표시 (0–11 are SlotId)
    const int GaugeKey = (int)Part.게이지;  // the gauge, as a plate's member
    const double HiddenOpacity = 0.25;     // a box hidden on cooldown, where it is shown anyway (ShowHidden)
    static readonly Color PlateColor = Color.FromArgb(0xD9, 0x0C, 0x0D, 0x10);   // the swatch; windows use their setting
    static readonly MineProfile NoEffects = new();
    static readonly Color IconColor = Color.FromRgb(0x2A, 0x2D, 0x33);   // box behind the class number when there is no emblem
    static readonly Color WarningRed = Color.FromRgb(0xE5, 0x22, 0x22);
    const double WarningPeriodMs = 600;   // the red gauge's flicker: on for half, faint for half
    static readonly Color TextColor = Colors.White;
    static readonly Color TextOutline = Color.FromArgb(0xE6, 0x00, 0x00, 0x00);
    static readonly Color EstimatedOutline = Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF);
    static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    // ─────────────────────────────────────────────────────────────────────────────────

    static readonly Brush TextBrush = NewSolid(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    static readonly Pen DottedPen = Frozen(new Pen(new SolidColorBrush(EstimatedOutline), 1) { DashStyle = new DashStyle([2, 2], 0) });
    static readonly Pen OutlinePen = Frozen(new Pen(new SolidColorBrush(TextOutline), 2) { LineJoin = PenLineJoin.Round });
    static readonly Pen LightOutlinePen = Frozen(new Pen(Brushes.White, 2) { LineJoin = PenLineJoin.Round });
    const double LightFill = 50;   // CIE L*: a 쿨 돈 뒤 지난 시간 colour this light is outlined in black
    static readonly Pen GlowPen = Frozen(new Pen(Brushes.White, 1));
    const double GoldEdge = 1;   // dark line inside the gold frame
    static readonly Pen GoldEdgePen = Frozen(new Pen(new SolidColorBrush(TextOutline), GoldEdge));

    /// <summary>The party slot drawn (0 = "party member 1"), or -1 for yourself. Set before the first Update.</summary>
    public int Peer { get; set; } = -1;
    /// <summary>The buff bars' own window (버프 막대 따로 두기): yours, only the bars.</summary>
    public bool BuffsOnly { get; set; }
    /// <summary>Your gauge flickers red: the 초각스 icon shows another class than the one set, so the gauge read and its
    /// predictions (게이지 읽기: 예측) are another class's (CharacterService.ClassMismatch).</summary>
    public bool ClassWarning { get => _classWarning; set { _classWarning = value; InvalidateVisual(); } }
    bool _classWarning;

    /// <summary>Boxes that hide on cooldown (쿨타임 중 숨기기) are drawn faint instead, so they can be seen and picked:
    /// the layout editor, edit mode and sample previews.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>The settings object the user edits; read on every frame.</summary>
    public Settings Settings { get; set; } = new();

    bool _loop, _moving, _animating;   // loop attached; this frame had a moving sweep / a shine or tween
    double _frameMs = SlowFrameMs;
    Player? _player;
    TimeSpan? _frozenAt;
    PlayerLayout? _layout;
    readonly Dictionary<(string, int), BoxAnim> _boxes = [];
    readonly Dictionary<string, Tween> _gauges = [], _changed = [];
    readonly Dictionary<string, ColorFade> _barColors = [];
    readonly Dictionary<string, Color> _colors = [];
    readonly Dictionary<uint, SolidColorBrush> _brushes = [];
    readonly Dictionary<(string, double), Geometry> _texts = [];
    readonly Dictionary<(string, double), FormattedText> _names = [];
    readonly Dictionary<Rect, Geometry> _clips = [];
    readonly Dictionary<(double, bool), Brush> _hatches = [];
    readonly Dictionary<double, Pen> _linePens = [];
    readonly Dictionary<Color, Pen> _glowPens = [];   // 계속 빛남 테두리, per colour
    double _lastFrame;

    readonly Dictionary<SlotId, ImageSource> _icons = [], _targetIcons = [];

    static BitmapSource ToBitmap(Image icon)
    {
        var bmp = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Bgr32, null, icon.Bgra, icon.Stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>아이콘으로 표시: the skill's icon as the skill bar last showed it ready (Engine.IconSeen).</summary>
    public void SetIcon(SlotId slot, Image icon)
    {
        _icons[slot] = ToBitmap(icon);
        InvalidateVisual();
    }

    /// <summary>Your boxes drawn as another 직업각인 than the active character's (설정's 편집 중 pick): its slots' groups,
    /// colours, effects and gauge, with <paramref name="icons"/> (its saved ones) in place of the character's. Null = the
    /// active character. Only yours (not the buff or debuff windows or a party member).</summary>
    public void SetTarget(PreviewTarget? target, IReadOnlyDictionary<SlotId, Image>? icons = null)
    {
        Target = target;
        _targetIcons.Clear();
        foreach (var (slot, icon) in icons ?? new Dictionary<SlotId, Image>()) _targetIcons[slot] = ToBitmap(icon);
        Refresh();
    }

    public PreviewTarget? Target { get; private set; }

    /// <summary>Another character: its icons aren't known until each slot is read ready again.</summary>
    public void ClearIcons()
    {
        _icons.Clear();
        InvalidateVisual();
    }

    public OverlayRenderer()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        // Nothing is drawn while hidden, so per-box state goes stale; starting fresh avoids a burst of shines on return.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { SetLoop(false); return; }
            _boxes.Clear(); _gauges.Clear(); _changed.Clear(); _barColors.Clear();
            InvalidateVisual();
        };
    }

    /// <summary>Replaces what is drawn: yourself or the party member in slot <see cref="Peer"/>, or nothing.
    /// <paramref name="frozenAt"/> pins slot time (sample data in edit mode).</summary>
    public void Update(Player? player, TimeSpan? frozenAt = null)
    {
        if ((frozenAt is null) != (_frozenAt is null)) { _boxes.Clear(); _gauges.Clear(); _changed.Clear(); _barColors.Clear(); }
        _player = player;
        _frozenAt = frozenAt;
        Refresh();
    }

    /// <summary>Call after <see cref="Settings"/> changed.</summary>
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>A ready box and a 40%-elapsed cooldown box in <paramref name="color"/>, for the settings swatch.</summary>
    public static DrawingImage Swatch(Color color)
    {
        var d = new DrawingGroup();
        using (var dc = d.Open())
        {
            dc.DrawRoundedRectangle(NewSolid(PlateColor), null, new Rect(0, 0, 64, 34), PlateRadius, PlateRadius);
            var a = new Rect(4, 4, 26, 26);
            var b = new Rect(34, 4, 26, 26);
            dc.DrawRoundedRectangle(NewSolid(color), null, a, BoxRadius, BoxRadius);
            dc.DrawRoundedRectangle(NewSolid(Level(color, CooldownLevel)), null, b, BoxRadius, BoxRadius);
            dc.PushClip(new RectangleGeometry(b, BoxRadius, BoxRadius));
            dc.DrawGeometry(NewSolid(Level(color, SweepLevel)), null, Pie(b, 0.4));
            dc.Pop();
        }
        var img = new DrawingImage(d);
        img.Freeze();
        return img;
    }

    // ── Layout ───────────────────────────────────────────────────────────────────────

    sealed class PlayerLayout(Player player, string key, double unit)
    {
        public Player Player = player;
        public string Key = key;
        public double Unit = unit;
        public Rect Plate;                  // around everything drawn
        public List<(Rect Plate, int[] Keys)>? Split;   // yours, skills placed apart: a plate per group of boxes
        public Rect? Gauge;
        public GaugeBar? Bar;               // yours: splits, segment colours and glow (peers: the plain bar)
        public Appearance? Look;            // yours: the chosen cooldown colours (peers: dimmed automatically)
        public double GridTop;              // top of the boxes (below the gauge)
        public Point Anchor;                // kept still on screen when the layout changes (OverlayWindow.Refresh)
        public Point CellOrigin;            // where cell (0, 0) is drawn
        public (double X, double Y) Scale;  // DIP per cell unit
        public bool Free;                   // laid out from cells (FreeLayout), not the fixed grid
        public bool Peer;
        public string? Name, Number;
        public Point NameAt, NumberAt;
        public Rect? Icon;
        public readonly List<(int Key, Rect Rect, Color Color)> Boxes = [];
    }

    protected override Size MeasureOverride(Size available)
    {
        _clips.Clear();
        if (DebuffsOnly) return DebuffPanelSize();
        _layout = BuildLayout();
        return _layout is { } l ? new Size(l.Plate.Right + Bleed, l.Plate.Bottom + Bleed) : new Size(0, 0);
    }

    PlayerLayout? BuildLayout()
    {
        if (_player is not { } p) return null;
        if (Peer < 0 && BuffsOnly)
        {
            // 버프 막대 따로 두기: the fixed grid with nothing but the bars, at this window's own size.
            var bars = ClassProfiles.Mine(Settings) with { Groups = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => false), Gauge = false, Layout = null };
            return LayoutPlayer(p, "buffs", BoxSize(Settings.Buffs.Width), BoxSize(Settings.Buffs.Height), 0, Bleed, bars, leadH: 0);
        }
        if (Peer < 0)
        {
            var mine = Target is { } t ? ClassProfiles.Mine(Settings, t.Key) : ClassProfiles.Mine(Settings);
            var own = LayoutPlayer(p, "", BoxSize(mine.Width), BoxSize(mine.Height), 0, Bleed, mine, leadH: 0);
            if (own is not null) own.Look = mine;
            var (barClass, barSub) = Target is { } picked ? (picked.Class, picked.Subclass)
                : Settings.ActiveCharacter is { } k && Settings.Characters.TryGetValue(k, out var c) ? (c.Class, c.Subclass) : default;
            if (own is not null && barClass is not null)
                own.Bar = GaugeBar.Resolve(Settings, barClass, barSub!, Emblems.Class(p.Snapshot.ClassId)?.Subclasses.GetValueOrDefault(barSub!));
            return own;
        }

        // What of the member to show, where, in what colours and at what size follows their class (ClassProfiles).
        var party = Settings.Party;
        var cls = Emblems.Class(p.Snapshot.ClassId);
        var look = ClassProfiles.Member(Settings, cls?.Name, cls?.Role ?? Role.딜러, p.Look, cls);
        double bw = BoxSize(look.Width, PeerShare, ClassProfiles.MinFitScale), bh = BoxSize(look.Height, PeerShare, ClassProfiles.MinFitScale);
        double font = NameFont(bh);
        // Left of the boxes, level with them (below any gauge): the slot number (파티원 순서), then the class emblem
        // (Emblems) with the name centred under it, or either alone. With both, the emblem gives up the name's line of
        // the grid height (never shrinking below one line) before 아이콘 크기 scales it, and the plate grows if the two
        // still don't fit. A name alone is centred vertically.
        bool withIcon = party.Label != PeerLabel.Name, withName = party.Label != PeerLabel.Icon;
        double colH = 2 * bh + Gap;
        string name = PlayerName.Clip(p.Name, party.NameLength);
        string slotLabel = PartySlots.Label(Peer, party.MyNumber);
        var number = NameText(slotLabel, font);
        double numberW = Math.Ceiling(number.Width);
        double nameW = withName ? Math.Ceiling(NameText(name, font).Width) : 0;
        double nameH = withName ? Math.Ceiling(NameText("가", font).Height) : 0;
        double iconScale = Math.Clamp(party.IconScale, MinIconScale, MaxIconScale);
        double iconW = !withIcon ? 0 : Math.Round((withName ? Math.Max(nameH, colH - nameH - Gap) : colH) * iconScale);
        double leadW = Math.Max(iconW, nameW);
        double leadH = withIcon ? iconW + (withName ? Gap + nameH : 0) : 0;
        var l = LayoutPlayer(p, "peer:" + p.Name, bw, bh, numberW + Gap + leadW + Gap * 2, Bleed, look, leadH);
        if (l is null) return null;
        l.Peer = true;
        double left = l.Plate.X + Pad;
        l.Number = slotLabel;
        l.NumberAt = new Point(left, l.GridTop + (colH - number.Height) / 2);
        left += numberW + Gap;
        if (withIcon) l.Icon = new Rect(left + (leadW - iconW) / 2, l.GridTop, iconW, iconW);
        if (withName)
        {
            var text = NameText(name, font);
            l.Name = name;
            l.NameAt = withIcon
                ? new Point(left + (leadW - text.Width) / 2, l.GridTop + iconW + Gap)
                : new Point(left, l.GridTop + (colH - text.Height) / 2);
        }
        return l;
    }

    /// <summary>Width/Height settings after dragging the corner handle by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// from a plate of <paramref name="plate"/> DIPs drawn at <paramref name="start"/>: each scales with its axis.</summary>
    public static (double W, double H) Resized((double W, double H) start, (double W, double H) plate, double dx, double dy) =>
        (Math.Round(Math.Clamp(start.W * (plate.W + dx) / plate.W, MinScale, MaxScale), 2),
         Math.Round(Math.Clamp(start.H * (plate.H + dy) / plate.H, MinScale, MaxScale), 2));

    /// <summary>A box edge in DIPs for a Width/Height setting.</summary>
    /// <param name="min">Lowest scale drawn: a party member's received size or layout may be fitted below the setting's.</param>
    static double BoxSize(double setting, double share = 1, double min = MinScale) => Math.Round(Box * Math.Clamp(setting, min, MaxScale) * share);

    /// <param name="a">What is shown, where and in what colours (<see cref="ClassProfiles"/>), decided once per layout.</param>
    /// <param name="leadH">Height of what stands left of the boxes, from the grid top; the plate grows to fit it.</param>
    PlayerLayout? LayoutPlayer(Player p, string key, double bw, double bh, double lead, double top, Appearance a, double leadH)
    {
        var s = p.Snapshot;
        bool Show(SlotGroup g) => s.Groups.Contains(g) && a.Groups.GetValueOrDefault(g, true);
        var colors = Enum.GetValues<SlotGroup>().ToDictionary(g => g, g => Parse(a.Colors[g]));
        Color Col(SlotGroup g) => colors[g];
        Color SkillCol(SlotId id) => a.SlotColors?.GetValueOrDefault(id) is { } own ? Parse(own) : colors[SlotGroup.일반스킬];   // a 일반스킬's own colour
        // The gauge's thickness follows the box height, as it did the one size.
        double gaugeH = a.Gauge && s.Gauge is not null ? Math.Max(2, Math.Round(Settings.Gauge.Thickness * bh / Box)) : 0;
        if (a.Layout is { } cells) return LayoutCells(p, key, cells, a.Hidden, bw, bh, lead, top, Show, Col, SkillCol, gaugeH, leadH);
        var hidden = a.Hidden;

        var l = new PlayerLayout(p, key, bh);
        // 버프 막대 위치 왼쪽 바깥: the bars take a strip left of the plate, which starts after it.
        var buffs = ShownBuffs(s);
        bool side = Settings.Buffs.Side && buffs.Count > 0;
        // Each bar's own 두께 and 길이 (BuffIndicator), clamped as the settings window offers them.
        double Thick(BuffKind k) => BarSizes.Thickness(Settings.Buffs.Of(k));
        double Long(BuffKind k) => BarSizes.Length(Settings.Buffs.Of(k));
        double SideW(BuffKind k) => Math.Max(2, Math.Round(SideBuffWidth * bw / Box * Thick(k)));
        double plateX = Bleed + (side ? buffs.Sum(k => SideW(k) + Gap) : 0);
        double x0 = plateX + Pad + lead, x = x0;
        double gridTop = top + Pad + (gaugeH > 0 ? gaugeH + Gap : 0);
        l.GridTop = gridTop;
        l.Anchor = new Point(x0, gridTop);
        l.CellOrigin = new Point(x0, top + Pad);
        l.Scale = (bw / Box, bh / Box);
        double colH = 2 * bh + Gap;
        void Add(SlotId id, Rect r, Color c) { if (!hidden.Contains(id)) l.Boxes.Add(((int)id, r, c)); }

        if (Show(SlotGroup.각성기))
        {
            double ch = Math.Round(bh * ChangedShare);
            l.Boxes.Add((ChangedKey, new Rect(x, gridTop, bw, ch), Parse(Settings.Overlay.AwakeningChangedColor)));
            Add(SlotId.각성기, new Rect(x, gridTop + ch + Gap, bw, colH - ch - Gap), Col(SlotGroup.각성기));
            x += bw + Gap;
        }
        if (Show(SlotGroup.초각스))
        {
            Add(SlotId.초각스, new Rect(x, gridTop, bw, colH), Col(SlotGroup.초각스));
            x += bw + Gap;
        }
        if (Show(SlotGroup.일반스킬))
        {
            for (int i = 0; i < 8; i++)
                Add(SlotId.일반1 + i, new Rect(x + i % 4 * (bw + Gap), gridTop + i / 4 * (bh + Gap), bw, bh), SkillCol(SlotId.일반1 + i));
            x += 4 * (bw + Gap);
        }
        var aden = new[] { SlotId.아덴1, SlotId.아덴2 }.Where(p.Snapshot.AdenSlots.Contains).ToList();
        if (Show(SlotGroup.아덴스킬) && aden.Count > 0)
        {
            double h = Math.Round((colH - (aden.Count - 1) * Gap) / aden.Count);
            for (int i = 0; i < aden.Count; i++)
                Add(aden[i], new Rect(x, gridTop + i * (h + Gap), bw, i == aden.Count - 1 ? colH - i * (h + Gap) : h), Col(SlotGroup.아덴스킬));
            x += bw + Gap;
        }

        bool anyColumn = x > x0;
        if (!anyColumn && gaugeH == 0 && buffs.Count == 0) return null;
        // Upright bars alone (the buffs' own window, 세로) take no grid width; flat ones get a grid's width.
        double right = anyColumn ? x - Gap : side && gaugeH == 0 ? plateX - Pad : x0 + 4 * bw;
        if (gaugeH > 0) l.Gauge = new Rect(x0, top + Pad, right - x0, gaugeH);
        double bottom = anyColumn ? gridTop + Math.Max(colH, leadH) : top + Pad + gaugeH;
        Color BuffColor(BuffKind k) => Parse(Settings.Buffs.Of(k).Color);
        if (side)
        {
            // Upright, as tall as the gauge and grid (at least a grid's height, when nothing else is shown), 공증 nearest
            // the plate (buff_example2: 뎀증 초각 아덴 공증).
            double tall = Math.Max(bottom - top - Pad, colH);
            bottom = top + Pad + tall;
            double bx = plateX;
            foreach (var k in buffs)   // a shorter bar stands on the bottom edge
            {
                double w = SideW(k), h = Math.Max(2, Math.Round(tall * Long(k)));
                bx -= w + Gap;
                l.Boxes.Add(((int)(FreeLayout.FirstBuff + (int)k), new Rect(bx, top + Pad + tall - h, w, h), BuffColor(k)));
            }
        }
        else
        {
            // The buff bars under the grid, two to a row, each at most half its width (from the left); in the buffs' own
            // window (가로) one to a row, top to bottom. A row is as tall as its thicker bar. Closing up like the columns.
            int perRow = BuffsOnly ? 1 : 2;
            double full = (right - x0 - (perRow - 1) * Gap) / perRow, barH = FreeLayout.BuffHeight * bh / Box, y = bottom + Gap;
            for (int i = 0; i < buffs.Count; i += perRow)
            {
                var row = buffs.Skip(i).Take(perRow).ToList();
                double rowH = row.Max(k => Math.Max(2, Math.Round(barH * Thick(k))));
                for (int j = 0; j < row.Count; j++)
                    l.Boxes.Add(((int)(FreeLayout.FirstBuff + (int)row[j]), new Rect(x0 + j * (full + Gap), y,
                        Math.Max(2, Math.Round(full * Long(row[j]))), Math.Max(2, Math.Round(barH * Thick(row[j])))), BuffColor(row[j])));
                y += rowH + Gap;
            }
            bottom = y - Gap;
        }
        l.Plate = new Rect(plateX, top, right + Pad - plateX, bottom + Pad - top);
        return l;
    }

    /// <summary>The free layout: each shown part at its cell (a part the layout lacks at its fixed-grid spot), scaled by
    /// the box size. Nothing closes up when a group is off. The plate wraps what is shown; the gauge keeps its bottom
    /// edge at its cell's, so a thicker gauge grows upwards.</summary>
    PlayerLayout? LayoutCells(Player p, string key, IReadOnlyDictionary<Part, Cell> cells, IReadOnlySet<SlotId> hidden, double bw,
        double bh, double lead, double top, Func<SlotGroup, bool> show, Func<SlotGroup, Color> col, Func<SlotId, Color> skillCol, double gaugeH, double leadH)
    {
        var s = p.Snapshot;
        var fallback = FreeLayout.Default(Settings.Gauge.Thickness);
        (double X, double Y) scale = (bw / Box, bh / Box);
        Rect At(Part part)
        {
            var c = cells.TryGetValue(part, out var v) ? v : fallback[part];
            if (part >= FreeLayout.FirstBuff && Settings.Buffs.Of((BuffKind)(part - FreeLayout.FirstBuff)) is var bar)
                c = BarSizes.Sized(c, BarSizes.Length(bar), BarSizes.Thickness(bar));   // 길이 and 두께 apply here too
            double x = Math.Round(c.X * scale.X), y = Math.Round(c.Y * scale.Y);
            return new Rect(x, y, Math.Max(1, Math.Round(c.Right * scale.X) - x), Math.Max(1, Math.Round(c.Bottom * scale.Y) - y));
        }
        var boxes = new List<(int Key, Rect Rect, Color Color)>();
        void Add(Part part, Color c) { if (part is Part.변화 or >= FreeLayout.FirstBuff || !hidden.Contains((SlotId)part)) boxes.Add(((int)part, At(part), c)); }
        if (show(SlotGroup.각성기))
        {
            Add(Part.변화, Parse(Settings.Overlay.AwakeningChangedColor));
            Add(Part.각성기, col(SlotGroup.각성기));
        }
        if (show(SlotGroup.초각스)) Add(Part.초각스, col(SlotGroup.초각스));
        if (show(SlotGroup.일반스킬))
            for (int i = 0; i < 8; i++) Add(Part.일반1 + i, skillCol(SlotId.일반1 + i));
        if (show(SlotGroup.아덴스킬))
            foreach (var id in new[] { SlotId.아덴1, SlotId.아덴2 }.Where(s.AdenSlots.Contains)) Add((Part)id, col(SlotGroup.아덴스킬));
        foreach (var k in ShownBuffs(s)) Add(FreeLayout.FirstBuff + (int)k, Parse(Settings.Buffs.Of(k).Color));
        Rect? gauge = gaugeH > 0 && At(Part.게이지) is var g ? new Rect(g.X, g.Bottom - gaugeH, g.Width, gaugeH) : null;
        if (boxes.Count == 0 && gauge is null) return null;

        var all = boxes.Select(b => b.Rect).Concat(gauge is { } gr ? [gr] : []).ToList();
        double ox = Bleed + Pad + lead - all.Min(r => r.X), oy = top + Pad - all.Min(r => r.Y);
        Rect Placed(Rect r) => new(r.X + ox, r.Y + oy, r.Width, r.Height);
        var l = new PlayerLayout(p, key, bh) { Anchor = new Point(ox, oy), CellOrigin = new Point(ox, oy), Scale = scale, Free = true };
        foreach (var (k, r, c) in boxes) l.Boxes.Add((k, Placed(r), c));
        if (gauge is { } gg) l.Gauge = Placed(gg);
        l.GridTop = boxes.Count > 0 ? l.Boxes.Min(b => b.Rect.Y) : l.Gauge!.Value.Bottom;
        double right = all.Max(r => r.Right) + ox, bottom = Math.Max(all.Max(r => r.Bottom) + oy, l.GridTop + leadH);
        l.Plate = new Rect(Bleed, top, right + Pad - Bleed, bottom + Pad - top);
        if (Peer < 0 && FreeLayout.Spread(cells))
        {
            // Skills placed apart stand on plates of their own; the gaps between them stay clear. Only once boxes were
            // moved apart: a group turned off in a compact layout keeps the one plate.
            var parts = l.Boxes.Select(b => (b.Key, b.Rect)).Concat(l.Gauge is { } pg ? [(GaugeKey, pg)] : []).ToList();
            var plates = FreeLayout.Plates([.. parts.Select(b => new Cell(b.Rect.X, b.Rect.Y, b.Rect.Width, b.Rect.Height))], Pad);
            if (plates.Count > 1)
                l.Split = [.. plates.Select(g => (new Rect(g.Plate.X, g.Plate.Y, g.Plate.W, g.Plate.H), g.Members.Select(i => parts[i].Key).ToArray()))];
        }
        return l;
    }

    /// <summary>The buff bars to draw: yours only, while the buff row is read, the ones whose 표시 is on; on the plate,
    /// or in the buffs' own window (<see cref="BuffsOnly"/>) with 버프 막대 따로 두기.</summary>
    List<BuffKind> ShownBuffs(Snapshot s) => Peer >= 0 || s.Buffs.IsDefaultOrEmpty || Settings.Buffs.Detached != BuffsOnly ? []
        : [.. Enum.GetValues<BuffKind>().Where(k => Settings.Buffs.Of(k).Visible)];

    /// <summary>The point the window keeps still on screen when the layout changes (NaN when nothing is laid out): the
    /// top of the fixed grid, so a thicker gauge grows upwards, or where the free layout's cell (0, 0) is drawn, so
    /// dragging a box past the others' edge doesn't move them.</summary>
    public Point Anchor => _layout?.Anchor ?? new Point(double.NaN, double.NaN);

    /// <summary>True when drawn from a free layout; <see cref="Anchor"/> means a different point in each kind.</summary>
    public bool IsFree => _layout?.Free == true;

    /// <summary>The part drawn at <paramref name="at"/> (this element's coordinates) and its rectangle, or null.</summary>
    public (Part Part, Rect Rect)? PartAt(Point at)
    {
        if (_layout is not { } l) return null;
        for (int i = l.Boxes.Count - 1; i >= 0; i--)
            if (l.Boxes[i].Rect.Contains(at)) return ((Part)l.Boxes[i].Key, l.Boxes[i].Rect);
        return l.Gauge is { } g && g.Contains(at) ? (Part.게이지, g) : null;
    }

    /// <summary>Where <paramref name="part"/> is drawn (this element's coordinates), or null when it isn't.</summary>
    public Rect? PartRect(Part part)
    {
        if (_layout is not { } l) return null;
        if (part == Part.게이지) return l.Gauge;
        foreach (var b in l.Boxes) if ((Part)b.Key == part) return b.Rect;
        return null;
    }

    /// <summary>The parts drawn now (the boxes a dragged one snaps to).</summary>
    public IEnumerable<Part> DrawnParts => _layout is not { } l ? []
        : l.Boxes.Select(b => (Part)b.Key).Concat(l.Gauge is null ? [] : [Part.게이지]);

    /// <summary>The drawn player's class (a party member's decides their boxes' settings), or null when unknown.</summary>
    public string? ClassName => _player is { } p ? Emblems.Class(p.Snapshot.ClassId)?.Name : null;

    /// <summary>A party member drawn with the layout they sent (파티원 배치 받기), not their class's.</summary>
    public bool ShowsReceivedLayout => Peer >= 0 && Settings.Party.ReceiveLayout && _player?.Look?.Layout is { } sent && !FreeLayout.Spread(sent);

    /// <summary>DIP per cell unit along each axis (the box size setting, as drawn).</summary>
    public (double X, double Y) CellScale => _layout?.Scale ?? (1, 1);

    /// <summary>What is on screen as a free layout: each drawn part's cell, and the fixed-grid spot of the rest. Turning
    /// the fixed grid into a free layout with this leaves every shown box where it was.</summary>
    public Dictionary<Part, Cell> CellsAsDrawn()
    {
        var cells = FreeLayout.Default(Settings.Gauge.Thickness);
        if (_layout is not { } l) return cells;
        var (sx, sy) = l.Scale;
        Cell ToCell(Rect r) => new((r.X - l.CellOrigin.X) / sx, (r.Y - l.CellOrigin.Y) / sy, r.Width / sx, r.Height / sy);
        foreach (var (k, r, _) in l.Boxes)
            cells[(Part)k] = (Part)k >= FreeLayout.FirstBuff && Settings.Buffs.Of((BuffKind)(k - (int)FreeLayout.FirstBuff)) is var bar
                ? BarSizes.Unsized(ToCell(r), BarSizes.Length(bar), BarSizes.Thickness(bar), Settings.Buffs.Side)   // drawn at 길이 × 두께
                : ToCell(r);
        if (l.Gauge is { } g) cells[Part.게이지] = ToCell(g);
        return cells;
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        TimeSpan now = _frozenAt ?? Clock.Now();
        double ms = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        bool reduced = !SystemParameters.ClientAreaAnimation;
        _moving = _animating = false;

        if (DebuffsOnly) DrawDebuffs(dc, now, ms, reduced);
        else if (_layout is { } l) DrawPlayer(dc, l, now, ms, reduced);
        _frameMs = _animating ? FastFrameMs : SlowFrameMs;
        SetLoop(_moving || _animating);
    }

    void DrawPlayer(DrawingContext dc, PlayerLayout l, TimeSpan now, double ms, bool reduced)
    {
        var s = l.Player.Snapshot;
        var hidden = HiddenBoxes(l, now);
        // Plate, gauge and boxes each have their own opacity setting. The detached buff window has no plate.
        if (Settings.Overlay.ShowPlate && !BuffsOnly)
        {
            var rgb = Parse(l.Peer ? Settings.Party.PlateColor : Settings.Overlay.PlateColor);
            var plate = Solid(Color.FromArgb((byte)(PlateColor.A * Math.Clamp(Settings.Overlay.Opacity, MinOpacity, 1)), rgb.R, rgb.G, rgb.B));
            if (l.Split is null)
            {
                if (ShowHidden || hidden.Count == 0 || l.Gauge is not null || !l.Boxes.All(b => hidden.Contains(b.Key)))   // every box hid: the plate too
                    dc.DrawRoundedRectangle(plate, null, l.Plate, PlateRadius, PlateRadius);
            }
            else
                foreach (var (r, keys) in l.Split)
                    if (ShowHidden || !keys.All(hidden.Contains))   // a plate whose boxes all hid goes with them
                        dc.DrawRoundedRectangle(plate, null, r, PlateRadius, PlateRadius);
        }

        if (l.Icon is { } icon)
        {
            if (Emblems.For(s.ClassId) is { } emblem)
                dc.DrawImage(emblem, icon);
            else
            {
                // No emblem (class not set, or no file for it): the class number in a box.
                dc.DrawRoundedRectangle(Solid(IconColor), null, icon, BoxRadius, BoxRadius);
                var number = NameText(s.ClassId > 0 ? s.ClassId.ToString(CultureInfo.InvariantCulture) : "?", NameFont(l.Unit));
                dc.DrawText(number, new Point(icon.X + (icon.Width - number.Width) / 2, icon.Y + (icon.Height - number.Height) / 2));
            }
        }
        if (l.Name is { } name)
            dc.DrawText(NameText(name, NameFont(l.Unit)), l.NameAt);
        if (l.Number is { } slotLabel)
            dc.DrawText(NameText(slotLabel, NameFont(l.Unit)), l.NumberAt);

        if (l.Gauge is { } g && s.Gauge is { } value)
        {
            double v = Get(_gauges, l.Key).Value(Math.Clamp(value, 0, 1), ms, ref _animating);
            dc.PushOpacity(Math.Clamp(Settings.Gauge.Opacity, MinOpacity, 1));
            dc.DrawRectangle(Solid(Parse(Settings.Gauge.Empty)), null, g);
            if (l.Bar is { } bar) DrawBar(dc, g, l.Key, bar, s, v, ms, reduced);
            else if (v > 0) dc.DrawRectangle(Solid(Parse(Settings.Gauge.Fill)), null, new Rect(g.X, g.Y, g.Width * v, g.Height));
            dc.Pop();
        }
        if (l.Gauge is { } warn && ClassWarning && !l.Peer)
        {
            if (!reduced && _frozenAt is null) _moving = true;
            dc.PushOpacity(reduced ? 0.7 : ms % WarningPeriodMs < WarningPeriodMs / 2 ? 0.9 : 0.25);
            dc.DrawRectangle(Solid(WarningRed), null, warn);
            dc.Pop();
        }

        dc.PushOpacity(Math.Clamp(Settings.Overlay.BoxOpacity, MinOpacity, 1));
        foreach (var (key, r, c) in l.Boxes)
        {
            if (key >= (int)FreeLayout.FirstBuff)
            {
                var kind = (BuffKind)(key - (int)FreeLayout.FirstBuff);
                DrawBuff(dc, r, c, s.Buffs.IsDefaultOrEmpty ? null : s.Buffs[(int)kind], Settings.Buffs.Of(kind).ShowLabel,
                    Settings.Buffs.EndFlash(kind), now, reduced);
                continue;
            }
            if (key == ChangedKey)
            {
                double level = Get(_changed, l.Key).Value(s.AwakeningChanged ? 1 : ChangedOffLevel, ms, ref _animating);
                dc.DrawRoundedRectangle(Solid(Level(c, level)), null, r, BoxRadius, BoxRadius);
                continue;
            }
            if (!_boxes.TryGetValue((l.Key, key), out var anim)) _boxes[(l.Key, key)] = anim = new BoxAnim();
            var group = SlotInfo.GroupOf((SlotId)key, s.AdenSlots);
            // Your boxes' effects are the class's (MineProfile); an 일반스킬 may have its own flash and pulse.
            var slot = (SlotId)key;
            var fx = l.Look?.Effects ?? NoEffects;   // a peer's boxes have none
            var skillIcon = !l.Peer && fx.ShowsIcon(group) ? (Target is null ? _icons : _targetIcons).GetValueOrDefault(slot) : null;   // none seen yet: colour
            Color? dim = l.Look?.DimOf(slot, group) is { } d ? Parse(d) : null;
            // 쿨타임 중 숨기기: drawn nowhere (its timing state still steps), or faint where hidden boxes are shown.
            bool gone = hidden.Contains(key);
            var sink = gone && !ShowHidden ? new DrawingGroup().Open() : null;
            if (gone && sink is null) dc.PushOpacity(HiddenOpacity);
            DrawBox(sink ?? dc, r, c, dim, s[slot], anim, now, ms, reduced, l, !l.Peer && fx.Shines(slot, group), !l.Peer && fx.Pulses(slot, group),
                fx.PulseStyleOf(group), Parse(l.Look?.GlowOf(slot, group) ?? "#FFFFFF"), !l.Peer && fx.EndFlashes(slot, group), skillIcon,
                !l.Peer && fx.Counts(slot, group));
            if (sink is not null) { sink.Close(); continue; }
            // The key as bound in the 키 tab: on each of two 아덴 blocks (바드 Z and X/C), and on any box of yours picked in
            // 키 표시. A peer's keys aren't known.
            if (!l.Peer && (group == SlotGroup.아덴스킬 && s.AdenSlots.Length > 1 || fx.KeyLetters.Contains(slot)))
                DrawOutlined(dc, new Rect(r.X, r.Y, r.Width * KeyShare, r.Height * KeyShare),
                    Settings.Keys.GetValueOrDefault((SlotId)key, SlotInfo.DefaultKey((SlotId)key)), Math.Floor(r.Height * KeyShare * 0.7));
            if (gone) dc.Pop();
        }
        dc.Pop();
    }

    /// <summary>Steps each skill box's held count (사용 후 3초 유지) for this frame and returns the boxes that hide on
    /// cooldown now (쿨타임 중 숨기기; yours only). Before the plates, which go when all their boxes hid.</summary>
    HashSet<int> HiddenBoxes(PlayerLayout l, TimeSpan now)
    {
        var s = l.Player.Snapshot;
        var fx = l.Peer ? null : l.Look?.Effects;
        var hidden = new HashSet<int>();
        foreach (var (key, _, _) in l.Boxes)
        {
            if (key >= ChangedKey) continue;
            var slot = (SlotId)key;
            var group = SlotInfo.GroupOf(slot, s.AdenSlots);
            if (!_boxes.TryGetValue((l.Key, key), out var a)) _boxes[(l.Key, key)] = a = new BoxAnim();
            a.Held = fx is not null && fx.Counts(slot, group) && fx.Holds(slot, group) ? a.Held.Next(s[slot]) : default;
            if (fx is not null && fx.Hides(slot, group) && CooldownLook.Hides(s[slot], now, a.Held.Shown(now) is not null)) hidden.Add(key);
        }
        return hidden;
    }

    void DrawBox(DrawingContext dc, Rect r, Color c, Color? dim, SlotState st, BoxAnim a, TimeSpan now, double ms, bool reduced, PlayerLayout l, bool shines, bool pulses, ReadyGlowStyle glow, Color glowColor, bool endFlash, ImageSource? icon, bool counts)
    {
        bool peer = l.Peer;   // peer rows: no shine, no catch-up, no timer
        TimeSpan? rem = st.Remaining(now);
        bool ready = st.Phase == SlotPhase.Ready || (st.Phase == SlotPhase.Cooldown && st.Estimated && rem == TimeSpan.Zero);
        bool cooling = !ready && st.Phase is SlotPhase.Cooldown or SlotPhase.LongCooldown or SlotPhase.Unavailable;

        // Shine: armed on the cooldown → ready edge. The box snaps to full brightness at once; the white flash waits until
        // Ready has held ShineDelayMs (a re-cast sooner skips it) and is cancelled the moment the box leaves Ready.
        if (!ready) a.ShineAt = -1;
        else if (a.WasCooling && shines && !peer) a.ShineAt = ms + (reduced ? 0 : ShineDelayMs);
        a.WasCooling = cooling;
        double shineT = a.ShineAt < 0 ? 1 : (ms - a.ShineAt) / (reduced ? FadeMs : ShineMs);
        if (shineT >= 1) a.ShineAt = -1; else _animating = true;

        // A seconds cooldown takes its colours from CooldownLook (a stack skill with a use left stays full colour, one
        // with none left turns grey).
        var look = CooldownLook.For(st, Settings.Overlay.CooldownStyle);
        bool seconds = st.Phase == SlotPhase.Cooldown && !ready;
        double level = seconds ? look.Base : !ready ? CooldownLevel
            : reduced && shineT < 1 ? CooldownLevel + (1 - CooldownLevel) * EaseOut(shineT) : 1;
        if (icon is not null) DrawIcon(dc, BoxClip(r), r, icon, level);
        else
        {
            var baseColor = Shade(c, dim, level);
            dc.DrawRoundedRectangle(Solid(seconds && look.Grey ? Grey(baseColor) : baseColor), null, r, BoxRadius, BoxRadius);
        }
        if (st.Phase == SlotPhase.Unknown)
            dc.DrawRoundedRectangle(HatchFor(l.Unit), null, r, BoxRadius, BoxRadius);

        string? timer = null;
        if (st.Phase == SlotPhase.Cooldown && !ready && rem is { } left)
        {
            // 99% hold: a readable slot turns Ready only when the game confirms it.
            double p = Math.Min(st.Progress(now) ?? 0, MaxCooldownSweep);
            // Reduction: same Total, EndsAt pulled in → catch up over 120 ms, except in the last second.
            if (!peer && a.EndsAt is { } oldEnd && st.EndsAt is { } newEnd && a.Total == st.Total
                && a.Total is { } total && total > TimeSpan.Zero
                && (oldEnd - newEnd).TotalSeconds / total.TotalSeconds >= CatchUpMinJumpFraction && left.TotalSeconds > 1)
            {
                a.CatchFrom = a.Drawn;
                a.CatchStart = ms;
            }
            if (left.TotalSeconds <= 1) a.CatchStart = -1;
            double shown = p;
            if (a.CatchStart >= 0)
            {
                double t = (ms - a.CatchStart) / FadeMs;
                if (t >= 1) a.CatchStart = -1;
                else { shown = a.CatchFrom + (p - a.CatchFrom) * EaseOut(t); _animating = true; }
            }
            a.Drawn = shown;

            if (shown > 0) DrawProgress(dc, r, c, dim, look, shown, l.Unit, icon);
            if (left > TimeSpan.Zero)
            {
                if (_frozenAt is null) _moving = true;
                double sec = left.TotalSeconds;
                // 쿨 돌기 직전 깜빡임: the buff bars' last-second sweep, here to say the skill is about to be ready.
                if (endFlash && sec < EndFlashFrom) DrawBoxEndFlash(dc, r, sec, reduced);
                timer = sec < 3
                    ? (Math.Ceiling(sec * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture)
                    : Math.Ceiling(sec).ToString(CultureInfo.InvariantCulture);
            }
        }
        else a.CatchStart = -1;
        a.EndsAt = st.Phase == SlotPhase.Cooldown ? st.EndsAt : null;
        a.Total = st.Total;

        // 쿨 돈 동안 계속 빛남, in the group's style and colour, once every PulsePeriod (steady under reduced motion).
        if (ready && pulses && !peer)
        {
            var brush = Solid(glowColor);
            double? at = reduced ? null : ms / (PulsePeriod * 1000) % 1;
            double breath = reduced ? 0.6 : GaugeBar.GlowLevel(ms, PulsePeriod);
            switch (glow)
            {
                case ReadyGlowStyle.Diagonal: DrawBand(dc, r, at, brush, diagonal: true); break;
                case ReadyGlowStyle.Horizontal: DrawBand(dc, r, at, brush, diagonal: false); break;
                case ReadyGlowStyle.Outline:
                    dc.PushOpacity(0.4 + 0.6 * breath);
                    dc.DrawRoundedRectangle(null, GlowPenOf(glowColor),
                        new Rect(r.X + ReadyOutline / 2, r.Y + ReadyOutline / 2, Math.Max(0, r.Width - ReadyOutline), Math.Max(0, r.Height - ReadyOutline)), BoxRadius, BoxRadius);
                    dc.Pop();
                    break;
                default:
                    dc.PushOpacity(GlowAlpha * breath);
                    dc.DrawRoundedRectangle(brush, null, r, BoxRadius, BoxRadius);
                    dc.Pop();
                    break;
            }
            if (!reduced) _animating = true;
        }
        if (st.Gold) DrawGold(dc, r);
        if (st.Phase == SlotPhase.LongCooldown) timer = $"{st.Minutes}m";
        // 사용 후 3초 유지: the seconds held before the use take the timer's place for HeldCount.Hold.
        int? held = a.Held.Shown(now);
        if (held is not null && _frozenAt is null) _moving = true;
        if (held is { } h) DrawReadyCount(dc, r, h);
        else if (timer is not null && Settings.Overlay.ShowTimer && !peer) DrawTimer(dc, r, timer);
        // 쿨 돈 뒤 지난 시간: where the timer would be, redrawn each frame until it holds at 29.
        if (counts && st is { Phase: SlotPhase.Ready, Estimated: false })
        {
            int? n = CooldownLook.ReadyCount(st, now);
            if (n is { } count) DrawReadyCount(dc, r, count);
            if (n != CooldownLook.MaxReadyCount && _frozenAt is null) _moving = true;
        }

        if (st.Estimated)
            dc.DrawRectangle(null, DottedPen, new Rect(r.X + 0.5, r.Y + 0.5, r.Width - 1, r.Height - 1));

        // Shine: the whole box flashes white, fading from ShineAlpha to 0 (ease-out).
        if (shineT is >= 0 and < 1 && !reduced)
        {
            dc.PushOpacity(ShineAlpha * (1 - EaseOut(shineT)));
            dc.DrawRoundedRectangle(Brushes.White, null, r, BoxRadius, BoxRadius);
            dc.Pop();
        }
    }

    /// <summary>Time gone by, <paramref name="p"/> of the cooldown, as the 쿨타임 표시 setting draws it: a sweep or a fill
    /// in the refill colour (slashed if the look says so), or only a white line at the progress edge (a stack skill with
    /// a use left): the clock hand of the sweep, or the fill's edge.</summary>
    /// <summary>The icon filling <paramref name="r"/> (centred, cropped to the box's shape), inside <paramref name="shape"/>,
    /// darkened to <paramref name="level"/> as a colour box would be.</summary>
    static void DrawIcon(DrawingContext dc, Geometry shape, Rect r, ImageSource icon, double level)
    {
        double s = Math.Max(r.Width / icon.Width, r.Height / icon.Height), w = icon.Width * s, h = icon.Height * s;
        dc.PushClip(shape);
        dc.DrawImage(icon, new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
        if (level < 1)
        {
            dc.PushOpacity(1 - level);
            dc.DrawRectangle(Brushes.Black, null, r);
            dc.Pop();
        }
        dc.Pop();
    }

    void DrawProgress(DrawingContext dc, Rect r, Color c, Color? dim, CooldownLook look, double p, double unit, ImageSource? icon)
    {
        var o = Settings.Overlay;
        bool sweep = o.CooldownStyle == CooldownStyle.Sweep;
        var (x, y, w, h) = CooldownLook.FillRect(r.X, r.Y, r.Width, r.Height, p, o.FillFrom);
        var fill = new Rect(x, y, w, h);
        dc.PushClip(BoxClip(r));
        if (look.Refill is { } refill)
        {
            Geometry part = sweep ? Pie(r, p) : new RectangleGeometry(fill);
            if (icon is not null) DrawIcon(dc, part, r, icon, refill);
            else dc.DrawGeometry(Solid(Shade(c, dim, refill)), null, part);
            if (look.Hatched) dc.DrawGeometry(HatchFor(unit, dark: true), null, part);
        }
        else
        {
            double t = Math.Max(1, Math.Round(LineWidth * unit / Box * 2) / 2);   // half-pixel steps keep _linePens small
            if (sweep)
            {
                var centre = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height) / 2, ang = p * 2 * Math.PI;
                dc.DrawLine(LinePen(t), centre, new Point(centre.X + rad * Math.Sin(ang), centre.Y - rad * Math.Cos(ang)));
            }
            else
            {
                // The fill's moving edge.
                var edge = o.FillFrom switch
                {
                    FillFrom.Bottom => new Rect(r.X, y, r.Width, t),
                    FillFrom.Top => new Rect(r.X, y + h - t, r.Width, t),
                    FillFrom.Left => new Rect(x + w - t, r.Y, t, r.Height),
                    _ => new Rect(x, r.Y, t, r.Height),
                };
                dc.DrawRectangle(Brushes.White, null, edge);
            }
        }
        dc.Pop();
    }

    /// <summary>The game's gold frame on this skill: a band of the user's colour and width inside the box edge (so the
    /// layout never moves), at most half the box.</summary>
    void DrawGold(DrawingContext dc, Rect r)
    {
        double t = Math.Min(Settings.Overlay.GoldThickness, Math.Min(r.Width, r.Height) / 2);
        if (t <= 0) return;
        var brush = Solid(Parse(Settings.Overlay.GoldColor));
        dc.PushClip(BoxClip(r));   // follow the box's rounded corners
        dc.DrawRectangle(brush, null, new Rect(r.X, r.Y, r.Width, t));
        dc.DrawRectangle(brush, null, new Rect(r.X, r.Bottom - t, r.Width, t));
        dc.DrawRectangle(brush, null, new Rect(r.X, r.Y + t, t, r.Height - 2 * t));
        dc.DrawRectangle(brush, null, new Rect(r.Right - t, r.Y + t, t, r.Height - 2 * t));
        // A dark line inside the band, so the frame stays visible on a box colour close to the gold (a member's own too).
        if (r.Width > 2 * t + GoldEdge && r.Height > 2 * t + GoldEdge)
            dc.DrawRectangle(null, GoldEdgePen, new Rect(r.X + t + GoldEdge / 2, r.Y + t + GoldEdge / 2, r.Width - 2 * t - GoldEdge, r.Height - 2 * t - GoldEdge));
        dc.Pop();
    }

    /// <summary>The largest timer font (DIP, in TimerFontStep steps) whose "88" fits a self box at the Width/Height
    /// settings. The drawn font is Settings.Overlay.TimerFont capped at this, so the setting survives a shrink and grow
    /// in edit mode and is clamped only when edit mode ends (OverlayWindow).</summary>
    public static double MaxTimerFont(double width, double height) => MaxFontFor(BoxSize(width), BoxSize(height));

    static double MaxFontFor(double boxWidth, double boxHeight) => Math.Floor(
        Math.Min((boxWidth - TimerInset) / DigitPair.Width, (boxHeight - TimerInset) / DigitPair.Height) / TimerFontStep) * TimerFontStep;

    // Size of "88" per DIP of font size (glyph bounds, as drawn).
    static readonly Size DigitPair = PerDip(
        new FormattedText("88", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 100, Brushes.White, 1.0)
            .BuildGeometry(new Point(0, 0)).Bounds);

    static Size PerDip(Rect r) => new(r.Width / 100, r.Height / 100);

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

    void DrawTimer(DrawingContext dc, Rect r, string text, Color? fill = null, Pen? outline = null) =>
        DrawOutlined(dc, r, text, Math.Min(Math.Round(Settings.Overlay.TimerFont / TimerFontStep) * TimerFontStep, MaxFontFor(r.Width, r.Height)), fill, outline);

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

    /// <summary>쿨 돌기 직전 깜빡임 on a skill box: a white band sweeps diagonally, top-left → bottom-right,
    /// <see cref="EndFlashSweeps"/> times over the last <see cref="EndFlashFrom"/> s, the last ending at 0 (steady under
    /// reduced motion), as a buff bar's 마지막 1초 깜빡임.</summary>
    void DrawBoxEndFlash(DrawingContext dc, Rect r, double rem, bool reduced) =>
        DrawBand(dc, r, reduced ? null : (EndFlashFrom - rem) / EndFlashFrom * EndFlashSweeps % 1, Brushes.White, diagonal: true);

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

    // ── Loop ─────────────────────────────────────────────────────────────────────────

    void SetLoop(bool on)
    {
        if (on == _loop) return;
        _loop = on;
        if (on) CompositionTarget.Rendering += OnFrame;
        else CompositionTarget.Rendering -= OnFrame;
        Trace.WriteLine($"[ui] render loop {(on ? "attached" : "detached")}");
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double t = ((RenderingEventArgs)e).RenderingTime.TotalMilliseconds;
        if (t - _lastFrame < _frameMs - 1.5) return;
        _lastFrame = t;
        InvalidateVisual();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    sealed class BoxAnim
    {
        public bool WasCooling;
        public double ShineAt = -1, CatchStart = -1, CatchFrom, Drawn;   // ShineAt may lie ShineDelayMs ahead
        public TimeSpan? EndsAt, Total;
        public HeldCount Held;
    }

    /// <summary>A 120 ms ease-out tween toward the latest target; the first target is taken without animating.</summary>
    sealed class Tween
    {
        double _from, _to = double.NaN, _start = -1;

        public double Value(double target, double ms, ref bool busy)
        {
            if (double.IsNaN(_to)) _to = _from = target;
            if (target != _to) { _from = Current(ms); _to = target; _start = ms; }
            double v = Current(ms);
            if (_start >= 0) busy = true;
            return v;
        }

        double Current(double ms)
        {
            if (_start < 0) return _to;
            double t = (ms - _start) / FadeMs;
            if (t >= 1) { _start = -1; return _to; }
            return _from + (_to - _from) * EaseOut(t);
        }
    }

    /// <summary>Each gauge segment's colour easing to its latest target over <see cref="ColorFadeMs"/> (바드: gold → blue as a
    /// section fills); the first targets, a change in the segment count, and reduced motion take the target at once.</summary>
    sealed class ColorFade
    {
        Color[] _from = [], _to = [];
        double _start = -1;

        public Color[] Value(IReadOnlyList<Color> target, double ms, bool reduced, ref bool busy)
        {
            if (reduced || _to.Length != target.Count) (_from, _to, _start) = ([.. target], [.. target], -1);
            else if (!target.SequenceEqual(_to)) (_from, _to, _start) = (Current(ms), [.. target], ms);
            var now = Current(ms);
            if (_start >= 0) busy = true;
            return now;
        }

        Color[] Current(double ms)
        {
            double t = _start < 0 ? 1 : (ms - _start) / ColorFadeMs;
            if (t >= 1) { _start = -1; return [.. _to]; }
            double e = EaseOut(t);
            static byte Mix(byte a, byte b, double e) => (byte)Math.Round(a + (b - a) * e);
            return [.. _from.Zip(_to, (a, b) => Color.FromArgb(Mix(a.A, b.A, e), Mix(a.R, b.R, e), Mix(a.G, b.G, e), Mix(a.B, b.B, e)))];
        }
    }

    static T Get<T>(Dictionary<string, T> d, string key) where T : new()
    {
        if (!d.TryGetValue(key, out var v)) d[key] = v = new T();
        return v;
    }

    /// <summary>cubic-bezier(0.23, 1, 0.32, 1), solved for x by bisection.</summary>
    static double EaseOut(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        static double B(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double lo = 0, hi = 1, t = x;
        for (int i = 0; i < 24; i++)
        {
            t = (lo + hi) / 2;
            if (B(t, 0.23, 0.32) < x) lo = t; else hi = t;
        }
        return B(t, 1, 1);
    }

    /// <summary>Clockwise pie from 12 o'clock covering <paramref name="p"/> of the box (radius reaches the corners).</summary>
    static Geometry Pie(Rect r, double p)
    {
        if (p >= 0.9999) return new RectangleGeometry(r);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height) / 2 + 1;
        double ang = p * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true, true);
            ctx.LineTo(new Point(c.X, c.Y - rad), false, false);
            ctx.ArcTo(new Point(c.X + rad * Math.Sin(ang), c.Y - rad * Math.Cos(ang)), new Size(rad, rad), 0,
                ang > Math.PI, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary><paramref name="c"/> at <paramref name="k"/> of its perceived lightness (CIE L*), chromaticity kept,
    /// so 30% / 55% are the same visible step for every hue (scaling sRGB bytes dims dark hues far more).</summary>
    /// <summary>A cooling box's colour at lightness <paramref name="k"/>: with a chosen cooldown colour
    /// (<paramref name="dim"/>), that colour at <see cref="CooldownLevel"/> blending into <paramref name="c"/> at 1;
    /// otherwise <see cref="Level"/>.</summary>
    static Color Shade(Color c, Color? dim, double k)
    {
        if (dim is not { } d || k >= 1) return Level(c, k);
        double t = Math.Clamp((k - CooldownLevel) / (1 - CooldownLevel), 0, 1);
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromRgb(Mix(d.R, c.R), Mix(d.G, c.G), Mix(d.B, c.B));
    }

    internal static Color Level(Color c, double k)
    {
        if (k >= 1) return c;
        double y = Luminance(c);
        if (y <= 0) return Color.FromRgb(0, 0, 0);
        double f = LightnessToY(k * Lightness(y)) / y;
        return Color.FromRgb(Encode(Linear(c.R) * f), Encode(Linear(c.G) * f), Encode(Linear(c.B) * f));
    }

    /// <summary>CIE L* (0–100) of a colour.</summary>
    internal static double Lightness(Color c) => Lightness(Luminance(c));

    static double Lightness(double y) => y > 216.0 / 24389 ? 116 * Math.Cbrt(y) - 16 : y * 24389 / 27;
    static double LightnessToY(double l) => l > 8 ? Math.Pow((l + 16) / 116, 3) : l * 27 / 24389;
    static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    static double Linear(byte v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
    static byte Encode(double x) => (byte)Math.Round(255 * Math.Clamp(x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055, 0, 1));

    Color Parse(string hex)
    {
        if (_colors.TryGetValue(hex, out var c)) return c;
        if (_colors.Count > 1024) _colors.Clear();   // colours a party member sent are new keys: keep the cache bounded
        try { c = (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { c = Colors.Gray; }
        return _colors[hex] = c;
    }

    SolidColorBrush Solid(Color c)
    {
        uint k = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
        if (_brushes.TryGetValue(k, out var b)) return b;
        if (_brushes.Count > 1024) _brushes.Clear();
        return _brushes[k] = Frozen(new SolidColorBrush(c));
    }

    static SolidColorBrush NewSolid(Color c) => Frozen(new SolidColorBrush(c));

    FormattedText Text(string s, double size) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>A peer name's text, kept per name and size (drawn every frame).</summary>
    static double NameFont(double unit) => Math.Max(MinNameFont, unit * PeerNameShare);

    FormattedText NameText(string s, double size)
    {
        if (_names.TryGetValue((s, size), out var t)) return t;
        if (_names.Count > 64) _names.Clear();
        return _names[(s, size)] = Text(s, size);
    }

    Geometry BoxClip(Rect r)
    {
        if (!_clips.TryGetValue(r, out var g)) _clips[r] = g = Frozen(new RectangleGeometry(r, BoxRadius, BoxRadius));
        return g;
    }

    Geometry TextGeometry(string s, double size)
    {
        if (_texts.TryGetValue((s, size), out var g)) return g;
        if (_texts.Count > 512) _texts.Clear();
        // Segoe UI's default figures are tabular, so digits don't shift as the timer counts.
        g = Text(s, size).BuildGeometry(new Point(0, 0));
        g.Freeze();
        return _texts[(s, size)] = g;
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>The "unknown" hatch, tiled at a size proportional to <paramref name="unit"/> so it scales with the
    /// box instead of going heavy at small scale and faint at large scale.</summary>
    /// <summary>Slashes for a box of this size: white (Unknown) or dark (a slashed fill, over full colour).</summary>
    Brush HatchFor(double unit, bool dark = false)
    {
        if (_hatches.TryGetValue((unit, dark), out var b)) return b;
        if (_hatches.Count > 16) _hatches.Clear();
        return _hatches[(unit, dark)] = MakeHatch(6 * unit / Box, dark ? Color.FromArgb(0x80, 0, 0, 0) : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
    }

    Pen LinePen(double width)
    {
        if (!_linePens.TryGetValue(width, out var p)) _linePens[width] = p = Frozen(new Pen(Brushes.White, width));
        return p;
    }

    Pen GlowPenOf(Color c)
    {
        if (_glowPens.TryGetValue(c, out var p)) return p;
        if (_glowPens.Count > MaxGlowPens) _glowPens.Clear();   // each colour tried in 빛 색 adds one: keep it bounded
        return _glowPens[c] = Frozen(new Pen(Solid(c), ReadyOutline));
    }

    /// <summary><paramref name="c"/> without its hue, at the same perceived lightness.</summary>
    static Color Grey(Color c)
    {
        byte v = Encode(Luminance(c));
        return Color.FromRgb(v, v, v);
    }

    static Brush MakeHatch(double tile, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.2 * tile / 6);
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, tile), new Point(tile, 0)));
        lines.Children.Add(new LineGeometry(new Point(-tile / 6, tile / 6), new Point(tile / 6, -tile / 6)));
        lines.Children.Add(new LineGeometry(new Point(tile * 5 / 6, tile * 7 / 6), new Point(tile * 7 / 6, tile * 5 / 6)));
        return Frozen(new DrawingBrush(new GeometryDrawing(null, pen, lines))
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewport = new Rect(0, 0, tile, tile), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, tile, tile), ViewboxUnits = BrushMappingMode.Absolute,
        });
    }
}
