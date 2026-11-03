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
    // "✳ key" 초각기 표시 banner above 각성기 (505, 955, 77×21, measured on 각성기변화2.png) with 3 px around it for the search.
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
    public const int BuffBoxCount = 7, DebuffBoxCount = 5;
    /// <summary>How many buff boxes 설정 lets the row read (<see cref="BuffSettings.Count"/>; <see cref="BuffBoxCount"/> by default).
    /// The game draws as many as fit and folds the rest into a "+N" icon: 10 at 1080p, buff 120% (mordum.png), 14 at buff 100%
    /// in an 810-high game area (4k_down.mp4).</summary>
    public const int MinBuffBoxCount = 1, MaxBuffBoxCount = 14;
    /// <summary>심판's icon can sit further along the row than the party buffs (user, 2026-10-13).</summary>
    public const int JudgmentBoxCount = 10;
    const double RefBuffRight = 845, BuffBottomAboveBaseline = 15;
    // The baseline sits RefBuffBaseline (HUD-scaled) plus BuffBaselineFixed (viewport-scaled only) from the bottom: 941.6 at HUD
    // 110, and 1.32 px per HUD % (HUD{80..105}_buff120.png: box 0's bottom 964, 950, 937, 931; 924 at 110), not the 1.26 a fully
    // HUD-scaled 941.6 gives.
    const double RefBuffBaseline = 934.2, BuffBaselineFixed = 941.6 - RefBuffBaseline;
    // Pitch at a 1080-high viewport = BuffPitchPerSize × size/100 + BuffPitchOffset; the gold frame is BuffIconShare of it.
    // ponytail: the pitch is measured at 1080p only. The native 4K stills (buff 100%) match the icon size and right edge within
    // 1 px but show one buff, so no pitch; 4k_down (the 21:9 one scaled to 810 high, ~2 px blur) reads 21.86 against this
    // 21.56, which would put box 13 ~3 px off. A native 4K shot with 10+ buffs settles it.
    const double BuffPitchPerSize = 32.5, BuffPitchOffset = -3.75, BuffIconShare = 0.885;
    // The timer text's band below the frame, per unit of buff scale.
    const double BuffTimerTop = 4.5, BuffTimerBottom = 15.5;
    // Below HUD 100 the game packs the row tighter while the icons keep their size: the pitch × this, by HUD, straight
    // between the points (HUD{80,90,100,105}_buff120.png: 32.43, 33.9, 34.75, 35 against 35.25; 100 counts as full, as
    // 4k_down's HUD 100 buff 100 pitch is no shorter).
    // ponytail: measured at buff 120% only; if smaller buff sizes drift at a small HUD, measure them and key this by size too.
    static readonly (double Hud, double Share)[] BuffPacking = [(80, 0.920), (90, 0.9617), (100, 1)];

    /// <summary>The share of the full pitch the buff row keeps at HUD <paramref name="hud"/> (<see cref="BuffPacking"/>).</summary>
    static double PackingAt(double hud)
    {
        if (hud <= BuffPacking[0].Hud) return BuffPacking[0].Share;
        for (int i = 1; i < BuffPacking.Length; i++)
            if (hud <= BuffPacking[i].Hud)
            {
                var (h0, s0) = BuffPacking[i - 1];
                var (h1, s1) = BuffPacking[i];
                return s0 + (s1 - s0) * (hud - h0) / (h1 - h0);
            }
        return BuffPacking[^1].Share;
    }

    /// <summary>One buff box: its icon (gold frame included) and the timer text under it.</summary>
    public readonly record struct BuffBox(PixelRect Icon, PixelRect Timer);

    /// <summary>Screen pixels per buff-reference pixel for the game's buff size setting (80–120%).</summary>
    public static double BuffScale(Viewport vp, int buffSize) => buffSize / 100.0 * vp.Height / ReferenceHeight;

    /// <summary>The buff row's full pitch (no packing) in screen pixels at the game's buff size.</summary>
    static double FullPitch(Viewport vp, int buffSize) => (BuffPitchPerSize * buffSize / 100.0 + BuffPitchOffset) * vp.Height / ReferenceHeight;

    /// <summary>The <paramref name="count"/> rightmost buff boxes, box 0 rightmost, in screen pixels.</summary>
    public static BuffBox[] BuffBoxes(Viewport vp, Fit fit, int buffSize, int count = BuffBoxCount)
    {
        double k = Scale(vp, fit), s = BuffScale(vp, buffSize);
        double full = FullPitch(vp, buffSize), icon = BuffIconShare * full;
        double pitch = full * PackingAt(fit.Hud);
        double right = vp.X + vp.Width / 2.0 + (RefBuffRight - RefCenterX + fit.NudgeX) * k + fit.BuffShift.X;
        double bottom = vp.Y + vp.Height + (RefBuffBaseline - RefBottom + fit.NudgeY) * k + BuffBaselineFixed * vp.Height / ReferenceHeight
            - BuffBottomAboveBaseline * s + fit.BuffShift.Y;
        int size = (int)Math.Round(icon), top = (int)Math.Round(bottom - icon);
        int timerTop = (int)Math.Round(bottom + BuffTimerTop * s), timerBottom = (int)Math.Round(bottom + BuffTimerBottom * s);
        return [.. Enumerable.Range(0, count).Select(i =>
        {
            double r = right - i * pitch;
            int x = (int)Math.Round(r - icon), tx = (int)Math.Round(r - (icon + pitch) / 2);
            return new BuffBox(new(x, top, size, size), new(tx, timerTop, (int)Math.Round(pitch), timerBottom - timerTop));
        })];
    }

    /// <summary>The guide lines over a row of <paramref name="boxes"/> (buff or debuff, any order): the x of every edge between two
    /// boxes and of both ends (the timer cells, a pitch wide, tile the row), and the y where the icons end and the timers begin.</summary>
    public static (int[] Columns, int IconBottom) RowGrid(IReadOnlyList<BuffBox> boxes) =>
        ([.. boxes.Select(b => b.Timer.X).Append(boxes.Max(b => b.Timer.Right)).Distinct().Order()], boxes[0].Icon.Bottom);

    /// <summary>The one rect to capture for the buff row: every box and timer, of the first <paramref name="count"/> boxes.</summary>
    public static PixelRect BuffArea(Viewport vp, Fit fit, int buffSize, int count = BuffBoxCount)
    {
        var b = BuffBoxes(vp, fit, buffSize, count);
        int x = b.Min(r => r.Timer.X), y = b.Min(r => r.Icon.Y);
        return new(x, y, b.Max(r => r.Timer.Right) - x, b.Max(r => r.Timer.Bottom) - y);
    }

    // ── 단심's text (the white "심판" over the character) ─────────────────────────────────────────────────────────────
    // The camera keeps the character at the screen's centre: at 1080p its HP bar sat near (923, 373) and the text under it,
    // 75–300% (단심75%.mp4 … 단심300%.mp4), within 440×300 from (740, 320).
    const double JudgmentBandLeft = 220, JudgmentBandUp = 220, JudgmentBandWidth = 440, JudgmentBandHeight = 300;

    /// <summary>The rect round the character where "심판" is written, in screen pixels; it scales with the viewport only.</summary>
    public static PixelRect JudgmentBand(Viewport vp)
    {
        double k = vp.Height / ReferenceHeight;
        return new((int)Math.Round(vp.X + vp.Width / 2.0 - JudgmentBandLeft * k), (int)Math.Round(vp.Y + vp.Height / 2.0 - JudgmentBandUp * k),
            (int)Math.Round(JudgmentBandWidth * k), (int)Math.Round(JudgmentBandHeight * k));
    }

    // ── Debuff row and boss counts (PLAN §4 디버프) ───────────────────────────────────────────────────────────────────
    // The debuff row mirrors the buff row about the HUD's centre: its left edge is fixed and new debuffs are added on the
    // right, at the buff row's size and height (mordum.png: frames from 1074, pitch 35, at buff 120%).

    /// <summary>The <see cref="DebuffBoxCount"/> leftmost debuff boxes, box 0 leftmost, in screen pixels.</summary>
    public static BuffBox[] DebuffBoxes(Viewport vp, Fit fit, int buffSize)
    {
        double axis = 2 * (vp.X + vp.Width / 2.0 + fit.NudgeX * Scale(vp, fit));
        var (dx, dy) = (fit.DebuffShift.X, fit.DebuffShift.Y);   // its own shift, not the buff row's
        PixelRect Mirror(PixelRect r) => r with { X = (int)Math.Round(axis - r.X - r.Width) + dx, Y = r.Y + dy };
        return [.. BuffBoxes(vp, fit with { BuffShift = default }, buffSize, DebuffBoxCount).Select(b => new BuffBox(Mirror(b.Icon), Mirror(b.Timer)))];
    }

    /// <summary>The one rect to capture for the debuff row.</summary>
    public static PixelRect DebuffArea(Viewport vp, Fit fit, int buffSize, int count = DebuffBoxCount)
    {
        var b = DebuffBoxes(vp, fit, buffSize)[..Math.Clamp(count, 1, DebuffBoxCount)];
        int x = b.Min(r => r.Timer.X), y = b.Min(r => r.Icon.Y);
        return new(x, y, b.Max(r => r.Timer.Right) - x, b.Max(r => r.Timer.Bottom) - y);
    }

    /// <summary>The strip under the boss's HP bar holding its debuff icons and their "X n" counts, reference pixels
    /// from the top of the viewport (mordum.png: the sigils at 845 and 880, y 169, the counts 14 px above them).</summary>
    public static readonly PixelRect RefBossRow = new(700, 140, 540, 70);

    /// <summary>The game's boss effect size every boss shot was taken at (mordum.png, bossHP_hud80/100.png): the size
    /// <see cref="RefBossRow"/> is measured at. The boss row has its own size in the game, apart from 버프 크기 (user, 2026-10-23).</summary>
    public const int RefBossSize = 120;

    /// <summary>Screen pixels per reference pixel for the boss's effect icons and their counts. Like the buff row's icons they
    /// don't shrink with the HUD (31.7 px at HUD 110, 29.4 at 80): the viewport height and the same packing as the buff row,
    /// times the game's boss size (<paramref name="bossSize"/>, <see cref="BossSizeShare"/>). A 수동 조정 boss size
    /// (<see cref="Fit.BossSize"/>, % of it) scales it. Both grow the icons about the slash (<see cref="BossRow"/>): it is the
    /// middle of the "모든 면역 / 관통 면역" text, which an icon size doesn't move.</summary>
    public static double BossScale(Viewport vp, Fit fit, int bossSize = RefBossSize) =>
        GameBossScale(vp, fit) * BossSizeShare(bossSize) * (fit.BossSize ?? 100) / 100.0;

    static double GameBossScale(Viewport vp, Fit fit) => vp.Height / ReferenceHeight * PackingAt(fit.Hud);

    /// <summary>The boss icons' size at the game's boss size <paramref name="bossSize"/> against <see cref="RefBossSize"/>, by the buff
    /// row's measured pitch law (linear in the size, not proportional to it): the boss icon at 120% is the buff icon's size there
    /// (31.7 px against 31.2 at 1080p). The boss row's own spacing (<see cref="BossPitch"/>) runs up to 3% wider below 120%.</summary>
    public static double BossSizeShare(int bossSize) =>
        (BuffPitchPerSize * bossSize / 100.0 + BuffPitchOffset) / (BuffPitchPerSize * RefBossSize / 100.0 + BuffPitchOffset);

    /// <summary>The boss row's icon spacing in screen px at 1080p and HUD 100 or more, measured on 빛암 80_90 / 120_110_100: the
    /// debuffs and the green boss buffs both 22, 25, 28, 31, 34 px apart at 80–120% (frames 2 px apart).</summary>
    public static double BossPitch(Viewport vp, Fit fit, int bossSize) =>
        (0.3 * bossSize - 2) * GameBossScale(vp, fit) * (fit.BossSize ?? 100) / 100.0;

    /// <summary>Where in <see cref="RefBossRow"/> (reference px from its left) the boss's buffs and debuffs mirror: the slash in
    /// "모든 면역 / 관통 면역" at x 919. A 수동 조정 icon size keeps it in place.</summary>
    public const double BossAxis = 919 - 700;

    /// <summary><see cref="RefBossRow"/> on screen: placed by the HUD from the top centre (the boss bar and the top of its effect
    /// row follow the HUD: bossHP_hud80/100.png, HUD80_buff120.png), and as big as <see cref="BossScale"/> makes the icons, with
    /// the sigils' row where it was at HUD 110 and the slash (<see cref="BossAxis"/>) where the game's size puts it.</summary>
    public static PixelRect BossRow(Viewport vp, Fit fit, int bossSize = RefBossSize)
    {
        double k = Scale(vp, fit), s0 = GameBossScale(vp, fit), s = BossScale(vp, fit, bossSize), cx = vp.X + vp.Width / 2.0;
        var r = RefBossRow;
        double iconTop = vp.Y + (r.Y + DebuffReader.BossIconTop) * k;
        double left = cx + (r.X - RefCenterX) * k + BossAxis * (s0 - s);
        return new((int)Math.Round(left) + fit.BossShift.X, (int)Math.Round(iconTop - DebuffReader.BossIconTop * s) + fit.BossShift.Y,
            (int)Math.Round(r.Width * s), (int)Math.Round(r.Height * s));
    }

    /// <summary>The most icons the boss debuff zone shows before folding the rest into "+N": 14 at 보스 디버프 크기 100% and under,
    /// 12 at 110%, 10 at 120% (user, 2026-11-01).</summary>
    public static int MaxBossDebuffs(int bossSize) => bossSize > 110 ? 10 : bossSize > 100 ? 12 : 14;

    /// <summary>The boss debuff zone's first icon, reference px at HUD 110 from the left: it stays put at every 보스 디버프 크기
    /// (x 927–929 at 1080p, HUD 100, reference/빛암) while the boss's buffs grow to its left.</summary>
    const int RefFirstBossDebuffX = 924;

    /// <summary>The boss debuff zone inside <see cref="BossRowSearch"/>'s strip: where its first slot starts, and how many it shows.</summary>
    public static BossSlots BossSlotsIn(Viewport vp, Fit fit, int bossSize) =>
        new(FirstBossDebuffX(vp, fit) - BossRowSearch(vp, fit, bossSize).X, MaxBossDebuffs(bossSize));

    /// <summary>The boss debuff zone's first icon's left edge on screen (<see cref="RefFirstBossDebuffX"/>): the 암수 walk starts there.</summary>
    public static int FirstBossDebuffX(Viewport vp, Fit fit) =>
        (int)Math.Round(vp.X + vp.Width / 2.0 + (RefFirstBossDebuffX - RefCenterX) * Scale(vp, fit) + fit.BossShift.X);

    /// <summary>Reference px searched above and below the expected icon line (<see cref="BossRowSearch"/>).</summary>
    public const int BossRowPad = 12;

    /// <summary>Reference px (at the HUD's scale) more below, for the "N초" under the icons. Once read as the stagger bar or the lock
    /// line pushing the row down 15–16 px: the 빛암 clips were HUD 110, not the 100 assumed (2026-11-04); nothing under the bar moves
    /// the row (the icons' top at 169 with a purple bar, a lock line or neither). The 암수 reads <see cref="BossDebuffStrip"/>.</summary>
    public const int BossRowDrop = 24;

    /// <summary><see cref="BossRow"/> with <see cref="BossRowPad"/> more above and below, and <see cref="BossRowDrop"/> more below:
    /// the debuff row is found inside it (DebuffReader.FindRowY) rather than assumed, since it moves with the boss bar (HUD, 21:9,
    /// resolution) and with what shows under the bar.</summary>
    public static PixelRect BossRowSearch(Viewport vp, Fit fit, int bossSize = RefBossSize)
    {
        var r = BossRow(vp, fit, bossSize);
        double s = BossScale(vp, fit, bossSize);
        int pad = (int)Math.Round(BossRowPad * s), drop = (int)Math.Round(BossRowDrop * GameBossScale(vp, fit));
        // Out to the last debuff slot the size shows at the row's measured spacing, and a pad past it.
        double pitch = Math.Max(BossPitch(vp, fit, bossSize), DebuffReader.BossSlotPitch * s);
        int right = Math.Max(r.Right, (int)Math.Ceiling(FirstBossDebuffX(vp, fit) + MaxBossDebuffs(bossSize) * pitch + BossRowPad * s));
        return new(r.X, r.Y - pad, right - r.X, r.Height + 2 * pad + drop);
    }

    /// <summary>The 암수 strip: the boss debuff row and the "N초" under it, no taller. The row's top is set by the HUD and the
    /// resolution only (its icons grow downward with 보스 디버프 크기; 21:9 the same: the clips, 2026-11-04), so it is placed, not
    /// searched for: <see cref="BossDebuffLine"/> px from the top, with <see cref="BossLineSnap"/> px either side for a snap.
    /// Same left and width as <see cref="BossRowSearch"/>, so the slots (<see cref="BossSlotsIn"/>) count alike.</summary>
    public static PixelRect BossDebuffStrip(Viewport vp, Fit fit, int bossSize = RefBossSize)
    {
        var search = BossRowSearch(vp, fit, bossSize);
        int top = BossIconTopY(vp, fit) - BossDebuffLine;
        double icon = DebuffReader.BossIconSize * BossScale(vp, fit, bossSize), text = (BossTimerBelow) * BuffScale(vp, bossSize);
        int bottom = (int)Math.Ceiling(BossIconTopY(vp, fit) + icon + text) + BossLineSnap + 1;
        return new(search.X, top, search.Width, bottom - top);
    }

    /// <summary>The row's line (the icons' top) in <see cref="BossDebuffStrip"/>, strip px: room for the walk's row above it
    /// and the snap.</summary>
    public const int BossDebuffLine = BossLineSnap + 2;

    /// <summary>Strip px the line may be off its placed spot (rounding at odd resolutions): it is snapped once, to the frames.</summary>
    public const int BossLineSnap = 2;

    /// <summary>Reference px under the icons for the "N초" (AmsuReader's timer box, 14 high, and a margin).</summary>
    const double BossTimerBelow = 18;

    /// <summary>The boss debuff icons' top on screen: <see cref="RefBossRow"/>'s icon line at the HUD's scale from the top
    /// (169 at 1080p, HUD 110), as <see cref="BossRow"/> places it.</summary>
    static int BossIconTopY(Viewport vp, Fit fit) =>
        (int)Math.Round(vp.Y + (RefBossRow.Y + DebuffReader.BossIconTop) * Scale(vp, fit)) + fit.BossShift.Y;

    // ── Boss HP bar (project B) ─────────────────────────────────────────────────────────────────────────────────────
    // Reference px at HUD 110 from the viewport's top, measured on mordum.png and Mordum_layout(_21_9).png; the bar follows the
    // HUD about the top centre (bossHP_hud100/80.png: exactly 100/110 and 80/110 of it). A raid bar's fill spans x 576–1299,
    // y 69–94; a portrait (21:9) or a field boss (the dummy: 640–1232) starts or ends it sooner, but "X n" sits at its right end.

    public static readonly PixelRect RefBossBar = new(576, 69, 724, 26);
    /// <summary>"X n" at the bar's right end ("X 494": x 1257–1294, y 76–87), with room for two more digits.</summary>
    public static readonly PixelRect RefBossCount = new(1236, 72, 63, 20);
    /// <summary>"current/max" HP, centred near x 917 (mordum: 765–1068), between "Lv.60" and "X n".</summary>
    public static readonly PixelRect RefBossHpText = new(650, 72, 580, 20);
    /// <summary>The boss's name above the bar ("심연의 징벌자, 모르둠": x 833–999, y 39–54; 40–57 in the layout shots), wide enough for a longer name.</summary>
    public static readonly PixelRect RefBossName = new(720, 32, 400, 30);

    /// <summary>The bar's whole text row ("Lv.60", "current/max", "X n"), from left of the 16:9 HP text to right of its "X n" and
    /// a little above and below: a 21:9 game at 2560×1080 draws a shorter bar further left and its text ~10 px higher (bosstop_2560),
    /// so the fixed areas above miss there. BossDigits.ReadBar finds the words in it.</summary>
    public static readonly PixelRect RefBossTextRow = new(590, 60, 730, 36);

    public static PixelRect BossTextRow(Viewport vp, Fit fit) => FromTop(RefBossTextRow, vp, fit);
    public static PixelRect BossBar(Viewport vp, Fit fit) => FromTop(RefBossBar, vp, fit);
    public static PixelRect BossCount(Viewport vp, Fit fit) => FromTop(RefBossCount, vp, fit);
    public static PixelRect BossHpText(Viewport vp, Fit fit) => FromTop(RefBossHpText, vp, fit);
    public static PixelRect BossName(Viewport vp, Fit fit) => FromTop(RefBossName, vp, fit);

    /// <summary>A reference rect placed from the viewport's top centre by the HUD, moved by the 수동 조정 boss shift.</summary>
    static PixelRect FromTop(PixelRect r, Viewport vp, Fit fit)
    {
        double k = Scale(vp, fit), cx = vp.X + vp.Width / 2.0;
        return new((int)Math.Round(cx + (r.X - RefCenterX) * k) + fit.BossShift.X, (int)Math.Round(vp.Y + r.Y * k) + fit.BossShift.Y,
            (int)Math.Round(r.Width * k), (int)Math.Round(r.Height * k));
    }

    // ── 수동 조정 ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The skill bar as one box in reference pixels: every slot and the 초각기 banner.</summary>
    public static readonly PixelRect RefBar = new(502, 952, 1055 - 502, 1070 - 952);

    public static PixelRect BarRect(Viewport vp, Fit fit) => ToScreen(RefBar, vp, fit);

    /// <summary><paramref name="fit"/> at HUD <paramref name="hud"/>, scaled about the HUD's anchor (the game area's bottom centre):
    /// its nudge changed so the move in screen pixels (nudge × scale) stays the same.</summary>
    public static Fit Rescale(Viewport vp, Fit fit, double hud)
    {
        double ratio = Scale(vp, fit) / Scale(vp, fit with { Hud = hud });
        return fit with { Hud = hud, NudgeX = fit.NudgeX * ratio, NudgeY = fit.NudgeY * ratio };
    }

    /// <summary>The HUD % that puts the bar's left edge on screen x <paramref name="left"/>, its move in screen pixels kept.</summary>
    public static double HudForLeft(Viewport vp, Fit fit, double left)
    {
        double k = (vp.X + vp.Width / 2.0 + fit.NudgeX * Scale(vp, fit) - left) / (RefCenterX - RefBar.X);
        return k / (vp.Height / ReferenceHeight) * ReferenceHud;
    }

    /// <summary>The HUD % that makes the bar <paramref name="width"/> screen pixels wide.</summary>
    public static double HudForWidth(Viewport vp, double width) => width / RefBar.Width / (vp.Height / ReferenceHeight) * ReferenceHud;

    /// <summary>The top third of the game area: the boss's bar and effect row, for the debug window's picture.</summary>
    public static PixelRect BossBand(Viewport vp) => new(vp.X, vp.Y, vp.Width, vp.Height / 3);
}
