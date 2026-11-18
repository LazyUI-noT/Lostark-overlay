using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The two bombs a party throws at the boss (amsu.design.md): 20 s and 25 s.</summary>
public enum Bomb { 암수, 빛암 }

/// <summary>The boss debuff zone in a strip: its first slot's left edge (strip px, Geometry.FirstBossDebuffX) and the most slots
/// it shows (Geometry.MaxIcons); the gap between two frames (reference px); <paramref name="Centred"/>: the row is centred
/// on <paramref name="FirstX"/> (대난투, Geometry.DaenantuSlotsIn), so the walk starts at the first debuff left of it.</summary>
public sealed record BossSlots(int FirstX, int Most, int Gap = 2, bool Centred = false, int? Identify = null)
{
    /// <summary>The slots whose icons are identified and timed (감지 개수 within what the row shows); the walk still goes to <see cref="Most"/>.</summary>
    public int Reads => Identify ?? Most;
}

/// <summary>A bomb's icon found in the boss row strip (strip px).</summary>
public sealed record BombAt(Bomb Kind, PixelRect Icon, int? Slot = null);

/// <summary>A bomb and its seconds left ("N초" under it; null when unread).</summary>
public sealed record BombRead(Bomb Kind, double? Seconds, int? Slot = null);

/// <summary>What one read of the boss row gave: the bombs in it; <paramref name="Full"/>: every slot the size shows is taken (a
/// bomb missing may be behind "+N"); <paramref name="Seen"/>: the row was on screen (any red or green frame on its line), not
/// hidden by a menu or a cutscene.</summary>
public sealed record AmsuRow(IReadOnlyList<BombRead> Bombs, bool Full = false, bool Seen = true);

/// <summary>암흑수류탄 / 빛나는 암흑수류탄 in the boss debuff row (Geometry.BossDebuffStrip, on the line the HUD and resolution place).
/// Icons first (<see cref="Locate"/>); the "N초" under a found bomb only (<see cref="ReadSeconds"/>).
/// Icons are matched as the boss's sigils are (BuffReader.Match: structure and hue, frame left out) along the line. Pure.</summary>
public static class AmsuReader
{
    /// <summary>A set of boss-row icons matched together (structure and hue, frame left out): its templates (null: none yet),
    /// and the score a match needs.</summary>
    internal sealed class IconSet
    {
        public readonly Image?[] Templates;
        public readonly int[] Kinds;   // the templates there are
        public readonly float[][] Thumbs;
        public readonly BuffReader.Centred[] Centred;
        public readonly double[] Hues;
        public readonly double Min;

        public IconSet(IEnumerable<string?> icons, double min)
        {
            Templates = [.. icons.Select(s => s is null ? null : Image.FromBytes(Convert.FromBase64String(s)))];
            Kinds = [.. Enumerable.Range(0, Templates.Length).Where(k => Templates[k] is not null)];
            Thumbs = [.. Kinds.Select(k => BuffReader.Thumb(Templates[k]!))];
            Centred = [.. Thumbs.Select(BuffReader.Centred.Of)];
            Hues = [.. Kinds.Select(k => SlotClassifier.ColourStats(Templates[k]!).MeanHue)];
            Min = min;
        }
    }

    static readonly IconSet Bombs = new(AmsuTemplates.Icons, MinBomb);
    static Image?[] Templates => Bombs.Templates;
    const int Icon = DebuffReader.BossIconSize, Pitch = DebuffReader.BossSlotPitch, Slack = 8;   // ±8: at 120% a slot's frame peaks 28 px after the last, not 34 (amsu_live_120)

    /// <summary>The bomb's icon as matched (the 암수 창 draws it); null while it has no template (빛암, amsu.plan.md).</summary>
    public static Image? IconOf(Bomb b) => Templates[(int)b];
    // Measured on amsu_15/25/45/55 (2560×1080, 타겟 UI 버프 크기 100): 암수 scores 0.93 in each, under a damage number too (45); the
    // best other icon 0.54 (any hue) / 0.40 (a hue within MaxHueDistance), amsu_35 (no 암수) 0.38 at best. Midway: margins ±0.2.
    internal const double MinBomb = 0.72;
    // A slot placed on its frame (OnFrame) holds a debuff from this share: 0.45–0.88 on bitam_*/amsu_*; an empty slot whose window
    // catches the next icon's frame along its edge 0.27 at most, and the art inside green-framed buffs as much.
    const double MinSlotFrame = 0.35;

    // An icon's frame line: a column of its height nearly all red. Measured on 빛암 80_90 / 120_110_100 (1080p, HUD 100): the
    // right line 19, 22, 25, 28, 31 screen px after the left at 80–120%, 30.1–31.0 reference px at every size, and the next
    // icon's left line ending 35–36 ref px after it; so the right line is looked for up to 33. The next one starts 1–6 px after.
    const double LineShare = 0.75;
    const int MinWidth = 23, MaxWidth = 33, GapReach = 4;   // the next left line up to 4 px past the layout's gap (2: 1–6 px)
    // A frame's top and bottom border run inward from its side lines: the column after a left line is red in those rows, the
    // gap after a right line is not (scene behind it).
    const double BorderShare = 0.15;

    /// <summary>The bombs found on a line, whether every slot the size shows was taken, whether the row was there at all, and how
    /// many slots hold a debuff.</summary>
    /// <remarks><c>Full</c>: every slot the size shows holds a debuff, so a bomb missing from the row may be behind "+N" (else
    /// it is gone). <c>Seen</c>: a red or green frame on the line, so the row is on screen.</remarks>
    public sealed record RowFind(IReadOnlyList<BombAt> Bombs, bool Full, bool Seen, int Filled);

    /// <summary>The bombs on line <paramref name="rowY"/> (strip px) of <paramref name="strip"/>, captured at <paramref name="scale"/>
    /// screen px per reference px: the walk, then one match per filled slot.</summary>
    public static RowFind Locate(Image strip, double scale, BossSlots slots, int rowY) =>
        LocateIn(DebuffReader.ToReference(strip, scale), scale, slots, rowY);

    /// <summary><see cref="Locate"/> on the strip already at reference size (<see cref="DebuffReader.ToReference"/>): a pass that
    /// walks and then identifies resizes the strip once.</summary>
    internal static RowFind LocateIn(Image r, double scale, BossSlots slots, int rowY, IconCache<(Bomb Kind, int Dx)?>? gate = null)
    {
        var (bombs, full, seen, filled) = FindOn(r, scale, rowY, slots, gate);
        return new(bombs, full, seen, filled);
    }

    /// <summary>The icons <see cref="LocateIn"/> matched lately (icon-gates step 2): a filled slot showing one of them is not
    /// matched again, wherever the row moved it. Twice the most slots: the row and the icons it held before a shift. One per
    /// <see cref="AmsuWatch"/>, new with each layout.</summary>
    public static IconCache<(Bomb Kind, int Dx)?> Gate() =>
        new(InBox, 2 * Geometry.MaxIconsAny);

    /// <summary><see cref="Around"/> at <paramref name="box"/>, the match's x relative to the box (as the cache keeps it).</summary>
    static (Bomb Kind, int Dx)? InBox(Image r, PixelRect box) =>
        Around(r, box.X, box.Y) is { } a ? (a.Kind, a.X - box.X) : null;   // no score: a cached one would be stale

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
    static (List<BombAt> Bombs, bool Full, bool Seen, int Filled) FindOn(Image r, double scale, int rowY, BossSlots slots,
        IconCache<(Bomb Kind, int Dx)?>? gate)
    {
        var walk = Walk(r, scale, rowY, slots);
        int y = (int)Math.Round(rowY / scale);
        var found = new List<BombAt>();
        foreach (var f in walk.Filled.Take(slots.Reads))
        {
            int x = (int)Math.Round(f.Icon.X / scale);
            var box = new PixelRect(x, y, Icon, Icon);
            var match = gate is null ? InBox(r, box) : gate.Classify(r, box);
            if (match is { } hit)   // the box where the icon matched: its "N초" is under it
                found.Add(new(hit.Kind, f.Icon with { X = (int)Math.Round(Math.Clamp(x + hit.Dx, 0, Math.Max(0, r.Width - Icon)) * scale) }, f.Slot));   // a Dx kept from another box stays on the strip
        }
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
        // Each column's checks are asked for many times over (the frame-line checks go back and forth): each is worked out once.
        using var map = new DebuffReader.FrameMap(r);   // every window's frame colours from one pass over the strip (icon-gates 1a)
        var reds = new double?[r.Width];
        var lines = new bool?[r.Width + 1];
        var borders = new bool?[r.Width + 1];
        double Red(int x) => reds[x] ??= map.Colours(new PixelRect(x, y, Icon, Icon)).Red;
        // Each slot onto its frame: the red share peaks where the window's band lies on the 1–2 px frame lines (0.54 there, 0.25
        // 2 px off, bitam_100), even on a red scene (amsu_15: 0.88 against 0.48 around it). The first slot's place is known to
        // ±4 px (x 927–929 at 1080p, 3 px right of it on the 21:9 clip).
        int? OnFrame(int at) => at - Slack > last || at + Slack < 0 ? null
            : Enumerable.Range(Math.Max(0, at - Slack), Math.Min(at + Slack, last) - Math.Max(0, at - Slack) + 1).MaxBy(Red);
        // Each icon is put on its own frame lines (left and right, the column nearly all red), and the next one looked for just
        // after its right line, so the walk follows the icons' real spacing (31 px at 120%, not Pitch: amsu_live_120_slot3);
        // where no lines are found (a blurred small size) the window's peak and share decide as before.
        bool Line(int x) => x >= 0 && x < r.Width && (lines[x] ??= map.Colours(new PixelRect(x, y + 2, 1, Icon - 4)).Red >= LineShare);
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
                    if (map.Colours(new PixelRect(x, row, 1, 1)).Red > 0) red++;
                }
            return all > 0 && red / (double)all >= BorderShare;
        }
        // The first slot: the first debuff from the left within a slot of where the layout puts it (테스트 모드's fit put it 20 px
        // off, replay 7): its frame lines (a lone 빛암 there scores 0.34 by the window: AmsuSimulationTests), else the first window
        // over MinSlotFrame, put onto its peak. Buff art never reaches MinSlotFrame; on a red scene the frame stays the peak.
        // Centred (대난투): the first debuff left of the centre, no further than a full row reaches, by its frame lines only (the
        // gimmick's scene is red: no red-share fallback), and the slots counted from it.
        int centre = (int)Math.Round(slots.FirstX / scale);
        var firstIcon = slots.Centred
            ? IconIn(centre - slots.Most * (Icon + slots.Gap) / 2 - Slack, centre)
            : IconIn(centre - Pitch, centre + Pitch);
        int guess = slots.Centred ? firstIcon?.Left ?? centre : centre;
        int start = firstIcon?.Left ?? (slots.Centred ? -1 : Enumerable.Range(Math.Max(0, guess - Pitch), Math.Max(0, Math.Min(guess + Pitch, last) - Math.Max(0, guess - Pitch) + 1))
            .FirstOrDefault(x => Red(x) >= MinSlotFrame, -1));
        if (start < 0) return new(filled, false, DebuffReader.BuffZone(map, y).Count > 0);
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
            filled.Add(new((int)Math.Round((x - guess) / (double)pitch),   // from the layout's first slot (steady when the walk begins early); centred, from the first debuff
                new PixelRect((int)Math.Round(x * scale), rowY, (int)Math.Round(Icon * scale), (int)Math.Round(Icon * scale))));
            if (right is { } edge)   // the last line in reach: red icon art makes lines inside
            {
                pitch = edge - x + 1 + slots.Gap;   // the icon's own width and the layout's gap
                lined = Edge(edge + 1, edge + slots.Gap + GapReach);
            }
            else lined = null;
            expected = x + pitch;
        }
        // No debuff: the boss's own (green-framed) buffs on the line still show the row is there, and the bomb gone from it.
        return new(filled, filled.Count >= slots.Most, filled.Count > 0 || DebuffReader.BuffZone(map, y).Count > 0);
    }

    static int[] Kinds => Bombs.Kinds;

    /// <summary>Each template's structure score and hue distance on <paramref name="icon"/> (reference px), in <see cref="Kinds"/> order.</summary>
    internal static (double[] Structure, double[] Hue) Scores(Image r, PixelRect icon)
    {
        var (s, h, _) = BuffReader.Match(r, icon, Bombs.Thumbs, Bombs.Hues, []);
        return (s, h);
    }

    /// <summary><see cref="Best"/> at the slot's left frame line and up to <see cref="MatchSlack"/> px either side: the templates
    /// sit a px or two inside the frame line, more or less with the size (100%: 1–2 px right of it). The tries overlap (each
    /// matches ±s px around itself), so each box is taken once and its scores reused: 45 boxes instead of 125 at 100%, the
    /// same answer (AmsuMatchTests). Every box's thumbnail comes from one table of sums over the slot, and a box's hue only
    /// when a kind's best moves to it (icon-gates.plan.md, 1a): the same numbers as a crop per box.</summary>
    internal static (Bomb Kind, double Score, int X)? Around(Image r, int x, int y) =>
        Around(Bombs, r, x, y) is { } a ? ((Bomb)a.Kind, a.Score, a.X) : null;

    /// <summary><see cref="Around(Image, int, int)"/> for any <paramref name="set"/>: its kind (template index) over its
    /// <see cref="IconSet.Min"/>.</summary>
    internal static (int Kind, double Score, int X)? Around(IconSet set, Image r, int x, int y)
    {
        var Kinds = set.Kinds;
        var inner = BuffReader.Interior(new PixelRect(x, y, Icon, Icon));
        int s = Math.Max(1, inner.Width / 12), span = MatchSlack + s, w = 2 * span + 1;
        int x0 = Math.Max(0, inner.X - span), y0 = Math.Max(0, inner.Y - s);
        var region = new PixelRect(x0, y0, Math.Min(r.Width, inner.Right + span) - x0, Math.Min(r.Height, inner.Bottom + s) - y0);
        if (region.Width < inner.Width || region.Height < inner.Height) return null;   // no box fits on the image
        using var sums = new BuffReader.BgrSums(r, region);
        var boxes = new double[]?[w * (2 * s + 1)];
        var hues = new double?[boxes.Length];
        var thumb = new float[BuffReader.ThumbLength];   // one per slot: a local function can't hold a stack span
        var centred = new double[BuffReader.ThumbLength];
        int Slot(int dx, int dy) => (dy + s) * w + dx + span;
        PixelRect RectAt(int dx, int dy) => inner with { X = inner.X + dx, Y = inner.Y + dy };
        double[]? Box(int dx, int dy)   // each kind's score in the box dx from inner.X (−span…span), dy from inner.Y; null off the image
        {
            var rect = RectAt(dx, dy);
            if (rect.X < 0 || rect.Y < 0 || rect.Right > r.Width || rect.Bottom > r.Height) return null;
            ref var box = ref boxes[Slot(dx, dy)];
            if (box is null)
            {
                sums.Thumb(rect, thumb);
                double squares = BuffReader.Centre(thumb, centred);
                box = new double[set.Thumbs.Length];
                for (int k = 0; k < box.Length; k++) box[k] = BuffReader.ChannelNcc(centred, squares, set.Centred[k]);
            }
            return box;
        }
        double HueAt(int dx, int dy) => hues[Slot(dx, dy)] ??= SlotClassifier.ColourStats(r, RectAt(dx, dy)).MeanHue;
        (int Kind, double Score, int X)? hit = null;
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
                        if (b[k] > best[k]) (best[k], hue[k]) = (b[k], BuffReader.HueDistance(HueAt(at + dx, dy), set.Hues[k]));
                }
            int pick = -1;
            for (int k = 0; k < Kinds.Length; k++)
                if (hue[k] <= BuffReader.MaxHueDistance && best[k] >= set.Min && (pick < 0 || best[k] > best[pick])) pick = k;
            if (pick >= 0 && (hit is null || best[pick] > hit.Value.Score)) hit = (Kinds[pick], best[pick], x + at);
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

    /// <summary>The seconds under <paramref name="at"/>; <paramref name="textScale"/> = Geometry.BuffScale at the 타겟 UI 버프 크기.</summary>
    public static double? ReadSeconds(Image strip, BombAt at, double textScale) =>
        ReadSeconds(strip, at.Icon, textScale, Domain.Amsu.Duration(at.Kind));

    /// <summary>The seconds under the icon <paramref name="box"/>, never more than <paramref name="most"/> (what the debuff lasts, and a
    /// little over: a misread longer than that is dropped).</summary>
    public static double? ReadSeconds(Image strip, PixelRect box, double textScale, double most)
    {
        if (Math.Min((int)Math.Round(TimerHeight * textScale), strip.Height - box.Bottom) <= 0) return null;
        var readers = Readers(textScale);
        // Both scales, a value only when they agree or one is unread, and never more than the bomb lasts: a scale just off reads
        // a digit short ("22" as "2" at 110% × 0.95), the same way every read, so the countdown's second read can't catch it. The
        // window at the box first, then a step either side (the text cut by a box a px or two off its icon: amsu_live_120_slot3).
        int step = Math.Max(1, (int)Math.Round(TimerShift * textScale));
        foreach (int dx in new[] { 0, step, -step })
        {
            var timer = TimerWindow(strip, box, textScale, dx);
            var reads = readers.Select(r => r.ReadTimer(strip, timer)).OfType<double>().Distinct().ToList();
            if (reads is [var seconds] && seconds <= most) return seconds;
        }
        return null;
    }

    /// <summary>The "N초" window under <paramref name="box"/>, moved <paramref name="dx"/>, inside <paramref name="strip"/>. Wider on
    /// the right (TimerReach): a box a px or two left of its icon cut the "초" off and read nothing; the next icon's text starts well
    /// past it.</summary>
    internal static PixelRect TimerWindow(Image strip, PixelRect box, double textScale, int dx = 0)
    {
        int top = box.Bottom, height = (int)Math.Round(TimerHeight * textScale), pad = (int)Math.Round(TimerPad * textScale);
        int reach = (int)Math.Round(TimerReach * textScale), left = Math.Max(0, box.X + dx - pad);
        return new PixelRect(left, top, Math.Min(strip.Width - left, box.Width + 2 * pad + reach), Math.Min(height, strip.Height - top));
    }

    /// <summary>Two leads, so a scale just off can't read a digit short unchecked: under <see cref="BossGlyphsBelow"/> the boss
    /// text's own glyphs (80%, 90% at 1080) at <see cref="BossLeads"/>, from it the buff glyphs at <see cref="TextLeads"/>, which
    /// read 100–120% and score the smaller boss text too low.</summary>
    static BuffReader[] Readers(double textScale) => textScale < BossGlyphsBelow
        ? [.. BossLeads.Select(lead => BuffReader.ForBossText(textScale * lead))]
        : [.. TextLeads.Select(lead => new BuffReader(textScale * lead))];

    // BossTimerTests, 빛암 80_90: ×0.95 alone drops tens ("22" as "2"), so with ×1.00 they disagree and nothing reads; ×1.05 alone
    // reads "14" in an empty slot, which ×1.00's null would let through. 1.00 + 0.95 reads every labelled timer.
    static readonly double[] BossLeads = [1.0, 0.95];

    // The old timed floor (100%): every scale the buff glyphs never read (replay 5 misread 90%) goes to the boss glyphs.
    const double BossGlyphsBelow = 0.995;

    const double TimerShift = 2;   // ref px the timer window moves when the box's own reads nothing

    // The boss row's "N초" runs a little larger than a buff's at the same size. Buff-glyph text scales that read (bitam_*, 1080p):
    // 90% 0.90–0.95, 100% 1.00–1.05, 110% 1.15–1.20 (1.05 misreads "22" as "2"), 120% 1.15–1.30; 80% none. ×1.05 reads 90–120%;
    // ×1.00 read the 21:9 clip and no size wrong; ReadSeconds wants them to agree. Under BossGlyphsBelow the boss glyphs read instead.
    static readonly double[] TextLeads = [1.05, 1.0];

    /// <summary>The 타겟 UI 버프 크기 (on screen, at 1080) from which the "N초" is read: 80%, with the boss text's own glyphs under
    /// <see cref="BossGlyphsBelow"/> (user, 2026-11-18; BossTimerTests: every labelled 80% and 90% timer of 빛암 80_90 reads, one
    /// over glare unread). The buff glyphs alone needed 100% (replay 5 misread 90%). Under it the icons alone count (Amsu.Timed);
    /// in practice the icons stop first (Amsu.Readable).</summary>
    public const int ReadableFrom = 80;

    const double TimerHeight = 14, TimerPad = 4, TimerReach = 4;   // ref px under the icon box, and either side of it

    /// <summary>Icons and their seconds in one go (tests; the engine reads the seconds only when it needs them).</summary>
    public static AmsuRow Read(Image strip, double scale, int rowY, BossSlots slots, double textScale) =>
        new([.. Locate(strip, scale, slots, rowY).Bombs.Select(b => new BombRead(b.Kind, ReadSeconds(strip, b, textScale), b.Slot))]);
}
