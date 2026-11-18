using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using static GaugeOverlay.Tests.Helpers;

namespace GaugeOverlay.Tests;

/// <summary>The boss row's identify-on-change gates (HANDOFF 3d): 낙인's per slot (<see cref="BrandWatch"/>, an
/// <see cref="IconGate{T}"/>), 암수's by look (<see cref="AmsuWatch"/>, an <see cref="IconCache{T}"/>). A slot whose icon stayed put
/// keeps its answer; the "N초" is read on every pass all the same.</summary>
public class BossRowGateTests
{
    static readonly Fit Hud110 = new(110);
    static readonly AmsuLayout Usual = AmsuLayout.Of(Fhd, Hud110, 100);
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    /// <summary>nakin_holy's row as the engine captures it: 5 debuffs, the 홀나/발키리 낙인 the 4th (BrandReaderTests).</summary>
    static Image Holy()
    {
        var s = Geometry.BossDebuffStrip(Fhd, Hud110, 100);
        return Fixture.Load("nakin_holy").Frames[0].Crop(new PixelRect(s.X - 700, s.Y - 100, Math.Min(s.Width, 600 - (s.X - 700)), s.Height));
    }

    /// <summary>Each filled slot's icon box in strip px.</summary>
    static IReadOnlyList<AmsuReader.SlotAt> Slots(Image strip)
    {
        var r = DebuffReader.ToReference(strip, Usual.Scale);
        int line = AmsuReader.SnapIn(r, Usual.Scale, Usual.Slots, Usual.Line, Geometry.BossLineSnap) ?? Usual.Line;
        return AmsuReader.WalkIn(r, Usual.Scale, Usual.Slots, line).Filled;
    }

    /// <summary><paramref name="strip"/> with slot <paramref name="to"/>'s icon replaced by slot <paramref name="from"/>'s, frames kept.</summary>
    static Image Swapped(Image strip, int from, int to)
    {
        var slots = Slots(strip);
        var (a, b) = (slots[from].Icon, slots[to].Icon);
        var copy = strip.Copy();
        const int Frame = 3;   // the red frame lines stay, so the walk finds the same slots
        for (int y = Frame; y < a.Height - Frame; y++)
            Array.Copy(strip.Bgra, ((a.Y + y) * strip.Width + a.X + Frame) * 4, copy.Bgra, ((b.Y + y) * strip.Width + b.X + Frame) * 4, (a.Width - 2 * Frame) * 4);
        return copy;
    }

    /// <summary><paramref name="strip"/> cut just before slot <paramref name="slot"/>: that slot and the ones after it gone.</summary>
    static Image CutBefore(Image strip, int slot) => strip.Crop(new PixelRect(0, 0, Slots(strip)[slot].Icon.X - 1, strip.Height));

    [Fact]
    public void A_brand_slot_unchanged_is_not_identified_again()
    {
        var w = new BrandWatch();
        var strip = Holy();
        w.Read(Usual, strip, S(0));
        int first = w.Identifies;

        w.Read(Usual, strip, S(0.5));

        Assert.Equal(4, first);   // slots 0–3: the search stops at the 낙인
        Assert.Equal(first, w.Identifies);
        Assert.Equal(BrandIcon.홀나발키리, w.Icon);
    }

    [Fact]
    public void A_brand_slot_whose_icon_changed_is_identified_again_alone()
    {
        var w = new BrandWatch();
        var strip = Holy();
        w.Read(Usual, strip, S(0));
        int first = w.Identifies;

        w.Read(Usual, Swapped(strip, from: 0, to: 3), S(0.5));   // the 낙인 slot now shows the first debuff

        Assert.Null(w.Icon);
        Assert.Equal(first + 2, w.Identifies);   // slot 3 changed, slot 4 asked for the first time; 0–2 kept
    }

    [Fact]
    public void A_filled_count_change_identifies_every_brand_slot_again()
    {
        var w = new BrandWatch();
        var strip = Holy();
        w.Read(Usual, strip, S(0));
        int first = w.Identifies;

        w.Read(Usual, CutBefore(strip, 4), S(0.5));   // 4 debuffs: the row shifted

        Assert.Equal(4, w.Filled);
        Assert.Equal(8, w.Identifies);   // 4 searches before, 4 again
        Assert.Equal(BrandIcon.홀나발키리, w.Icon);
    }

    [Fact]
    public void A_cleared_brand_slot_drops_its_result()
    {
        var w = new BrandWatch();
        var strip = Holy();
        w.Read(Usual, strip, S(0));

        w.Read(Usual, CutBefore(strip, 3), S(0.5));   // the 낙인's slot gone

        Assert.Equal(3, w.Filled);
        Assert.Null(w.Icon);
    }

    [Fact]
    public void Resetting_the_brand_gate_identifies_every_slot_again()
    {
        var w = new BrandWatch();
        var strip = Holy();
        w.Read(Usual, strip, S(0));
        int first = w.Identifies;

        w.ResetGate();
        w.Read(Usual, strip, S(0.5));

        Assert.Equal(4, first);
        Assert.Equal(8, w.Identifies);   // 4 searches before, 4 again
    }

    [Fact]
    public void A_slot_that_turns_into_a_brand_is_identified_again()
    {
        var w = new BrandWatch();
        var strip = Holy();
        w.Read(Usual, CutBefore(Swapped(strip, from: 0, to: 3), 4), S(0));   // 4 debuffs, none a 낙인
        int first = w.Identifies;

        w.Read(Usual, CutBefore(strip, 4), S(0.5));   // the 4th turns into the 낙인, the count unchanged

        Assert.Equal(4, first);
        Assert.Equal(BrandIcon.홀나발키리, w.Icon);
        Assert.Equal(first + 1, w.Identifies);   // only the 4th searched again; 0–2 kept
    }

    /// <summary>Every 낙인 icon and bomb differs from the others past the gate (one slot swapping straight from one to another
    /// is searched again), with room: the boss row's same-slot noise runs 3.1 median (BossGateSweep).</summary>
    [Fact]
    public void Brand_and_bomb_icons_differ_by_well_over_the_threshold()
    {
        var thumbs = Enum.GetValues<BrandIcon>().Select(b => BrandReader.IconOf(b)!)
            .Concat(new[] { Bomb.암수, Bomb.빛암 }.Select(b => AmsuReader.IconOf(b)!)).Select(BuffReader.Thumb).ToArray();

        double least = thumbs.SelectMany((a, i) => thumbs.Skip(i + 1).Select(b => IconGate.Change(a, b))).Min();

        Assert.True(least > 2 * IconGate.Threshold, $"least change {least:0.0}");
    }

    [Fact]
    public void Through_the_gate_the_brand_reads_as_without_it_on_every_shot()
    {
        foreach (var (name, size) in new[] { ("nakin_dohwa", 100), ("nakin_holy", 100), ("bitam_100", 100), ("bitam_110", 110) })
        {
            var s = Geometry.BossDebuffStrip(Fhd, Hud110, size);
            var strip = Fixture.Load(name).Frames[0].Crop(new PixelRect(s.X - 700, s.Y - 100, Math.Min(s.Width, 600 - (s.X - 700)), s.Height));
            var layout = AmsuLayout.Of(Fhd, Hud110, size);
            var r = DebuffReader.ToReference(strip, layout.Scale);
            int line = AmsuReader.SnapIn(r, layout.Scale, layout.Slots, layout.Line, Geometry.BossLineSnap) ?? layout.Line;
            var w = new BrandWatch();
            w.Read(layout, strip, S(0));
            w.Read(layout, strip, S(0.5));   // the second through the kept answers
            Assert.Equal(BrandReader.Find(r, layout.Scale, layout.Slots, line), w.Icon);
        }
    }

    // bosstext_8090 at 90% (1080p, HUD 110), 57–64 s: one 빛암 in the first slot counting 8 → 1, the icon unchanged.
    [Fact]
    public void A_kept_bomb_slot_still_reads_its_seconds_on_every_pass()
    {
        const int Size = 90, CropX = 900, CropY = 160;
        var f = Fixture.Load("bosstext_8090");
        var s = Geometry.BossDebuffStrip(Fhd, Hud110, Size);
        int left = Math.Max(s.X, CropX), top = Math.Max(s.Y, CropY);
        var cut = new PixelRect(left - CropX, top - CropY, Math.Min(s.Right, CropX + f.Rect[2]) - left, Math.Min(s.Bottom, CropY + f.Rect[3]) - top);
        var placed = AmsuLayout.Of(Fhd, Hud110, Size);
        var layout = placed with { Slots = placed.Slots with { FirstX = placed.Slots.FirstX - (left - s.X) }, Line = placed.Line - (top - s.Y) };
        var w = new AmsuWatch();
        int? matches = null;
        foreach (int second in Enumerable.Range(57, 8))
        {
            var t = S(second);
            Assert.Equal((RowPass.Frames, true), w.Due(t, layout.Timed));   // identification due each second
            w.Read(layout, RowPass.Frames, 1_000_000, f.Frames[second].Crop(cut), t);
            Assert.Equal(double.Parse(BossTimerTests.Labels[second]), Assert.Single(w.LastRow!.Bombs).Seconds);
            matches ??= w.Matches;
            Assert.Equal(matches, w.Matches);   // the bomb kept by its look: never matched again
        }
    }

    [Fact]
    public void A_brand_alone_is_read_by_the_amsu_watch_without_the_boss_text()
    {
        var brand = new BrandWatch();
        var w = new AmsuWatch { Bombs = false, Brand = brand };

        var (pass, bossText) = w.Due(S(0), timed: true);
        w.Read(Usual, pass, boss: null, Holy(), S(0));

        Assert.Equal(RowPass.Frames, pass);
        Assert.False(bossText);
        Assert.Equal(BrandIcon.홀나발키리, brand.Icon);
        Assert.True(brand.State.Active);
        Assert.Empty(w.State.Up);   // no bomb matched
    }

    [Fact]
    public void A_brand_and_bombs_share_one_walk_and_each_reads_its_own()
    {
        var brand = new BrandWatch();
        var w = new AmsuWatch { Brand = brand };

        var (pass, _) = w.Due(S(0), timed: true);
        w.Read(Usual, pass, boss: 1_000_000, Holy(), S(0));

        Assert.True(brand.State.Active);
        Assert.Equal(5, w.Filled);
        Assert.Equal(5, brand.Filled);
    }

        [Fact]
    public void A_brand_with_time_to_run_waits_for_the_identification_cadence()
    {
        var brand = new BrandWatch();
        var w = new AmsuWatch { Bombs = false, Brand = brand };
        w.Read(Usual, RowPass.Frames, null, Holy(), S(0));

        var (soon, _) = w.Due(S(0.3), timed: true);
        var (later, _) = w.Due(S(0.6), timed: true);

        Assert.Equal(RowPass.None, soon);   // the countdown is not under 2 s: no reason for 5 passes a second
        Assert.Equal(RowPass.Frames, later);
    }

    [Theory]
    [InlineData(true)]    // 암수 and 낙인
    [InlineData(false)]   // 낙인 alone
    public void A_pass_brings_the_strip_to_reference_size_once_whatever_reads_it(bool bombs)
    {
        var w = new AmsuWatch { Bombs = bombs, Brand = new BrandWatch() };
        var strip = Holy();
        double t = 0;
        for (int pass = 1; pass <= 4; pass++, t += 0.5)
        {
            var (due, bossText) = w.Due(S(t), timed: true);
            Assert.Equal(RowPass.Frames, due);
            w.Read(Usual, due, bossText ? 1_000_000 : null, strip, S(t));
            Assert.Equal(pass, w.Resizes);
        }
    }
}
