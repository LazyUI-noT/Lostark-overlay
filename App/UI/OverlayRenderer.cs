using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    const double BuffTrackLevel = 0.40;    // a buff bar's empty track: its colour still tells which buff (user, 2026-11-10: 0.20 was too dim; 0.55 was tried, but the dim part was the filled 자동 bars, not this)
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
    /// the layout editor, edit mode and sample previews. These also draw 자동 buff bars as in a raid.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>The settings object the user edits; read on every frame.</summary>
    public Settings Settings { get; set; } = new();

    bool _loop, _moving, _animating, _blinking;   // loop attached; this frame had a moving sweep / a shine or tween / only a slow fade
    double _frameMs = 1000.0 / DrawSteps.Default;
    /// <summary>The shortest frame time; 설정 → 표시's small scene draws slower than a real overlay (0: no limit).</summary>
    internal double MinFrameMs { get; set; }
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
    bool _frameAsked;   // the next draw was asked by a loop frame (perf counts)

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
        // The same data again (an empty party slot's null, a member's last message): nothing shown changes. A moving window
        // still draws from its loop; settings come through Refresh (status/decisions.md, "Overlay redraws"). ~30/s for 3
        // empty party windows before (perf-trace).
        if (frozenAt is null && _frozenAt is null && ReferenceEquals(player, _player)) return;
        // A still window whose part of the snapshot is the same as last drawn: nothing it shows changes (9/s each before).
        if (!_loop && frozenAt is null && _frozenAt is null && player is not null && _player is not null
            && SameDrawn(player.Snapshot, _player.Snapshot)) { _player = player; return; }
        if ((frozenAt is null) != (_frozenAt is null)) { _boxes.Clear(); _gauges.Clear(); _changed.Clear(); _barColors.Clear(); }
        _player = player;
        _frozenAt = frozenAt;
        // 초당 그리기 횟수 caps every draw: while the loop runs, its next frame draws the new snapshot (up to one frame
        // later). Drawing it at once too was ~9 extra draws a second over the setting (43.6/s at 30, perf-trace).
        if (_loop && frozenAt is null) { InvalidateMeasure(); return; }
        Refresh();
    }

    /// <summary>The window draws exactly these fields of the snapshot (and the settings, which come through
    /// <see cref="Refresh"/>), so equal ones draw the same picture while nothing moves. A part that shows the time must
    /// set the moving flags. 미터기 창: App builds MeterView once per meter status (1-2/s), each row's class included.
    /// Windows without a check (yours, 버프, 게이지, party) draw every new snapshot. status/decisions.md, "Overlay redraws".</summary>
    bool SameDrawn(Snapshot a, Snapshot b) =>
        MeterOnly ? ReferenceEquals(a.MeterView, b.MeterView)
        : DebuffsOnly ? Equals(a.Debuffs, b.Debuffs)
        : JudgmentOnly ? Equals(a.Judgment, b.Judgment)
        : AmsuOnly ? Equals(a.Amsu, b.Amsu) && a.AmsuTimed == b.AmsuTimed
        : RaidOnly && Equals(a.Raid, b.Raid);

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

    /// <summary>Width/Height settings after dragging the corner handle by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// from a plate of <paramref name="plate"/> DIPs drawn at <paramref name="start"/>: each scales with its axis.</summary>
    public static (double W, double H) Resized((double W, double H) start, (double W, double H) plate, double dx, double dy) =>
        (Math.Round(Math.Clamp(start.W * (plate.W + dx) / plate.W, MinScale, MaxScale), 2),
         Math.Round(Math.Clamp(start.H * (plate.H + dy) / plate.H, MinScale, MaxScale), 2));

    /// <summary>A box edge in DIPs for a Width/Height setting.</summary>
    /// <param name="min">Lowest scale drawn: a party member's received size or layout may be fitted below the setting's.</param>
    static double BoxSize(double setting, double share = 1, double min = MinScale) => SplitLayout.BoxSize(setting, share, min);

    /// <summary>The free layout: each shown part at its cell (a part the layout lacks at its fixed-grid spot), scaled by
    /// the box size. Nothing closes up when a group is off. The plate wraps what is shown; the gauge keeps its bottom
    /// edge at its cell's, so a thicker gauge grows upwards.</summary>
    PlayerLayout? LayoutCells(Player p, string key, IReadOnlyDictionary<Part, Cell> cells, IReadOnlySet<SlotId> hidden, double bw,
        double bh, double lead, double top, Func<SlotGroup, bool> show, Func<SlotGroup, Color> col, Func<SlotId, Color> skillCol, double gaugeH, double leadH, double homeH = 0)
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
        else if (homeH > 0 && At(Part.게이지) is var gh) l.GaugeHome = Placed(new Rect(gh.X, gh.Bottom - homeH, gh.Width, homeH));
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
        : [.. Enum.GetValues<BuffKind>().Where(k => ClassProfiles.BuffShown(Settings, k, Target?.Key))];

    /// <summary>The point the window keeps still on screen when the layout changes (NaN when nothing is laid out): the
    /// top of the fixed grid, so a thicker gauge grows upwards, or where the free layout's cell (0, 0) is drawn, so
    /// dragging a box past the others' edge doesn't move them.</summary>
    public Point Anchor => _layout?.Anchor ?? new Point(double.NaN, double.NaN);

    /// <summary>True when drawn from a free layout; <see cref="Anchor"/> means a different point in each kind.</summary>
    public bool IsFree => _layout?.Free == true;

    /// <summary>The fixed grid's buff bar drawn at 길이 1 (0: none drawn): what an end drag measures 길이 against.</summary>
    public double BarFull => _layout?.BarFull ?? 0;

    /// <summary>The parts drawn now (the boxes a dragged one snaps to).</summary>
    public IEnumerable<Part> DrawnParts => _layout is not { } l || GaugeOnly ? []
        : l.Boxes.Select(b => (Part)b.Key).Concat(l.Gauge is null ? [] : [Part.게이지]);

    /// <summary>The drawn player's class (a party member's decides their boxes' settings), or null when unknown.</summary>
    public string? ClassName => _player is { } p ? Emblems.Class(p.Snapshot.ClassId)?.Name : null;

    /// <summary>A party member drawn with the layout they sent (파티원 배치 받기), not their class's.</summary>
    public bool ShowsReceivedLayout => Peer >= 0 && Settings.Party.ReceiveLayout && _player?.Look?.Layout is { } sent && !FreeLayout.Spread(sent);

    /// <summary>DIP per cell unit along each axis (the box size setting, as drawn).</summary>
    public (double X, double Y) CellScale => _layout?.Scale ?? (1, 1);

    // ── Drawing ──────────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        TimeSpan now = _frozenAt ?? Clock.Now();
        double ms = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        long cpu0 = Services.Perf.ThreadCpu();
        bool reduced = !SystemParameters.ClientAreaAnimation;
        _moving = _animating = _blinking = false;

        if (DebuffsOnly) DrawDebuffs(dc, now, ms, reduced);
        else if (JudgmentOnly) DrawJudgment(dc, now, ms, reduced);
        else if (AmsuOnly) DrawAmsu(dc, now, ms, reduced);
        else if (RaidOnly) DrawRaid(dc, ms, reduced);
        else if (MeterOnly) DrawMeter(dc);
        else if (_layout is { } l) DrawPlayer(dc, l, now, ms, reduced);
        // 초당 그리기 횟수: one rate for every moving part; a slow fade alone at most 15 (DrawSteps).
        _frameMs = Math.Max(MinFrameMs, DrawSteps.FrameMs(Settings.DrawsPerSecond, fadeOnly: !_animating && !_moving));
        SetLoop(_moving || _animating || _blinking);
        string part = DebuffsOnly ? "그리기: 디버프 창" : JudgmentOnly ? "그리기: 단심 창" : AmsuOnly ? "그리기: 암수 창" : RaidOnly ? "그리기: 줄 수 창" : MeterOnly ? "그리기: 미터기 창"
            : BuffsOnly ? "그리기: 버프 창" : GaugeOnly ? "그리기: 게이지 창" : Peer >= 0 ? "그리기: 파티 창" : "그리기: 스킬 창";
        Services.Perf.AddCpu(part, Stopwatch.GetTimestamp() - (long)(ms * Stopwatch.Frequency / 1000.0), Services.Perf.ThreadCpu() - cpu0);
        Services.Perf.Count(part);   // draws a second, per window (perf-trace)
        if (!_frameAsked) Services.Perf.Count(part + " · 루프 밖");   // drawn for another reason than a loop frame
        _frameAsked = false;
    }

    void DrawPlayer(DrawingContext dc, PlayerLayout l, TimeSpan now, double ms, bool reduced)
    {
        var s = l.Player.Snapshot;
        var hidden = HiddenBoxes(l, now);
        // Plate, gauge and boxes each have their own opacity setting. The detached buff window has no plate.
        if (Settings.Overlay.ShowPlate && !BuffsOnly && !GaugeOnly)
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
            if (l.Bar is { } bar) DrawBar(dc, g, l.Key, bar, s, v, ms, reduced, GaugeUpright);
            else if (v > 0) dc.DrawRectangle(Solid(Parse(Settings.Gauge.Fill)), null, AsRect(GaugeAxis.Span(AsCell(g), 0, v, GaugeUpright)));
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
                var st = s.Buffs.IsDefaultOrEmpty ? null : s.Buffs[(int)kind];
                // A preview (ShowHidden) draws as in a raid: a 자동 bar full, not faint (user, 2026-11-12: 위치 조정 too).
                Faint(dc, Settings.Buffs.HideWhenEmpty && st is not { Active: true } || !ShowHidden && Settings.Buffs.OutOfRaid(s.Presence.InDungeon(now)),
                    () => DrawBuff(dc, r, c, st, Settings.Buffs.Of(kind).ShowLabel, Settings.Buffs.EndFlash(kind), now, reduced));
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
        // with none left turns grey, its icon too).
        var look = CooldownLook.For(st, l.Cooldown.Style);
        bool seconds = st.Phase == SlotPhase.Cooldown && !ready;
        double level = seconds ? look.Base : !ready ? CooldownLevel
            : reduced && shineT < 1 ? CooldownLevel + (1 - CooldownLevel) * EaseOut(shineT) : 1;
        if (icon is not null) DrawIcon(dc, BoxClip(r), r, seconds && look.Grey ? GreyIcon(icon) : icon, level);
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

            if (shown > 0) DrawProgress(dc, r, c, dim, look, l.Cooldown, shown, l.Unit, icon);
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
        else if (timer is not null && l.Cooldown.ShowTimer && !peer) DrawTimer(dc, r, timer);
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

    /// <summary>A stack skill with no use left: its icon in greys, as the game shows it (made once per icon).</summary>
    static ImageSource GreyIcon(ImageSource icon) =>
        icon is BitmapSource b ? GreyIcons.GetValue(b, k => Frozen(new FormatConvertedBitmap(k, PixelFormats.Gray8, null, 0))) : icon;
    static readonly ConditionalWeakTable<BitmapSource, BitmapSource> GreyIcons = new();

    /// <summary>Time gone by, <paramref name="p"/> of the cooldown, as the 쿨타임 표시 setting draws it: a sweep or a fill
    /// in the refill colour (slashed if the look says so), or only a white line at the progress edge (a stack skill with
    /// a use left): the clock hand of the sweep, or the fill's edge.</summary>
    void DrawProgress(DrawingContext dc, Rect r, Color c, Color? dim, CooldownLook look, CooldownView o, double p, double unit, ImageSource? icon)
    {
        bool sweep = o.Style == CooldownStyle.Sweep;
        var (x, y, w, h) = CooldownLook.FillRect(r.X, r.Y, r.Width, r.Height, p, o.From);
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
                var edge = o.From switch
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

    void DrawTimer(DrawingContext dc, Rect r, string text, Color? fill = null, Pen? outline = null) =>
        DrawOutlined(dc, r, text, Math.Min(Math.Round(Settings.Overlay.TimerFont / TimerFontStep) * TimerFontStep, MaxFontFor(r.Width, r.Height)), fill, outline);

    /// <summary>쿨 돌기 직전 깜빡임 on a skill box: a white band sweeps diagonally, top-left → bottom-right,
    /// <see cref="EndFlashSweeps"/> times over the last <see cref="EndFlashFrom"/> s, the last ending at 0 (steady under
    /// reduced motion), as a buff bar's 마지막 1초 깜빡임.</summary>
    void DrawBoxEndFlash(DrawingContext dc, Rect r, double rem, bool reduced) =>
        DrawBand(dc, r, reduced ? null : (EndFlashFrom - rem) / EndFlashFrom * EndFlashSweeps % 1, Brushes.White, diagonal: true);

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
        _frameAsked = true;
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

    /// <summary>CIE L* (0–100) of a colour.</summary>
    internal static double Lightness(Color c) => Lightness(Luminance(c));

    static double Lightness(double y) => y > 216.0 / 24389 ? 116 * Math.Cbrt(y) - 16 : y * 24389 / 27;
    static double LightnessToY(double l) => l > 8 ? Math.Pow((l + 16) / 116, 3) : l * 27 / 24389;
    static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    static double Linear(byte v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
    static byte Encode(double x) => (byte)Math.Round(255 * Math.Clamp(x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1 / 2.4) - 0.055, 0, 1));

    static SolidColorBrush NewSolid(Color c) => Frozen(new SolidColorBrush(c));

    FormattedText Text(string s, double size) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>A peer name's text, kept per name and size (drawn every frame).</summary>
    static double NameFont(double unit) => Math.Max(MinNameFont, unit * PeerNameShare);

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

}
