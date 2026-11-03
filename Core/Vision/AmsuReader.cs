using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The two bombs a party throws at the boss (amsu.design.md): 20 s and 25 s.</summary>
public enum Bomb { 암수, 빛암 }

/// <summary>The boss debuff zone in a strip: its first slot's left edge (strip px, Geometry.FirstBossDebuffX) and the most slots
/// it shows (Geometry.MaxBossDebuffs).</summary>
public sealed record BossSlots(int FirstX, int Most);

/// <summary>A bomb's icon found in the boss row strip (strip px).</summary>
public sealed record BombAt(Bomb Kind, PixelRect Icon, int? Slot = null);

/// <summary>A bomb and its seconds left ("N초" under it; null when unread).</summary>
public sealed record BombRead(Bomb Kind, double? Seconds, int? Slot = null);

/// <summary>What one read of the boss row gave: the bombs in it; <paramref name="Full"/>: every slot the size shows is taken (a
/// bomb missing may be behind "+N"); <paramref name="Seen"/>: the row was on screen (any red or green frame on its line), not
/// hidden by a menu or a cutscene.</summary>
public sealed record AmsuRow(IReadOnlyList<BombRead> Bombs, bool Full = false, bool Seen = true);

/// <summary>암흑수류탄 / 빛나는 암흑수류탄 in the boss debuff row (Geometry.BossRowSearch, the line from DebuffReader.FindRowY settled
/// by RowLock). Icons first and cheap (<see cref="FindBombs"/>, every read); the "N초" under a found bomb only (<see cref="ReadSeconds"/>).
/// Icons are matched as the boss's sigils are (BuffReader.Match: structure and hue, frame left out) along the line. Pure.</summary>
public static class AmsuReader
{
    static readonly Image?[] Templates = [.. AmsuTemplates.Icons.Select(s => s is null ? null : Image.FromBytes(Convert.FromBase64String(s)))];
    static readonly float[]?[] Thumbs = [.. Templates.Select(t => t is null ? null : BuffReader.Thumb(t))];
    static readonly double[] Hues = [.. Templates.Select(t => t is null ? 0 : SlotClassifier.ColourStats(t).MeanHue)];
    const int Icon = DebuffReader.BossIconSize, Pitch = DebuffReader.BossSlotPitch, Slack = 8;   // ±8: at 120% a slot's frame peaks 28 px after the last, not 34 (amsu_live_120)

    /// <summary>The bomb's icon as matched (the 암수 창 draws it); null while it has no template (빛암, amsu.plan.md).</summary>
    public static Image? IconOf(Bomb b) => Templates[(int)b];
    // Measured on amsu_15/25/45/55 (2560×1080, 보스 디버프 크기 100): 암수 scores 0.93 in each, under a damage number too (45); the
    // best other icon 0.54 (any hue) / 0.40 (a hue within MaxHueDistance), amsu_35 (no 암수) 0.38 at best. Midway: margins ±0.2.
    internal const double MinBomb = 0.72;
    // A slot placed on its frame (OnFrame) holds a debuff from this share: 0.45–0.88 on bitam_*/amsu_*; an empty slot whose window
    // catches the next icon's frame along its edge 0.27 at most, and the art inside green-framed buffs as much.
    const double MinSlotFrame = 0.35;

    // An icon's frame line: a column of its height nearly all red. Measured on 빛암 80_90 / 120_110_100 (1080p, HUD 100): the
    // right line 19, 22, 25, 28, 31 screen px after the left at 80–120%, 30.1–31.0 reference px at every size, and the next
    // icon's left line ending 35–36 ref px after it; so the right line is looked for up to 33. The next one starts 1–6 px after.
    const double LineShare = 0.75;
    const int MinWidth = 23, MaxWidth = 33, Gap = 2, MaxGap = 6;
    // A frame's top and bottom border run inward from its side lines: the column after a left line is red in those rows, the
    // gap after a right line is not (scene behind it).
    const double BorderShare = 0.15;

    /// <summary>The bombs' icons on the line <paramref name="rowY"/> (strip px) of <paramref name="strip"/>, captured at
    /// <paramref name="scale"/> screen px per reference px; each the best window within an icon's width.</summary>
    public static IReadOnlyList<BombAt> FindBombs(Image strip, double scale, int rowY, BossSlots slots) =>
        FindOn(DebuffReader.ToReference(strip, scale), scale, rowY, slots).Bombs;

    /// <summary>The bombs found on a line, whether every slot the size shows was taken, and whether the row was there at all.</summary>
    public sealed record RowFind(int? Y, IReadOnlyList<BombAt> Bombs, bool Full, bool Seen, int Filled = 0)
    {
        /// <summary>No bomb found: the first line tried that holds the boss's red-framed debuffs from the zone's first slot (the
        /// row's line, which only the game's settings move), else null. With a bomb: <see cref="Y"/>.</summary>
        public int? FramedY { get; init; }
    }

    /// <summary>The bombs and the line they were found on (strip px): <paramref name="lockedY"/> first (RowLock's), then, when
    /// <paramref name="search"/>, DebuffReader.FindRowYs' candidates; the first line with a bomb wins. None found: the best
    /// candidate (or the locked line) and no bombs.</summary>
    /// <remarks><c>Full</c>: every slot the size shows holds a debuff, so a bomb missing from the row may be behind "+N" (else
    /// it is gone). <c>Seen</c>: a red or green frame on the line, so the row is on screen. Without a bomb, both are the locked
    /// line's (no locked line: not seen).</remarks>
    public static RowFind Locate(Image strip, double scale, BossSlots slots, int? lockedY, bool search = true) =>
        LocateIn(DebuffReader.ToReference(strip, scale), scale, slots, lockedY, search);

    /// <summary><see cref="Locate"/> on the strip already at reference size (<see cref="DebuffReader.ToReference"/>): a pass that
    /// walks and then identifies resizes the strip once.</summary>
    internal static RowFind LocateIn(Image r, double scale, BossSlots slots, int? lockedY, bool search = true)
    {
        (bool Full, bool Seen, int Filled) locked = (false, false, 0);
        int? framed = null;
        if (lockedY is { } l)
        {
            var there = FindOn(r, scale, l, slots);
            if (there.Bombs.Count > 0) return new(l, there.Bombs, there.Full, true, there.Filled) { FramedY = l };   // the usual case: one line matched
            locked = (there.Full, there.Seen, there.Filled);
            if (there.Filled > 0) framed = l;
        }
        if (!search) return new(lockedY, [], locked.Full, locked.Seen, locked.Filled) { FramedY = framed };
        var lines = DebuffReader.RowYs(r, scale);
        foreach (int y in lines.Where(c => lockedY is not { } l2 || Math.Abs(c - l2) > 2))
        {
            var found = FindOn(r, scale, y, slots);
            if (found.Bombs.Count > 0) return new(y, found.Bombs, found.Full, true, found.Filled) { FramedY = y };
            if (found.Filled > 0) framed ??= y;
        }
        return new(lines.Count > 0 ? lines[0] : null, [], locked.Full, locked.Seen, locked.Filled) { FramedY = framed };
    }

    /// <summary>A debuff's slot in the boss debuff zone: its number from the layout's first slot and its icon box (strip px).</summary>
    public sealed record SlotAt(int Slot, PixelRect Icon);

    /// <summary>The walk alone (RowCadence's frame pass: frame checks, no icon match): the filled slots on line
    /// <paramref name="rowY"/>, whether every slot the size shows is taken, and whether the row is on screen.</summary>
    public sealed record RowWalk(IReadOnlyList<SlotAt> Filled, bool Full, bool Seen);

    /// <remarks>The whole strip is brought to reference size, as <see cref="Locate"/> does: a band cut out of it resamples on
    /// another sub-pixel grid, and the frame check is sharp enough (0.54 on the frame, 0.25 2 px off) to count the slots apart.</remarks>
    public static RowWalk Walk(Image strip, double scale, BossSlots slots, int rowY) => Walk(DebuffReader.ToReference(strip, scale), scale, rowY, slots);

    /// <summary>The line within <paramref name="reach"/> px of <paramref name="line"/> (strip px) where the walk finds the most
    /// red-framed debuffs from the zone's first slot: another line only when it finds more than the placed one (a tie, an icon's
    /// red art or a damage number keeps the placed line); null when none shows any.</summary>
    internal static int? SnapIn(Image r, double scale, BossSlots slots, int line, int reach)
    {
        var counts = Enumerable.Range(line - reach, 2 * reach + 1).Select(y => (Y: y, Filled: Walk(r, scale, y, slots).Filled.Count)).ToList();
        var best = counts.OrderByDescending(c => c.Filled).ThenBy(c => Math.Abs(c.Y - line)).First();
        int placed = counts.First(c => c.Y == line).Filled;
        return best.Filled == 0 ? null : best.Filled > placed ? best.Y : line;
    }

    /// <summary><see cref="Walk(Image, double, BossSlots, int)"/> on the strip already at reference size.</summary>
    internal static RowWalk WalkIn(Image r, double scale, BossSlots slots, int rowY) => Walk(r, scale, rowY, slots);

    /// <summary>Bombs on line <paramref name="rowY"/> (strip px) of <paramref name="r"/> (the strip at reference size): the walk, then
    /// one match per filled slot (7 debuffs cost 7 matches and one empty slot's frame check).</summary>
    static (List<BombAt> Bombs, bool Full, bool Seen, int Filled) FindOn(Image r, double scale, int rowY, BossSlots slots)
    {
        var walk = Walk(r, scale, rowY, slots);
        int y = (int)Math.Round(rowY / scale);
        var found = new List<BombAt>();
        foreach (var f in walk.Filled)
            if (Around(r, (int)Math.Round(f.Icon.X / scale), y) is { } hit)   // the box where the icon matched: its "N초" is under it
                found.Add(new(hit.Kind, f.Icon with { X = (int)Math.Round(hit.X * scale) }, f.Slot));
        return (found, walk.Full, walk.Seen, walk.Filled.Count);
    }

    /// <summary>The walk over line <paramref name="rowY"/> (strip px) of <paramref name="r"/> (reference size), slot by slot as your
    /// own debuff row is read (RowCadence): from the zone's first slot (screen-fixed, <paramref name="slots"/>), one
    /// <see cref="Pitch"/> on at a time while a debuff's red frame is there, up to the size's most (past it the game shows "+N").</summary>
    static RowWalk Walk(Image r, double scale, int rowY, BossSlots slots)
    {
        int y = (int)Math.Round(rowY / scale), last = r.Width - Icon;
        var filled = new List<SlotAt>();
        if (y < 0 || y + Icon > r.Height || last < 0) return new(filled, false, false);
        // Each column's checks are asked for many times over (the line search goes back and forth): each is worked out once.
        var reds = new double?[r.Width];
        var lines = new bool?[r.Width + 1];
        var borders = new bool?[r.Width + 1];
        double Red(int x) => reds[x] ??= DebuffReader.FrameColours(r, new PixelRect(x, y, Icon, Icon)).Red;
        // Each slot onto its frame: the red share peaks where the window's band lies on the 1–2 px frame lines (0.54 there, 0.25
        // 2 px off, bitam_100), even on a red scene (amsu_15: 0.88 against 0.48 around it). The first slot's place is known to
        // ±4 px (x 927–929 at 1080p, 3 px right of it on the 21:9 clip).
        int? OnFrame(int at) => at - Slack > last || at + Slack < 0 ? null
            : Enumerable.Range(Math.Max(0, at - Slack), Math.Min(at + Slack, last) - Math.Max(0, at - Slack) + 1).MaxBy(Red);
        // Each icon is put on its own frame lines (left and right, the column nearly all red), and the next one looked for just
        // after its right line, so the walk follows the icons' real spacing (31 px at 120%, not Pitch: amsu_live_120_slot3);
        // where no lines are found (a blurred small size) the window's peak and share decide as before.
        bool Line(int x) => x >= 0 && x < r.Width && (lines[x] ??= DebuffReader.FrameColours(r, new PixelRect(x, y + 2, 1, Icon - 4)).Red >= LineShare);
        // A frame's outer line has the gap beside it: a column run of red is icon art (a fire icon at 100%: 34 columns), whose
        // last column would take the next icon's left line for this one's right (clip 빛암 120_110_100 at 102 s).
        int? Edge(int from, int to) => Enumerable.Range(from, Math.Max(0, to - from + 1)).Cast<int?>().FirstOrDefault(x => Line(x!.Value) && !Line(x.Value - 1));
        int? LastEdge(int from, int to) => Enumerable.Range(from, Math.Max(0, to - from + 1)).Cast<int?>().LastOrDefault(x => Line(x!.Value) && !Line(x.Value + 1));
        // An icon whose left line lies in [from, to]: that line and its right one (the last in reach: red icon art makes lines inside).
        (int Left, int Right)? IconIn(int from, int to)
        {
            for (int x = Math.Max(0, from); x <= Math.Min(to, last); x++)
                if (Line(x) && !Line(x - 1) && RightLine(x) is { } right) return (x, right);
            return null;
        }
        // The last line in reach that is a right line: one with the border running on past it is the next icon's left line
        // (icons smaller than the size set, amsu_live_120: the right line 28 px on, the next left line 32).
        int? RightLine(int x)
        {
            for (int to = x + MaxWidth; LastEdge(x + MinWidth, to) is { } e; to = e - 1)
                if (!BorderOn(e + 1)) return e;
            return null;
        }
        bool BorderOn(int x) => x is >= 0 && x < r.Width ? borders[x] ??= Border(x) : Border(x);
        bool Border(int x)
        {
            int red = 0, all = 0;
            foreach (int row in new[] { y - 1, y, y + 1, y + 2, y + Icon - 3, y + Icon - 2, y + Icon - 1, y + Icon })
                if (row >= 0 && row < r.Height && x >= 0 && x < r.Width)
                {
                    all++;
                    if (DebuffReader.FrameColours(r, new PixelRect(x, row, 1, 1)).Red > 0) red++;
                }
            return all > 0 && red / (double)all >= BorderShare;
        }
        // The first slot: the first debuff from the left within a slot of where the layout puts it (테스트 모드's fit put it 20 px
        // off, replay 7): its frame lines (a lone 빛암 there scores 0.34 by the window: AmsuSimulationTests), else the first window
        // over MinSlotFrame, put onto its peak. Buff art never reaches MinSlotFrame; on a red scene the frame stays the peak.
        int guess = (int)Math.Round(slots.FirstX / scale);
        var firstIcon = IconIn(guess - Pitch, guess + Pitch);
        int start = firstIcon?.Left ?? Enumerable.Range(Math.Max(0, guess - Pitch), Math.Max(0, Math.Min(guess + Pitch, last) - Math.Max(0, guess - Pitch) + 1))
            .FirstOrDefault(x => Red(x) >= MinSlotFrame, -1);
        if (start < 0) return new(filled, false, DebuffReader.BuffZone(r, y).Count > 0);
        // One empty slot is passed (a damage number or a popup over one frame, review 2026-11-01); two in a row end the walk.
        int expected = firstIcon?.Left ?? Enumerable.Range(start, Math.Min(Pitch / 2, last - start) + 1).MaxBy(Red), empty = 0, pitch = Pitch;
        int? lined = firstIcon?.Left;
        for (int slot = 0; slot < slots.Most && empty < RowCadence.MissesToEnd; slot++)
        {
            // The next icon: its left line just after the last one's right; else (after an empty slot, or no lines) its lines near
            // where the spacing puts it, else the window's peak there.
            var icon = lined is { } l ? IconIn(l, l) : IconIn(expected - Slack, expected + Slack);
            int? at = icon?.Left ?? lined ?? OnFrame(expected);
            if (at is not { } x || x > last) break;
            int? right = icon?.Right;
            if (right is null && Red(x) < MinSlotFrame)
            {
                (empty, expected, lined) = (empty + 1, expected + pitch, null);   // stay on the grid, not on what this window caught
                continue;
            }
            empty = 0;
            filled.Add(new((int)Math.Round((x - guess) / (double)pitch),   // from the layout's first slot: steady when the walk begins early
                new PixelRect((int)Math.Round(x * scale), rowY, (int)Math.Round(Icon * scale), (int)Math.Round(Icon * scale))));
            if (right is { } edge)   // the last line in reach: red icon art makes lines inside
            {
                pitch = edge - x + 1 + Gap;   // the icon's own width and the usual gap
                lined = Edge(edge + 1, edge + MaxGap);
            }
            else lined = null;
            expected = x + pitch;
        }
        // No debuff: the boss's own (green-framed) buffs on the line still show the row is there, and the bomb gone from it.
        return new(filled, filled.Count >= slots.Most, filled.Count > 0 || DebuffReader.BuffZone(r, y).Count > 0);
    }

    static readonly int[] Kinds = [.. Enumerable.Range(0, Thumbs.Length).Where(k => Thumbs[k] is not null)];
    static readonly float[][] KindThumbs = [.. Kinds.Select(k => Thumbs[k]!)];
    static readonly double[] KindHues = [.. Kinds.Select(k => Hues[k])];

    /// <summary>Each template's structure score and hue distance on <paramref name="icon"/> (reference px), in <see cref="Kinds"/> order.</summary>
    internal static (double[] Structure, double[] Hue) Scores(Image r, PixelRect icon)
    {
        var (s, h, _) = BuffReader.Match(r, icon, KindThumbs, KindHues, []);
        return (s, h);
    }

    /// <summary><see cref="Best"/> at the slot's left frame line and up to <see cref="MatchSlack"/> px either side: the templates
    /// sit a px or two inside the frame line, more or less with the size (100%: 1–2 px right of it). The tries overlap (each
    /// matches ±s px around itself), so each box is taken once and its scores reused: 45 boxes instead of 125 at 100%, the
    /// same answer (AmsuMatchTests).</summary>
    internal static (Bomb Kind, double Score, int X)? Around(Image r, int x, int y)
    {
        var inner = BuffReader.Interior(new PixelRect(x, y, Icon, Icon));
        int s = Math.Max(1, inner.Width / 12), span = MatchSlack + s, w = 2 * span + 1;
        var boxes = new (double[] Ncc, double Hue)?[w * (2 * s + 1)];
        (double[] Ncc, double Hue)? Box(int dx, int dy)   // dx from inner.X (−span…span), dy from inner.Y; null off the image
        {
            var rect = inner with { X = inner.X + dx, Y = inner.Y + dy };
            if (rect.X < 0 || rect.Y < 0 || rect.Right > r.Width || rect.Bottom > r.Height) return null;
            ref var box = ref boxes[(dy + s) * w + dx + span];
            if (box is null)
            {
                var crop = ImageOps.Crop(r, rect);
                var thumb = BuffReader.Thumb(crop);
                box = ([.. KindThumbs.Select(t => BuffReader.ChannelNcc(thumb, t))], SlotClassifier.ColourStats(crop).MeanHue);
            }
            return box;
        }
        (Bomb Kind, double Score, int X)? hit = null;
        var best = new double[Kinds.Length];
        var hue = new double[Kinds.Length];
        for (int at = -MatchSlack; at <= MatchSlack; at++)
        {
            // One try, as BuffReader.Match then Best do it: each kind's best box (the first on a tie), its hue, the kind over the bar.
            Array.Fill(best, -2.0);
            for (int dy = -s; dy <= s; dy++)
                for (int dx = -s; dx <= s; dx++)
                {
                    if (Box(at + dx, dy) is not { } b) continue;
                    for (int k = 0; k < Kinds.Length; k++)
                        if (b.Ncc[k] > best[k]) (best[k], hue[k]) = (b.Ncc[k], BuffReader.HueDistance(b.Hue, KindHues[k]));
                }
            int pick = -1;
            for (int k = 0; k < Kinds.Length; k++)
                if (hue[k] <= BuffReader.MaxHueDistance && best[k] >= MinBomb && (pick < 0 || best[k] > best[pick])) pick = k;
            if (pick >= 0 && (hit is null || best[pick] > hit.Value.Score)) hit = ((Bomb)Kinds[pick], best[pick], x + at);
        }
        return hit;
    }

    const int MatchSlack = 2;

    /// <summary>The bomb whose template scores highest on <paramref name="icon"/> (reference px), and its score; null below
    /// <see cref="MinBomb"/> or off in hue.</summary>
    internal static (Bomb Kind, double Score)? Best(Image r, PixelRect icon)
    {
        var (s, h) = Scores(r, icon);
        int best = -1;
        for (int i = 0; i < Kinds.Length; i++)
            if (h[i] <= BuffReader.MaxHueDistance && s[i] >= MinBomb && (best < 0 || s[i] > s[best])) best = i;
        return best < 0 ? null : ((Bomb)Kinds[best], s[best]);
    }

    /// <summary>The seconds under <paramref name="at"/>; <paramref name="textScale"/> = Geometry.BuffScale at the 보스 디버프 크기.</summary>
    public static double? ReadSeconds(Image strip, BombAt at, double textScale)
    {
        var box = at.Icon;
        int top = box.Bottom, height = (int)Math.Round(TimerHeight * textScale), pad = (int)Math.Round(TimerPad * textScale);
        // Wider on the right (TimerReach): a box a px or two left of its icon cut the "초" off and read nothing; the next icon's text
        // starts well past it.
        int reach = (int)Math.Round(TimerReach * textScale);
        if (Math.Min(height, strip.Height - top) <= 0) return null;
        // Both scales, a value only when they agree or one is unread, and never more than the bomb lasts: a scale just off reads
        // a digit short ("22" as "2" at 110% × 0.95), the same way every read, so the countdown's second read can't catch it. The
        // window at the box first, then a step either side (the text cut by a box a px or two off its icon: amsu_live_120_slot3).
        int step = Math.Max(1, (int)Math.Round(TimerShift * textScale));
        foreach (int dx in new[] { 0, step, -step })
        {
            int left = Math.Max(0, box.X + dx - pad);
            var timer = new PixelRect(left, top, Math.Min(strip.Width - left, box.Width + 2 * pad + reach), Math.Min(height, strip.Height - top));
            var reads = TextLeads.Select(lead => new BuffReader(textScale * lead).ReadTimer(strip, timer)).OfType<double>().Distinct().ToList();
            if (reads is [var seconds] && seconds <= Domain.Amsu.Duration(at.Kind)) return seconds;
        }
        return null;
    }

    const double TimerShift = 2;   // ref px the timer window moves when the box's own reads nothing

    // The boss row's "N초" runs a little larger than a buff's at the same size. Text scales that read (bitam_*, 1080p): 90% 0.90–0.95,
    // 100% 1.00–1.05, 110% 1.15–1.20 (1.05 misreads "22" as "2"), 120% 1.15–1.30; 80% none (the glyphs are too small: unread, never
    // wrong). ×1.05 reads 90–120%; ×1.00 read the 21:9 clip and no size wrong; ReadSeconds wants them to agree.
    static readonly double[] TextLeads = [1.05, 1.0];

    /// <summary>The 보스 디버프 크기 from which the "N초" is read (user, 2026-11-01: down to the smallest size whose replays pass). 100%
    /// passes (replay 5); at 90% the tens digit and the 암수/빛암 kind were misread in the game (replay 5), 80% has no readable text.
    /// Under it the icons alone count (Amsu.Timed).</summary>
    public const int ReadableFrom = 100;

    const double TimerHeight = 14, TimerPad = 4, TimerReach = 4;   // ref px under the icon box, and either side of it

    /// <summary>Icons and their seconds in one go (tests; the engine reads the seconds only when it needs them).</summary>
    public static AmsuRow Read(Image strip, double scale, int rowY, BossSlots slots, double textScale) =>
        new([.. FindBombs(strip, scale, rowY, slots).Select(b => new BombRead(b.Kind, ReadSeconds(strip, b, textScale), b.Slot))]);
}
