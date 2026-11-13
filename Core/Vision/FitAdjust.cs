using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>What 수동 조정 moves: the skill bar as a whole (its fit), one of the rows that follow it, or the 전분 panel's spot.</summary>
public enum AdjustPart { 스킬창, 버프, 디버프, 보스, 전분 }

/// <summary>수동 조정's live edit in the debug window: <see cref="Start"/> is the fit it opened on, <see cref="Current"/> the one
/// shown. The bar moves by its nudge (reference pixels; shown as screen pixels that its scaling never changes) and scales by its
/// HUD %, the rows by their own shifts (screen pixels); the buff and debuff rows are sized by the game's 버프 크기
/// (<see cref="BuffSize"/>, a 설정 value, not part of the fit), the boss row by its own % (<see cref="Fit.BossSize"/>). Every edit
/// is clamped: HUD <see cref="MinHud"/>–<see cref="MaxHud"/>, the bar and every row inside the game area. A drag should apply its
/// whole distance to the session it started on, so rounding never drifts. The 전분 panel is no part of the fit: <see cref="Panel"/> is
/// its own rect, starting on the spot in use or the usual one, in the reader's 470:114 shape once sized. Immutable.</summary>
public sealed record FitAdjust(Viewport Vp, int BuffSize, Fit Start, Fit Current, int StartBuffSize)
{
    public const double MinHud = 50, MaxHud = 250;
    /// <summary>The boss row's size range in % of the game's.</summary>
    public const int MinBoss = 50, MaxBoss = 200;
    /// <summary>The 버프 크기 range ± reaches: the game offers <see cref="Settings.BuffSizes"/>, a video (테스트 모드) may show any size.</summary>
    public const int MinBuffSize = 50, MaxBuffSize = 200;
    /// <summary>The narrowest 전분 box: half the panel at 1080p.</summary>
    public const int MinPanelWidth = (int)(JeonbunReader.RefW / 2);

    /// <summary>The buff boxes read (설정's 버프 읽을 칸 수): the buff row is drawn and kept inside the game area at this many.</summary>
    public int BuffCount { get; init; } = Geometry.BuffBoxCount;
    /// <summary>The game's 보스 디버프 크기 (a 설정 value): the boss row's size before <see cref="Fit.BossSize"/>.</summary>
    public int BossGameSize { get; init; } = Geometry.RefBossSize;
    /// <summary>The 전분 box shown, in screen pixels.</summary>
    public PixelRect Panel { get; init; }
    /// <summary>The spot the window opened on: the one in use, or <see cref="Geometry.UsualPanel"/> with none.</summary>
    public PixelRect StartPanel { get; init; }

    /// <param name="panel">The 전분 spot in use; null: none yet, the box starts on the usual spot.</param>
    public static FitAdjust Begin(Viewport vp, int buffSize, Fit fit, int buffCount = Geometry.BuffBoxCount, int bossSize = Geometry.RefBossSize, PixelRect? panel = null)
    {
        var start = panel ?? Geometry.UsualPanel(vp);
        var a = new FitAdjust(vp, buffSize, fit, fit, buffSize) { BuffCount = buffCount, BossGameSize = bossSize, StartPanel = start };
        return a with { Current = a.Clamp(fit), Panel = a.Placed(start) };
    }

    /// <summary>The fit or the buff size differ from the start: 적용 has a fit to store and calibrate.</summary>
    public bool FitChanged => Current != Start || BuffSize != StartBuffSize;
    /// <summary>The 전분 box left its start: 적용 has a spot to store. An unmoved box saves nothing, so the usual spot never counts as placed.</summary>
    public bool PanelMoved => Panel != StartPanel;
    public bool Changed => FitChanged || PanelMoved;

    /// <summary>되돌리기: back to the fit, buff size and 전분 spot the window opened on.</summary>
    public FitAdjust Reverted() => this with { Current = Start, BuffSize = StartBuffSize, Panel = Placed(StartPanel) };

    double K => Geometry.Scale(Vp, Current);

    public FitAdjust Move(AdjustPart part, double dx, double dy) => part == AdjustPart.전분
        ? this with { Panel = Placed(Panel with { X = Panel.X + (int)Math.Round(dx), Y = Panel.Y + (int)Math.Round(dy) }) }
        : this with
    {
        Current = Clamp(part switch
        {
            AdjustPart.스킬창 => Current with { NudgeX = Current.NudgeX + dx / K, NudgeY = Current.NudgeY + dy / K },
            AdjustPart.버프 => Current with { BuffShift = By(Current.BuffShift, dx, dy) },
            AdjustPart.디버프 => Current with { DebuffShift = By(Current.DebuffShift, dx, dy) },
            _ => Current with { BossShift = By(Current.BossShift, dx, dy) },
        }),
    };

    /// <summary>The bar at HUD <paramref name="hud"/>, scaled about the HUD's anchor: its x/y (<see cref="Offset"/>) stay.</summary>
    public FitAdjust ScaleTo(double hud) => this with { Current = Clamp(Geometry.Rescale(Vp, Current, Math.Clamp(hud, MinHud, MaxHud))) };

    /// <summary>The bar's bottom-left corner dragged: its left edge to screen x <paramref name="left"/>, its x/y kept.</summary>
    public FitAdjust DragLeftTo(double left) => ScaleTo(Geometry.HudForLeft(Vp, Current, left));

    /// <summary>± on the size: the bar <paramref name="pixels"/> screen pixels wider, its x/y kept.</summary>
    public FitAdjust WidenBy(int pixels) => ScaleTo(Geometry.HudForWidth(Vp, Geometry.BarRect(Vp, Current).Width + pixels));

    /// <summary>The 버프 크기 for both rows, any whole % in <see cref="MinBuffSize"/>–<see cref="MaxBuffSize"/> (the slider's drag
    /// snaps to the game's choices; ± steps 1% between them).</summary>
    public FitAdjust WithBuffSize(int size)
    {
        var a = this with { BuffSize = Math.Clamp(size, MinBuffSize, MaxBuffSize) };
        return a with { Current = a.Clamp(Current) };
    }

    /// <summary>The 전분 box <paramref name="width"/> wide in the reader's shape (it scales from the width), its top left kept unless
    /// that would leave the game area.</summary>
    FitAdjust PanelTo(double width)
    {
        double max = Math.Min(Vp.Width, Vp.Height * JeonbunReader.RefW / JeonbunReader.RefH);
        int w = (int)Math.Round(Math.Clamp(width, MinPanelWidth, Math.Max(MinPanelWidth, max)));
        return this with { Panel = Placed(Panel with { Width = w, Height = (int)Math.Round(w * JeonbunReader.RefH / JeonbunReader.RefW) }) };
    }

    /// <summary>+ / − on the layer: the bar or the 전분 box <paramref name="step"/> screen pixels wider, the buff and debuff rows' 버프 크기
    /// or the boss row's % by <paramref name="step"/>.</summary>
    public FitAdjust Resize(AdjustPart part, int step) => part switch
    {
        AdjustPart.스킬창 => WidenBy(step),
        AdjustPart.버프 or AdjustPart.디버프 => WithBuffSize(BuffSize + step),
        AdjustPart.전분 => PanelTo(Panel.Width + step),
        _ => BossTo(BossPercent + step),
    };

    /// <summary>An arrow key on a picked part, <paramref name="dx"/>/<paramref name="dy"/> its direction times the step: a move, or with
    /// <paramref name="resize"/> (Ctrl) a size step in the part's own shape (<see cref="Resize"/>), → and ↑ bigger, ← and ↓ smaller.</summary>
    public FitAdjust Arrow(AdjustPart part, int dx, int dy, bool resize) => resize ? Resize(part, dx - dy) : Move(part, dx, dy);

    /// <summary>The lines inside a part where its icons belong, in screen pixels: the bar's slot outlines, the buff and debuff rows'
    /// box edges and the line between icons and timers, the boss row's slash. None for the 전분 box.</summary>
    public IReadOnlyList<(int X1, int Y1, int X2, int Y2)> Guides(AdjustPart part)
    {
        var r = RectOf(part);
        switch (part)
        {
            case AdjustPart.스킬창:
                return [.. SlotInfo.All.Select(s => Geometry.Slot(s, Vp, Current)).SelectMany(s => new[]
                {
                    (s.X, s.Y, s.Right, s.Y), (s.X, s.Bottom, s.Right, s.Bottom), (s.X, s.Y, s.X, s.Bottom), (s.Right, s.Y, s.Right, s.Bottom),
                })];
            case AdjustPart.버프 or AdjustPart.디버프:
                var boxes = part == AdjustPart.버프 ? Geometry.BuffBoxes(Vp, Current, BuffSize, BuffCount) : Geometry.DebuffBoxes(Vp, Current, BuffSize);
                var (columns, iconBottom) = Geometry.RowGrid(boxes);
                return [.. columns.Select(x => (x, r.Y, x, r.Bottom)).Append((r.X, iconBottom, r.Right, iconBottom))];
            case AdjustPart.보스:
                int axis = (int)Math.Round(r.X + Geometry.BossAxis * Geometry.BossScale(Vp, Current, BossGameSize));
                return [(axis, r.Y, axis, r.Bottom)];
            default:
                return [];
        }
    }

    /// <summary>A part's size handle, on the edge a size change moves: the bar's bottom-left (its left edge follows the pointer),
    /// the buff row's bottom-left (it keeps its right end and grows left), the debuff and boss rows' and the 전분 box's bottom-right.</summary>
    public (int X, int Y) Handle(AdjustPart part) => RectOf(part) is var r && part is AdjustPart.스킬창 or AdjustPart.버프 ? (r.X, r.Bottom) : (r.Right, r.Bottom);

    /// <summary>A size handle dragged <paramref name="dx"/> screen pixels, called on the drag's start state with the whole distance:
    /// the bar's left edge follows it (<see cref="DragLeftTo"/>); a row takes the size whose handle lands nearest the pointer (its
    /// width isn't proportional to its size, and the boss row grows about the slash).</summary>
    public FitAdjust DragCorner(AdjustPart part, double dx)
    {
        if (part == AdjustPart.스킬창) return DragLeftTo(RectOf(part).X + dx);
        if (part == AdjustPart.전분) return PanelTo(Panel.Width + dx);
        double target = Handle(part).X + dx;
        var (min, max, now) = part == AdjustPart.보스 ? (MinBoss, MaxBoss, BossPercent) : (MinBuffSize, MaxBuffSize, BuffSize);
        return Enumerable.Range(min, max - min + 1).Select(size => part == AdjustPart.보스 ? BossTo(size) : WithBuffSize(size))
            .MinBy(a => (Math.Abs(a.Handle(part).X - target), Math.Abs((part == AdjustPart.보스 ? a.BossPercent : a.BuffSize) - now)))!;
    }

    /// <summary>The boss row's size in % of the game's (100: as the game draws it at this HUD).</summary>
    public int BossPercent => Current.BossSize ?? 100;

    /// <summary>The boss row at <paramref name="percent"/> of the game's size, grown about the slash between its buffs and debuffs;
    /// no bigger than still fits beside it (<see cref="MaxBossPercent"/>).</summary>
    public FitAdjust BossTo(int percent) => this with
    {
        Current = Clamp(Current with { BossSize = Math.Clamp(percent, MinBoss, Math.Max(MinBoss, MaxBossPercent())) }),
    };

    /// <summary>The slider value of <paramref name="part"/>: how far it is moved, in screen pixels.</summary>
    public (double X, double Y) Offset(AdjustPart part) => part switch
    {
        AdjustPart.스킬창 => (Current.NudgeX * K, Current.NudgeY * K),
        AdjustPart.버프 => (Current.BuffShift.X, Current.BuffShift.Y),
        AdjustPart.디버프 => (Current.DebuffShift.X, Current.DebuffShift.Y),
        AdjustPart.전분 => (Panel.X - StartPanel.X, Panel.Y - StartPanel.Y),
        _ => (Current.BossShift.X, Current.BossShift.Y),
    };

    public PixelRect RectOf(AdjustPart part) => RectOf(Current, part);

    PixelRect RectOf(Fit f, AdjustPart part) => part switch
    {
        AdjustPart.스킬창 => Geometry.BarRect(Vp, f),
        AdjustPart.버프 => Geometry.BuffArea(Vp, f, BuffSize, BuffCount),
        AdjustPart.디버프 => Geometry.DebuffArea(Vp, f, BuffSize),
        AdjustPart.전분 => Panel,
        _ => Geometry.BossRow(Vp, f, BossGameSize),
    };

    /// <summary>The biggest boss size whose row still fits in the game area on both sides of the slash, which a size keeps in
    /// place: so <see cref="Clamp"/> never has to push the row (and keep that push after the size shrinks again).</summary>
    int MaxBossPercent()
    {
        var row = Geometry.BossRow(Vp, Current, BossGameSize);
        double scale = Geometry.BossScale(Vp, Current, BossGameSize), game = scale / (BossPercent / 100.0);
        double axis = row.X + Geometry.BossAxis * scale;
        double left = Geometry.BossAxis, right = Geometry.RefBossRow.Width - Geometry.BossAxis;
        double s = Math.Min((axis - Vp.X) / left, (Vp.X + Vp.Width - axis) / right) - 1.0 / Geometry.RefBossRow.Width;   // 1 px for rounding
        return Math.Min(MaxBoss, (int)Math.Floor(s / game * 100));
    }

    /// <summary>What a press at screen pixel (<paramref name="x"/>, <paramref name="y"/>) takes: a size handle (<see cref="Handle"/>,
    /// within <paramref name="handle"/> pixels: <see cref="DragCorner"/>), the bar's first; else the smallest box under it; null over none.</summary>
    public (AdjustPart Part, bool Corner)? PartAt(double x, double y, double handle)
    {
        var bar = RectOf(AdjustPart.스킬창);
        if (Math.Abs(x - bar.X) <= handle && Math.Abs(y - bar.Bottom) <= handle) return (AdjustPart.스킬창, true);
        foreach (var row in new[] { AdjustPart.버프, AdjustPart.디버프, AdjustPart.보스, AdjustPart.전분 })
            if (Handle(row) is var (hx, hy) && Math.Abs(x - hx) <= handle && Math.Abs(y - hy) <= handle) return (row, true);
        var hit = Enum.GetValues<AdjustPart>().Select(p => (Part: p, Rect: RectOf(p)))
            .Where(h => x >= h.Rect.X && x < h.Rect.Right && y >= h.Rect.Y && y < h.Rect.Bottom)
            .OrderBy(h => (long)h.Rect.Width * h.Rect.Height).ToList();
        return hit.Count > 0 ? (hit[0].Part, false) : null;
    }

    Fit Clamp(Fit f)
    {
        f = f with { Hud = Math.Clamp(f.Hud, MinHud, MaxHud) };
        var (dx, dy) = Inside(Geometry.BarRect(Vp, f));
        if ((dx, dy) != (0, 0))
        {
            double k = Geometry.Scale(Vp, f);
            f = f with { NudgeX = f.NudgeX + dx / k, NudgeY = f.NudgeY + dy / k };
        }
        (dx, dy) = Inside(RectOf(f, AdjustPart.버프));
        f = f with { BuffShift = By(f.BuffShift, dx, dy) };
        (dx, dy) = Inside(RectOf(f, AdjustPart.디버프));
        f = f with { DebuffShift = By(f.DebuffShift, dx, dy) };
        (dx, dy) = Inside(RectOf(f, AdjustPart.보스));
        return f with { BossShift = By(f.BossShift, dx, dy) };
    }

    /// <summary><paramref name="r"/> inside the game area, cut down first if it is bigger than the area (a hand-typed spot).</summary>
    PixelRect Placed(PixelRect r)
    {
        r = r with { Width = Math.Min(r.Width, Vp.Width), Height = Math.Min(r.Height, Vp.Height) };
        var (dx, dy) = Inside(r);
        return r with { X = r.X + dx, Y = r.Y + dy };
    }

    /// <summary>How far to move <paramref name="r"/> to put it inside the game area (0 when it is).</summary>
    (int Dx, int Dy) Inside(PixelRect r)
    {
        int right = Vp.X + Vp.Width, bottom = Vp.Y + Vp.Height;
        return (r.X < Vp.X ? Vp.X - r.X : r.Right > right ? right - r.Right : 0,
                r.Y < Vp.Y ? Vp.Y - r.Y : r.Bottom > bottom ? bottom - r.Bottom : 0);
    }

    static Shift By(Shift s, double dx, double dy) => new(s.X + (int)Math.Round(dx), s.Y + (int)Math.Round(dy));
}
