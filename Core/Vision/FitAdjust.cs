using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>What 수동 조정 moves: the skill bar as a whole (its fit), or one of the rows that follow it.</summary>
public enum AdjustPart { 스킬창, 버프, 디버프, 보스 }

/// <summary>수동 조정's live edit in the debug window: <see cref="Start"/> is the fit it opened on, <see cref="Current"/> the one
/// shown. The bar moves by its nudge (reference pixels; shown as screen pixels that its scaling never changes) and scales by its
/// HUD %, the rows by their own shifts (screen pixels); the buff and debuff rows are sized by the game's 버프 크기
/// (<see cref="BuffSize"/>, a 설정 value, not part of the fit), the boss row by its own % (<see cref="Fit.BossSize"/>). Every edit
/// is clamped: HUD <see cref="MinHud"/>–<see cref="MaxHud"/>, the bar and every row inside the game area. A drag should apply its
/// whole distance to the session it started on, so rounding never drifts. Immutable.</summary>
public sealed record FitAdjust(Viewport Vp, int BuffSize, Fit Start, Fit Current, int StartBuffSize)
{
    public const double MinHud = 50, MaxHud = 250;
    /// <summary>The boss row's size range in % of the game's.</summary>
    public const int MinBoss = 50, MaxBoss = 200;
    /// <summary>The 버프 크기 range ± reaches: the game offers <see cref="Settings.BuffSizes"/>, a video (테스트 모드) may show any size.</summary>
    public const int MinBuffSize = 50, MaxBuffSize = 200;

    public static FitAdjust Begin(Viewport vp, int buffSize, Fit fit)
    {
        var a = new FitAdjust(vp, buffSize, fit, fit, buffSize);
        return a with { Current = a.Clamp(fit) };
    }

    public bool Changed => Current != Start || BuffSize != StartBuffSize;

    /// <summary>되돌리기: back to the fit and buff size the window opened on.</summary>
    public FitAdjust Reverted() => this with { Current = Start, BuffSize = StartBuffSize };

    double K => Geometry.Scale(Vp, Current);

    public FitAdjust Move(AdjustPart part, double dx, double dy) => this with
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
        _ => (Current.BossShift.X, Current.BossShift.Y),
    };

    public PixelRect RectOf(AdjustPart part) => RectOf(Current, part);

    PixelRect RectOf(Fit f, AdjustPart part) => part switch
    {
        AdjustPart.스킬창 => Geometry.BarRect(Vp, f),
        AdjustPart.버프 => Geometry.BuffArea(Vp, f, BuffSize),
        AdjustPart.디버프 => Geometry.DebuffArea(Vp, f, BuffSize),
        _ => Geometry.BossRow(Vp, f),
    };

    /// <summary>The biggest boss size whose row still fits in the game area on both sides of the slash, which a size keeps in
    /// place: so <see cref="Clamp"/> never has to push the row (and keep that push after the size shrinks again).</summary>
    int MaxBossPercent()
    {
        var row = Geometry.BossRow(Vp, Current);
        double scale = Geometry.BossScale(Vp, Current), game = scale / (BossPercent / 100.0);
        double axis = row.X + Geometry.BossAxis * scale;
        double left = Geometry.BossAxis, right = Geometry.RefBossRow.Width - Geometry.BossAxis;
        double s = Math.Min((axis - Vp.X) / left, (Vp.X + Vp.Width - axis) / right) - 1.0 / Geometry.RefBossRow.Width;   // 1 px for rounding
        return Math.Min(MaxBoss, (int)Math.Floor(s / game * 100));
    }

    /// <summary>What a press at screen pixel (<paramref name="x"/>, <paramref name="y"/>) takes: the bar's bottom-left corner
    /// (within <paramref name="handle"/> pixels: a resize, <see cref="DragLeftTo"/>), else the smallest box under it; null over none.
    /// The left corner: the bar mostly lies left of the HUD's anchor, so its left edge follows the pointer.</summary>
    public (AdjustPart Part, bool Corner)? PartAt(double x, double y, double handle)
    {
        var bar = RectOf(AdjustPart.스킬창);
        if (Math.Abs(x - bar.X) <= handle && Math.Abs(y - bar.Bottom) <= handle) return (AdjustPart.스킬창, true);
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

    /// <summary>How far to move <paramref name="r"/> to put it inside the game area (0 when it is).</summary>
    (int Dx, int Dy) Inside(PixelRect r)
    {
        int right = Vp.X + Vp.Width, bottom = Vp.Y + Vp.Height;
        return (r.X < Vp.X ? Vp.X - r.X : r.Right > right ? right - r.Right : 0,
                r.Y < Vp.Y ? Vp.Y - r.Y : r.Bottom > bottom ? bottom - r.Bottom : 0);
    }

    static Shift By(Shift s, double dx, double dy) => new(s.X + (int)Math.Round(dx), s.Y + (int)Math.Round(dy));
}
