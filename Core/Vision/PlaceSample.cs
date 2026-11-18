using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>예시 보기 in the zoom window (rail-split.plan.md item 5): a crop of a real 1920×1080 game capture (HUD 110, names
/// out of frame), <see cref="Crop"/> its rect in that capture. It is drawn where the game would put it around the box as placed
/// now: held at the box's fixed point (the bar's and rows' corners, the buff row's right end, the boss row's slash) and scaled
/// by the HUD (버프/디버프 by 버프 크기, 보스 by 타겟 UI 버프 크기, the 전분 box by its own height). Not redrawn per resolution: it only shows how to line up.</summary>
public sealed record PlaceSample(string File, PixelRect Crop)
{
    public static readonly IReadOnlyDictionary<AdjustPart, PlaceSample> All = new Dictionary<AdjustPart, PlaceSample>
    {
        // Mordum_layout.png (모르둠, HUD 110); the 암수 one from 빛암 120_110_100.mp4 at 70.5 s (프로켈, HUD 110).
        [AdjustPart.스킬창] = new("sample-skillbar.png", new(462, 900, 633, 180)),
        [AdjustPart.버프] = new("sample-buffs.png", new(470, 850, 540, 130)),
        [AdjustPart.디버프] = new("sample-debuffs.png", new(1030, 850, 450, 130)),
        [AdjustPart.보스] = new("sample-amsu.png", new(620, 120, 760, 150)),
        [AdjustPart.전분] = new("sample-jeonbun.png", new(1400, 850, 520, 170)),
    };

    /// <summary>The captures' own screen: 1080p, HUD 110, 버프 크기 120, 타겟 UI 버프 크기 110 (the 암수 clip), the 전분 box where 모르둠's was.</summary>
    static readonly FitAdjust Source = FitAdjust.Begin(new Viewport(0, 0, 1920, 1080), 120, new Fit(ReferenceHud), bossSize: SourceBossSize, panel: new PixelRect(1447, 877, 470, 114));
    const int SourceBossSize = 110;
    const double ReferenceHud = Geometry.ReferenceHud;

    /// <summary>Where the picture goes on screen, for the box as <paramref name="now"/> places it.</summary>
    public PixelRect At(FitAdjust now, AdjustPart part)
    {
        var (sx, sy) = Anchor(Source, part);
        var (nx, ny) = Anchor(now, part);
        double k = Scale(now, part) / Scale(Source, part);
        return new((int)Math.Round(nx + (Crop.X - sx) * k), (int)Math.Round(ny + (Crop.Y - sy) * k),
            (int)Math.Round(Crop.Width * k), (int)Math.Round(Crop.Height * k));
    }

    static double Scale(FitAdjust a, AdjustPart part) => part switch
    {
        AdjustPart.전분 => a.RectOf(part).Height,
        AdjustPart.보스 => Geometry.BossScale(a.Vp, a.Current, a.BossGameSize),   // the icons grow with 타겟 UI 버프 크기, not the HUD alone
        AdjustPart.버프 or AdjustPart.디버프 => Geometry.BuffScale(a.Vp, a.BuffSize),   // 버프 크기 and the resolution, not the HUD
        _ => Geometry.Scale(a.Vp, a.Current),
    };

    /// <summary>The box's point a size change doesn't move.</summary>
    static (double X, double Y) Anchor(FitAdjust a, AdjustPart part)
    {
        var r = a.RectOf(part);
        return part switch
        {
            AdjustPart.버프 => (r.Right, r.Bottom),
            AdjustPart.보스 => (a.Guides(part)[0].X1, Geometry.BossIconTopY(a.Vp, a.Current)),   // the icons' top, which a size keeps
            _ => (r.X, r.Y),
        };
    }
}
