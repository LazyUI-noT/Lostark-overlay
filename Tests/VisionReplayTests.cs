using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit.Abstractions;

namespace GaugeOverlay.Tests;

/// <summary>The frames measured from gameplay.mp4 and 333.mp4 (PLAN §3), and the reference screenshots,
/// through the real reader. Frame numbers are relative to each fixture's first frame.</summary>
[Collection(nameof(VisionReplayTests))]   // alone: Slot_read_is_fast times itself and failed beside other tests
public class VisionReplayTests(ITestOutputHelper log)
{
    private static Reading[] Replay(string name, string calName, int calFrame)
    {
        var f = Fixture.Load(name);
        var slot = Enum.Parse<SlotId>(f.Slot);
        var cal = new SlotCalibration(Fixture.Load(calName).Frames[calFrame]);
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(f.Hud)));
        return f.Frames.Select((img, i) => SlotClassifier.Read(slot, img, f.Pts[i], cal, digits)).ToArray();
    }

    private static void AssertRange(Reading[] r, int from, int to, ReadKind kind, int value = 0)
    {
        for (int i = from; i <= to; i++)
            Assert.True(r[i].Kind == kind && r[i].Value == value, $"frame {i}: {r[i].Kind} {r[i].Value}, expected {kind} {value}");
    }

    private static void AssertCooldown(Reading r, int value, int frame) =>
        Assert.True(r.Kind == ReadKind.CooldownIllegible || (r.Kind == ReadKind.Seconds && r.Value == value),
            $"frame {frame}: {r.Kind} {r.Value}, expected {value}s or CooldownIllegible");

    [Fact]
    public void D_end_reads_2_then_1_at_frame_39_then_ready_at_99()
    {
        var r = Replay("gameplay_D_end", "gameplay_D_end", 110);
        AssertRange(r, 0, 38, ReadKind.Seconds, 2);
        AssertRange(r, 39, 98, ReadKind.Seconds, 1);
        AssertRange(r, 99, r.Length - 1, ReadKind.Ready);
    }

    [Fact]
    public void S_end_reads_2_then_1_at_frame_7_then_ready_at_67()
    {
        var r = Replay("gameplay_S_end", "gameplay_S_end", 75);
        AssertRange(r, 0, 6, ReadKind.Seconds, 2);
        AssertRange(r, 7, 66, ReadKind.Seconds, 1);
        AssertRange(r, 67, r.Length - 1, ReadKind.Ready);
    }

    [Fact]
    public void D_start_reads_7_through_the_press_highlight_and_red_border_then_6_at_frame_15()
    {
        var r = Replay("gameplay_D_start", "gameplay_D_end", 110);
        for (int i = 0; i <= 14; i++) AssertCooldown(r[i], 7, i);
        AssertRange(r, 15, r.Length - 1, ReadKind.Seconds, 6);
    }

    [Fact]
    public void E_is_ready_until_the_cast_flash_at_20_then_reads_25()
    {
        var r = Replay("333_E", "333_E", 0);
        AssertRange(r, 0, 19, ReadKind.Ready);
        // The flash washes the icon out but keeps its key label, so frame 20 may still read Ready (as it always has
        // without a calibration); on a slot that was ready that changes nothing, and frame 21 records the cast.
        Assert.True(r[20].Kind is ReadKind.CooldownIllegible or ReadKind.Unreadable or ReadKind.Ready
            || (r[20].Kind == ReadKind.Seconds && r[20].Value == 25), $"frame 20: {r[20].Kind} {r[20].Value}");
        AssertRange(r, 21, 38, ReadKind.Seconds, 25);
        AssertRange(r, 39, r.Length - 1, ReadKind.Seconds, 24);
    }

    [Fact]
    public void C_reads_17_then_is_reset_on_Es_cast_frame_21()
    {
        var r = Replay("333_C", "333_C", 30);
        AssertRange(r, 0, 20, ReadKind.Seconds, 17);
        AssertRange(r, 21, r.Length - 1, ReadKind.Ready);
    }

    [Fact]
    public void S_is_unreadable_while_the_cutscene_hides_the_HUD()
    {
        // HUD gone from 16.884 s (frame 53) to 19.784 s (frame 226), found by looking at the fixture.
        var r = Replay("gameplay_S_cutscene", "gameplay_S_cutscene", 0);
        AssertRange(r, 0, 52, ReadKind.Ready);
        AssertRange(r, 53, 226, ReadKind.Unreadable);
        AssertRange(r, 227, r.Length - 1, ReadKind.Ready);
    }

    private static readonly Dictionary<SlotId, (ReadKind Kind, int Value)> CdShot = new()
    {
        [SlotId.각성기] = (ReadKind.Minutes, 3), [SlotId.초각스] = (ReadKind.Seconds, 41),
        [SlotId.일반2] = (ReadKind.Seconds, 14), [SlotId.일반3] = (ReadKind.Seconds, 7), [SlotId.아덴2] = (ReadKind.Seconds, 12),
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cd_screenshot_reads_every_slot(bool calibrated)
    {
        var digits = new DigitReader(1);
        foreach (var slot in SlotInfo.All)
        {
            var cal = calibrated ? new SlotCalibration(Fixture.Load($"Nocd_{slot}").Frames[0]) : null;
            var r = SlotClassifier.Read(slot, Fixture.Load($"cd_{slot}").Frames[0], TimeSpan.Zero, cal, digits);
            var want = CdShot.GetValueOrDefault(slot, (ReadKind.Ready, 0));
            Assert.True((r.Kind, r.Value) == want, $"{slot}: {r.Kind} {r.Value}, expected {want}");
        }
    }

    // A stack skill's first cast, every recorded frame: "1s" without its count for ~150 ms, then with it, then the recharge
    // timer. Known to stack, the slot shows no sweep in the lockout and one that never goes back after it (the sweep had
    // jumped 84 times in the first 1.2 s of these casts, user 2026-10-13).
    [Theory]
    [InlineData("lt2_lockout_일반3")]
    [InlineData("lt_lockout_일반1")]
    public void A_known_stack_skills_first_cast_sweeps_without_jumps(string name)
    {
        var f = Fixture.Load(name);
        var slot = Enum.Parse<SlotId>(f.Slot);
        var digits = new DigitReader(1);
        var s = new SlotState(slot, SlotPhase.Ready, null, null, false, f.Pts[0] - TimeSpan.FromSeconds(1), StackSkill: true);
        double? last = null;
        int casts = 0;
        for (int i = 0; i < f.Frames.Count; i++)
        {
            var r = SlotClassifier.Read(slot, f.Frames[i], f.Pts[i], null, digits);
            var next = Core.Cooldown.CooldownTracker.Update(Core.Cooldown.CooldownTracker.Advance(s, f.Pts[i]), r);
            if (next.Phase == SlotPhase.Cooldown && (s.Phase != SlotPhase.Cooldown || next.ChangedAt != s.ChangedAt)) casts++;
            s = next;
            var p = s.Phase == SlotPhase.Cooldown ? s.Progress(f.Pts[i]) : null;
            if (r is { Kind: ReadKind.Seconds, Value: 1 }) Assert.True(p is null, $"sweep {p:0.00} in the lockout at {f.Pts[i].TotalSeconds:0.000}");
            if (p is { } now && last is { } before) Assert.True(now >= before - 1e-9, $"sweep went back at {f.Pts[i].TotalSeconds:0.000}");
            if (p is not null) last = p;
        }
        Assert.Equal(1, casts);
        Assert.NotNull(last);   // the recharge timer was reached and swept
    }

    // A stack skill recharging with a use left shows its count ("1") beside the timer; ordinary cooldowns show none.
    [Theory]
    [InlineData("lt2_stack1_일반3", true)]
    [InlineData("lt_stack1_일반1", true)]
    [InlineData("lt2_11_일반4", false)]
    [InlineData("lt2_11_일반5", false)]
    [InlineData("lt2_11_일반6", false)]
    [InlineData("lt2_11_일반7", false)]
    [InlineData("lt2_11_일반8", false)]
    [InlineData("lt2_19_초각스", false)]
    [InlineData("lt2_31_초각스", false)]
    public void A_stack_count_beside_the_timer_marks_the_skill_usable(string name, bool stacked)
    {
        var f = Fixture.Load(name);
        var r = SlotClassifier.Read(Enum.Parse<SlotId>(f.Slot), f.Frames[0], TimeSpan.Zero, null, new DigitReader(1));
        Assert.Equal(ReadKind.Seconds, r.Kind);
        Assert.Equal(stacked, r.Stacked);
    }

    /// <summary>bugEskill.mp4 (10 fps, 14s → 6s): D is an ordinary cooldown, but its icon art took a count.</summary>
    [Fact]
    public void An_ordinary_cooldown_never_shows_a_stack_count()
    {
        var f = Fixture.Load("eskill_일반7");
        var digits = new DigitReader(1);
        var stacked = f.Frames.Select((img, i) => (i, r: SlotClassifier.Read(SlotId.일반7, img, TimeSpan.Zero, null, digits)))
            .Where(x => x.r.Stacked).Select(x => $"{x.i} ({x.r.Value}s)").ToList();
        Assert.True(stacked.Count == 0, string.Join(", ", stacked));
    }

    /// <summary>발키리 해방자 Z and C at 2 fps (buff_gameplay.mp4, buff_wrong_detect.mp4): lit or dark (not enough resource),
    /// per the brightness of the icon's middle (L about 150–160, D about 57–60; ? = a fade or the cast flash, not checked).</summary>
    [Theory]
    [InlineData("valk_gameplay_Z", "LLLLLLLLLLLLL?DDDDDDDDDDDDDDDDLLLLLLLLLLLL?DDDDDDLLLL?DDDDDD")]
    [InlineData("valk_gameplay_C", "LLLLLLLLLLLLLLLLLLL??DDDDDDDDDDDDDDDDDDDDD?LLLLLLLLLLL?DDDDD")]
    [InlineData("valk_wrong_Z", "DDDDDDDDDDDL?DDDDDDDD")]
    [InlineData("valk_wrong_C", "DDDDDDDDDDDD?LLLLLLLL")]
    public void A_dark_aden_skill_reads_unavailable(string name, string expected)
    {
        var f = Fixture.Load(name);
        var slot = Enum.Parse<SlotId>(f.Slot);
        var digits = new DigitReader(1);
        var got = string.Concat(f.Frames.Select((img, i) => expected[i] == '?' ? '?'
            : SlotClassifier.Read(slot, img, TimeSpan.Zero, null, digits, DarkCue.Icon).Kind switch
            {
                ReadKind.Unavailable => 'D',
                ReadKind.Ready => 'L',
                var k => k.ToString()[0],
            }));
        Assert.Equal(expected, got);
        // Without the class flag, dark is still Ready (cooldowns only).
        Assert.DoesNotContain(f.Frames, img => SlotClassifier.Read(slot, img, TimeSpan.Zero, null, digits).Kind == ReadKind.Unavailable);
    }

    [Fact]
    public void Nocd_screenshot_is_all_ready()
    {
        var digits = new DigitReader(1);
        foreach (var slot in SlotInfo.All)
        {
            var img = Fixture.Load($"Nocd_{slot}").Frames[0];
            Assert.Equal(ReadKind.Ready, SlotClassifier.Read(slot, img, TimeSpan.Zero, null, digits).Kind);
            Assert.Equal(ReadKind.Ready, SlotClassifier.Read(slot, img, TimeSpan.Zero, new SlotCalibration(img), digits).Kind);
        }
    }

    // Only cooldowns are tracked, not whether a skill can be used right now: a skill greyed by 마나 부족 or tinted by
    // 침묵 still reads by its timer, and without one it is off cooldown.
    [Theory]
    [InlineData("nomana")]
    [InlineData("silenced")]
    public void A_greyed_or_tinted_icon_reads_by_its_cooldown_alone(string shot)
    {
        var digits = new DigitReader(1);
        foreach (var slot in SlotInfo.All)
        {
            var r = SlotClassifier.Read(slot, Fixture.Load($"{shot}_{slot}").Frames[0], TimeSpan.Zero, null, digits);
            Assert.True(r.Kind is ReadKind.Ready or ReadKind.Seconds or ReadKind.Minutes, $"{slot}: {r.Kind}");
        }
    }

    // other_*: the ready icons 스킬창 맞추기 saved on one character; lt2_clear_*: the same slots on another character,
    // whose skills look nothing alike. What the two share is the key label the game draws over every icon.
    [Theory]
    [InlineData("각성기")]
    [InlineData("일반1")]
    [InlineData("일반2")]
    [InlineData("일반3")]
    [InlineData("일반4")]
    [InlineData("일반5")]
    [InlineData("일반6")]
    [InlineData("일반7")]
    [InlineData("일반8")]
    public void Another_characters_icon_on_the_same_key_still_matches_the_slot(string slot)
    {
        var cal = new SlotCalibration(Fixture.Load($"other_{slot}").Frames[0]);
        Assert.InRange(cal.Similarity(Fixture.Load($"lt2_clear_{slot}").Frames[0]), 0.6, 1);
    }

    [Theory]
    [InlineData("일반5")]
    [InlineData("일반6")]
    [InlineData("일반7")]
    [InlineData("일반8")]
    public void A_slot_under_the_taskbar_is_unreadable(string slot)
    {
        var cal = new SlotCalibration(Fixture.Load($"lt2_clear_{slot}").Frames[0]);
        var r = SlotClassifier.Read(Enum.Parse<SlotId>(slot), Fixture.Load($"lt2_taskbar_{slot}").Frames[0], TimeSpan.Zero, cal, new DigitReader(1));
        Assert.Equal(ReadKind.Unreadable, r.Kind);
    }

    [Fact]
    public void Forced_21_9_reads_55s_at_three_quarter_scale()
    {
        var vp = new Viewport(0, 135, 1920, 810);
        var fit = new Fit(110);
        var band = Fixture.Load("band_219");
        var r = Geometry.Slot(SlotId.초각스, vp, fit);
        var crop = Crop(band.Frames[0], r.X - band.Rect[0], r.Y - band.Rect[1], r.Width, r.Height);
        var read = SlotClassifier.Read(SlotId.초각스, crop, TimeSpan.Zero, null, new DigitReader(Geometry.Scale(vp, fit)));
        Assert.Equal((ReadKind.Seconds, 55), (read.Kind, read.Value));
    }

    [Fact]
    public void Slot_read_is_fast_and_allocation_free()
    {
        var f = Fixture.Load("gameplay_D_end");
        var cal = new SlotCalibration(f.Frames[110]);
        var digits = new DigitReader(Geometry.Scale(Fhd, new Fit(100)));
        for (int k = 0; k < 20; k++)   // past tiered JIT
            for (int i = 0; i < f.Frames.Count; i++) SlotClassifier.Read(SlotId.일반7, f.Frames[i], TimeSpan.Zero, cal, digits);

        var sw = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        const int rounds = 20;
        for (int k = 0; k < rounds; k++)
            for (int i = 0; i < f.Frames.Count; i++) SlotClassifier.Read(SlotId.일반7, f.Frames[i], TimeSpan.Zero, cal, digits);
        sw.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        double us = sw.Elapsed.TotalMicroseconds / (rounds * f.Frames.Count);
        log.WriteLine($"{us:F1} µs per slot read, {allocated} bytes allocated");
        Assert.Equal(0, allocated);
        Assert.True(us < 1000, $"{us:F1} µs per read");
    }

    internal static Image Crop(Image src, int x, int y, int w, int h)
    {
        var dst = new Image(w, h);
        for (int row = 0; row < h; row++)
            src.Bgra.AsSpan((y + row) * src.Stride + x * 4, w * 4).CopyTo(dst.Bgra.AsSpan(row * dst.Stride));
        return dst;
    }
}


[CollectionDefinition(nameof(VisionReplayTests), DisableParallelization = true)]
public class VisionReplayCollection;
