using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The one place that converts reference coordinates to screen pixels. Capture regions, vision
/// and the overlay preview all use this; nothing else does its own maths.
///
/// Reference space: 1920×1080 viewport, HUD 110%, origin at the viewport's bottom centre (960, 1080).
/// Measured from reference/No cd.png and checked against the 80–110% screenshots.</summary>
public static class Geometry
{
    public const double ReferenceHud = 110;
    public const double ReferenceHeight = 1080;
    private const int RefCenterX = 960, RefBottom = 1080;

    /// <summary>Slot icon rects in reference screen pixels (x, y, w, h), icon only, no frame.</summary>
    private static readonly Dictionary<SlotId, PixelRect> RefSlots = new()
    {
        [SlotId.각성기] = new(514, 991, 56, 55),
        [SlotId.초각스] = new(592, 992, 54, 54),
        [SlotId.일반1] = new(654, 969, 51, 49),
        [SlotId.일반2] = new(706, 969, 51, 49),
        [SlotId.일반3] = new(758, 969, 51, 49),
        [SlotId.일반4] = new(810, 969, 50, 49),
        [SlotId.일반5] = new(681, 1021, 50, 49),
        [SlotId.일반6] = new(732, 1021, 50, 49),
        [SlotId.일반7] = new(783, 1021, 50, 49),
        [SlotId.일반8] = new(834, 1021, 49, 49),
        [SlotId.아덴1] = new(865, 964, 46, 55),
        [SlotId.아덴2] = new(1009, 964, 46, 55),
    };

    /// <summary>Other fixed regions in reference pixels.</summary>
    public static readonly PixelRect RefAwakeningFrame = new(505, 982, 75, 73);   // gold frame around 각성기 (fit anchor)
    // "✳ key" 각성기 변화 banner above 각성기 (505, 955, 77×21, measured on 각성기변화2.png) with 3 px around it for the search.
    public static readonly PixelRect RefAwakeningLabel = new(502, 952, 83, 27);
    public static readonly PixelRect RefEmblem = new(912, 962, 96, 108);          // class emblem without the top ornament (differs between shots)

    public static PixelRect RefSlot(SlotId slot) => RefSlots[slot];

    public static double Scale(Viewport vp, Fit fit) => fit.Hud / ReferenceHud * (vp.Height / ReferenceHeight);

    /// <summary>Maps a reference rect to screen pixels for this viewport and fit.</summary>
    public static PixelRect ToScreen(PixelRect r, Viewport vp, Fit fit)
    {
        double k = Scale(vp, fit);
        double cx = vp.X + vp.Width / 2.0, bottom = vp.Y + vp.Height;
        double x = cx + (r.X - RefCenterX + fit.NudgeX) * k;
        double y = bottom + (r.Y - RefBottom + fit.NudgeY) * k;
        return new((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(r.Width * k), (int)Math.Round(r.Height * k));
    }

    /// <summary>The slot's screen rect. <paramref name="own"/>: reference rects a class draws elsewhere (classes.json
    /// "adenRects", e.g. 발키리's round Z and C under the orb).</summary>
    public static PixelRect Slot(SlotId slot, Viewport vp, Fit fit, IReadOnlyDictionary<SlotId, PixelRect>? own = null) =>
        ToScreen(own?.GetValueOrDefault(slot) is { Width: > 0 } r ? r : RefSlots[slot], vp, fit);

    public static string ViewportKey(Viewport vp) => $"{vp.Width}×{vp.Height}";

    /// <summary>The game's forced 21:9: 64:27 in fact (1920×810, 3840×1620).</summary>
    public const double ForcedWide = 64.0 / 27;

    /// <summary>직접 입력: the game area for the typed 해상도, stretched to the game window's client area <paramref name="client"/>. The
    /// forced 21:9 bars the top and bottom of a screen narrower than it; on one as wide or wider the game follows the screen.</summary>
    public static Viewport ManualViewport(PixelRect client, ManualScreen screen)
    {
        double height = screen.Wide && (double)screen.Width / screen.Height < ForcedWide ? screen.Width / ForcedWide : screen.Height;
        int h = (int)Math.Round(height * client.Height / screen.Height);
        return new(client.X, client.Y + (client.Height - h) / 2, client.Width, h);
    }

    /// <summary>What a fit found, for 직접 입력 to start from: the window's size, and 21:9 when the game area is 64:27 with bars above
    /// and below (a cutscene's bars are another ratio).</summary>
    public static ManualScreen Detected(Viewport vp, PixelRect client) =>
        new(client.Width, client.Height, vp.Height < client.Height && Math.Abs((double)vp.Width / vp.Height - ForcedWide) < 0.02);

    // ── Buff row (PLAN §4 버프) ─────────────────────────────────────────────────────────────────────────────────
    // The row's right edge is fixed and new buffs push in from the left. The row is attached to the HUD at its right
    // edge and at a baseline just under the timer text (reference px at HUD 110); the icons scale with the game's own
    // buff size setting × viewport height and not with the HUD. Measured on buff_80_to_110.mp4 (sizes 80–110 at 1080,
    // HUD 110) and buff_gameplay.mp4 (120): the pitch is 22.25, 25.5, 28.75, 32, 35 — linear in the size but not
    // proportional to it — and the icon bottom rises 15 buff px above the baseline (930 at 80 to 924 at 120).
    public const int BuffBoxCount = 5;
    const double RefBuffRight = 845, RefBuffBaseline = 941.6, BuffBottomAboveBaseline = 15;
    // Pitch at a 1080-high viewport = BuffPitchPerSize × size/100 + BuffPitchOffset; the gold frame is BuffIconShare of it.
    const double BuffPitchPerSize = 32.5, BuffPitchOffset = -3.75, BuffIconShare = 0.885;
    // The timer text's band below the frame, per unit of buff scale.
    const double BuffTimerTop = 4.5, BuffTimerBottom = 15.5;

    /// <summary>One buff box: its icon (gold frame included) and the timer text under it.</summary>
    public readonly record struct BuffBox(PixelRect Icon, PixelRect Timer);

    /// <summary>Screen pixels per buff-reference pixel for the game's buff size setting (80–120%).</summary>
    public static double BuffScale(Viewport vp, int buffSize) => buffSize / 100.0 * vp.Height / ReferenceHeight;

    /// <summary>The <see cref="BuffBoxCount"/> rightmost buff boxes, box 0 rightmost, in screen pixels.</summary>
    public static BuffBox[] BuffBoxes(Viewport vp, Fit fit, int buffSize)
    {
        double k = Scale(vp, fit), s = BuffScale(vp, buffSize);
        double pitch = (BuffPitchPerSize * buffSize / 100.0 + BuffPitchOffset) * vp.Height / ReferenceHeight, icon = BuffIconShare * pitch;
        double right = vp.X + vp.Width / 2.0 + (RefBuffRight - RefCenterX + fit.NudgeX) * k;
        double bottom = vp.Y + vp.Height + (RefBuffBaseline - RefBottom + fit.NudgeY) * k - BuffBottomAboveBaseline * s;
        int size = (int)Math.Round(icon), top = (int)Math.Round(bottom - icon);
        int timerTop = (int)Math.Round(bottom + BuffTimerTop * s), timerBottom = (int)Math.Round(bottom + BuffTimerBottom * s);
        return [.. Enumerable.Range(0, BuffBoxCount).Select(i =>
        {
            double r = right - i * pitch;
            int x = (int)Math.Round(r - icon), tx = (int)Math.Round(r - (icon + pitch) / 2);
            return new BuffBox(new(x, top, size, size), new(tx, timerTop, (int)Math.Round(pitch), timerBottom - timerTop));
        })];
    }

    /// <summary>The one rect to capture for the buff row: every box and timer.</summary>
    public static PixelRect BuffArea(Viewport vp, Fit fit, int buffSize)
    {
        var b = BuffBoxes(vp, fit, buffSize);
        int x = b.Min(r => r.Timer.X), y = b.Min(r => r.Icon.Y);
        return new(x, y, b.Max(r => r.Timer.Right) - x, b.Max(r => r.Timer.Bottom) - y);
    }

    // ── Debuff row and boss counts (PLAN §4 디버프) ───────────────────────────────────────────────────────────────────
    // The debuff row mirrors the buff row about the HUD's centre: its left edge is fixed and new debuffs are added on the
    // right, at the buff row's size and height (mordum.png: frames from 1074, pitch 35, at buff 120%).

    /// <summary>The <see cref="BuffBoxCount"/> leftmost debuff boxes, box 0 leftmost, in screen pixels.</summary>
    public static BuffBox[] DebuffBoxes(Viewport vp, Fit fit, int buffSize)
    {
        double axis = 2 * (vp.X + vp.Width / 2.0 + fit.NudgeX * Scale(vp, fit));
        PixelRect Mirror(PixelRect r) => r with { X = (int)Math.Round(axis - r.X - r.Width) };
        return [.. BuffBoxes(vp, fit, buffSize).Select(b => new BuffBox(Mirror(b.Icon), Mirror(b.Timer)))];
    }

    /// <summary>The one rect to capture for the debuff row.</summary>
    public static PixelRect DebuffArea(Viewport vp, Fit fit, int buffSize)
    {
        var b = DebuffBoxes(vp, fit, buffSize);
        int x = b.Min(r => r.Timer.X), y = b.Min(r => r.Icon.Y);
        return new(x, y, b.Max(r => r.Timer.Right) - x, b.Max(r => r.Timer.Bottom) - y);
    }

    /// <summary>The strip under the boss's HP bar holding its debuff icons and their "X n" counts, reference pixels
    /// from the top of the viewport (mordum.png: the sigils at 845 and 880, y 169, the counts 14 px above them).</summary>
    public static readonly PixelRect RefBossRow = new(700, 140, 540, 70);

    /// <summary><see cref="RefBossRow"/> on screen.</summary>
    // ponytail: assumes the top UI scales like the HUD (only a HUD 110% 1080p shot exists); re-measure at another HUD size.
    public static PixelRect BossRow(Viewport vp, Fit fit)
    {
        double k = Scale(vp, fit), cx = vp.X + vp.Width / 2.0;
        var r = RefBossRow;
        return new((int)Math.Round(cx + (r.X - RefCenterX) * k), (int)Math.Round(vp.Y + r.Y * k),
            (int)Math.Round(r.Width * k), (int)Math.Round(r.Height * k));
    }
}
