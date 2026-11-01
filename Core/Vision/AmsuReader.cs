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
    const int Icon = DebuffReader.BossIconSize, Pitch = DebuffReader.BossSlotPitch, Slack = 4;

    /// <summary>The bomb's icon as matched (the 암수 창 draws it); null while it has no template (빛암, amsu.plan.md).</summary>
    public static Image? IconOf(Bomb b) => Templates[(int)b];
    // Measured on amsu_15/25/45/55 (2560×1080, 보스 디버프 크기 100): 암수 scores 0.93 in each, under a damage number too (45); the
    // best other icon 0.54 (any hue) / 0.40 (a hue within MaxHueDistance), amsu_35 (no 암수) 0.38 at best. Midway: margins ±0.2.
    internal const double MinBomb = 0.72;
    // A slot placed on its frame (OnFrame) holds a debuff from this share: 0.45–0.88 on bitam_*/amsu_*; an empty slot whose window
    // catches the next icon's frame along its edge 0.27 at most, and the art inside green-framed buffs as much.
    const double MinSlotFrame = 0.35;
    const int MissesToEnd = 2;

    /// <summary>The bombs' icons on the line <paramref name="rowY"/> (strip px) of <paramref name="strip"/>, captured at
    /// <paramref name="scale"/> screen px per reference px; each the best window within an icon's width.</summary>
    public static IReadOnlyList<BombAt> FindBombs(Image strip, double scale, int rowY, BossSlots slots) =>
        FindOn(DebuffReader.ToReference(strip, scale), scale, rowY, slots).Bombs;

    /// <summary>The bombs found on a line, whether every slot the size shows was taken, and whether the row was there at all.</summary>
    public sealed record RowFind(int? Y, IReadOnlyList<BombAt> Bombs, bool Full, bool Seen);

    /// <summary>The bombs and the line they were found on (strip px): <paramref name="lockedY"/> first (RowLock's), then, when
    /// <paramref name="search"/>, DebuffReader.FindRowYs' candidates; the first line with a bomb wins. None found: the best
    /// candidate (or the locked line) and no bombs.</summary>
    /// <remarks><c>Full</c>: every slot the size shows holds a debuff, so a bomb missing from the row may be behind "+N" (else
    /// it is gone). <c>Seen</c>: a red or green frame on the line, so the row is on screen. Without a bomb, both are the locked
    /// line's (no locked line: not seen).</remarks>
    public static RowFind Locate(Image strip, double scale, BossSlots slots, int? lockedY, bool search = true)
    {
        var r = DebuffReader.ToReference(strip, scale);
        (bool Full, bool Seen) locked = (false, false);
        if (lockedY is { } l)
        {
            var there = FindOn(r, scale, l, slots);
            if (there.Bombs.Count > 0) return new(l, there.Bombs, there.Full, true);   // the usual case: one line matched
            locked = (there.Full, there.Seen);
        }
        if (!search) return new(lockedY, [], locked.Full, locked.Seen);
        var lines = DebuffReader.RowYs(r, scale);
        foreach (int y in lines.Where(c => lockedY is not { } l2 || Math.Abs(c - l2) > 2))
            if (FindOn(r, scale, y, slots) is { Bombs.Count: > 0 } found) return new(y, found.Bombs, found.Full, true);
        return new(lines.Count > 0 ? lines[0] : null, [], locked.Full, locked.Seen);
    }

    /// <summary>Bombs on line <paramref name="rowY"/> (strip px) of <paramref name="r"/> (the strip at reference size), walked slot by
    /// slot as your own debuff row is read: from the zone's first slot (screen-fixed, <paramref name="slots"/>), one
    /// <see cref="Pitch"/> on at a time while a debuff's red frame is there, up to the size's most (past it the game shows "+N").
    /// One match per debuff: 7 debuffs cost 7 matches and one empty slot's frame check.</summary>
    static (List<BombAt> Bombs, bool Full, bool Seen) FindOn(Image r, double scale, int rowY, BossSlots slots)
    {
        int y = (int)Math.Round(rowY / scale), last = r.Width - Icon, filled = 0;
        var found = new List<BombAt>();
        if (y < 0 || y + Icon > r.Height || last < 0) return (found, false, false);
        double Red(int x) => DebuffReader.FrameColours(r, new PixelRect(x, y, Icon, Icon)).Red;
        // Each slot onto its frame: the red share peaks where the window's band lies on the 1–2 px frame lines (0.54 there, 0.25
        // 2 px off, bitam_100), even on a red scene (amsu_15: 0.88 against 0.48 around it). The first slot's place is known to
        // ±4 px (x 927–929 at 1080p, 3 px right of it on the 21:9 clip).
        int? OnFrame(int at) => at - Slack > last || at + Slack < 0 ? null
            : Enumerable.Range(Math.Max(0, at - Slack), Math.Min(at + Slack, last) - Math.Max(0, at - Slack) + 1).MaxBy(Red);
        // The first slot: the first debuff frame from the left within a slot of where the layout puts it (테스트 모드's fit put it
        // 20 px off, replay 7), then onto that frame. Buff art never reaches MinSlotFrame; on a red scene the frame stays the peak.
        int guess = (int)Math.Round(slots.FirstX / scale);
        int start = Enumerable.Range(Math.Max(0, guess - Pitch), Math.Max(0, Math.Min(guess + Pitch, last) - Math.Max(0, guess - Pitch) + 1))
            .FirstOrDefault(x => Red(x) >= MinSlotFrame, -1);
        if (start < 0) return (found, false, DebuffReader.BuffZone(r, y).Count > 0);
        // One empty slot is passed (a damage number or a popup over one frame, review 2026-11-01); two in a row end the walk.
        int expected = Enumerable.Range(start, Math.Min(Pitch / 2, last - start) + 1).MaxBy(Red), empty = 0;
        for (int slot = 0; slot < slots.Most && empty < MissesToEnd && OnFrame(expected) is { } at; slot++)
        {
            if (Red(at) < MinSlotFrame)
            {
                (empty, expected) = (empty + 1, expected + Pitch);   // stay on the grid, not on what this window caught
                continue;
            }
            (empty, filled) = (0, filled + 1);
            if (Best(r, new PixelRect(at, y, Icon, Icon)) is { } hit)
                found.Add(new(hit.Kind, new PixelRect((int)Math.Round(at * scale), rowY, (int)Math.Round(Icon * scale), (int)Math.Round(Icon * scale)),
                    (int)Math.Round((at - guess) / (double)Pitch)));   // from the layout's first slot: steady when the walk begins early
            expected = at + Pitch;
        }
        // No debuff: the boss's own (green-framed) buffs on the line still show the row is there, and the bomb gone from it.
        return (found, filled >= slots.Most, filled > 0 || DebuffReader.BuffZone(r, y).Count > 0);
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
        var timer = new PixelRect(Math.Max(0, box.X - pad), top, Math.Min(strip.Width - Math.Max(0, box.X - pad), box.Width + 2 * pad),
            Math.Min(height, strip.Height - top));
        if (timer.Height <= 0) return null;
        // Both scales, a value only when they agree or one is unread, and never more than the bomb lasts: a scale just off reads
        // a digit short ("22" as "2" at 110% × 0.95), the same way every read, so the countdown's second read can't catch it.
        var reads = TextLeads.Select(lead => new BuffReader(textScale * lead).ReadTimer(strip, timer)).OfType<double>().Distinct().ToList();
        return reads is [var seconds] && seconds <= Domain.Amsu.Duration(at.Kind) ? seconds : null;
    }

    // The boss row's "N초" runs a little larger than a buff's at the same size. Text scales that read (bitam_*, 1080p): 90% 0.90–0.95,
    // 100% 1.00–1.05, 110% 1.15–1.20 (1.05 misreads "22" as "2"), 120% 1.15–1.30; 80% none (the glyphs are too small: unread, never
    // wrong). ×1.05 reads 90–120%; ×1.00 read the 21:9 clip and no size wrong; ReadSeconds wants them to agree.
    static readonly double[] TextLeads = [1.05, 1.0];

    /// <summary>The 보스 디버프 크기 from which the "N초" is read (user, 2026-11-01: down to the smallest size whose replays pass). 100%
    /// passes (replay 5); at 90% the tens digit and the 암수/빛암 kind were misread in the game (replay 5), 80% has no readable text.
    /// Under it the icons alone count (Amsu.Timed).</summary>
    public const int ReadableFrom = 100;

    const double TimerHeight = 14, TimerPad = 4;   // ref px under the icon box, and either side of it

    /// <summary>Icons and their seconds in one go (tests; the engine reads the seconds only when it needs them).</summary>
    public static AmsuRow Read(Image strip, double scale, int rowY, BossSlots slots, double textScale) =>
        new([.. FindBombs(strip, scale, rowY, slots).Select(b => new BombRead(b.Kind, ReadSeconds(strip, b, textScale), b.Slot))]);
}
