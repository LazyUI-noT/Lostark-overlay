using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Debuff cadence v2 (the user's spec, 2026-10-30): the frame check of the filled boxes and the first empty one 5/s,
/// the icons identified 2/s or at once when the row shifts or a timer can't be read, the timers of timed boxes (번개, 교체,
/// 결투자) 5/s on the frame pass and 10/s on their own while under 2 s, following the box a debuff moved to.</summary>
public class DebuffScheduleTests
{
    static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Nothing_up_checks_box_one_five_times_a_second()
    {
        var s = DebuffSlots.None;
        Assert.Equal(1, DebuffSchedule.Boxes(s));
        Assert.Equal(DebuffPass.Frames, DebuffSchedule.Due(s, Ms(1000)));
        s = DebuffSchedule.Frames(s, 0, [], [], Ms(1000));
        Assert.Equal(DebuffPass.None, DebuffSchedule.Due(s, Ms(1150)));
        Assert.Equal(DebuffPass.Frames, DebuffSchedule.Due(s, Ms(1200)));
    }

    [Fact]
    public void Filled_boxes_and_the_next_empty_one_are_checked()
    {
        var s = DebuffSchedule.Frames(DebuffSlots.None, 2, [DebuffKind.빨강, DebuffKind.파랑], [null, null], Ms(0));
        Assert.Equal(3, DebuffSchedule.Boxes(s));
    }

    [Fact]
    public void Icons_are_identified_twice_a_second_or_at_once_when_the_count_changes()
    {
        var s = DebuffSchedule.Frames(DebuffSlots.None, 1, [DebuffKind.빨강], [null], Ms(0));
        Assert.False(DebuffSchedule.Classify(s, 1, Ms(200)));
        Assert.True(DebuffSchedule.Classify(s, 1, Ms(500)));
        Assert.True(DebuffSchedule.Classify(s, 2, Ms(200)));   // a debuff came: the row may have shifted
        Assert.True(DebuffSchedule.Classify(s, 0, Ms(200)));
        s = DebuffSchedule.Frames(s, 1, null, [null], Ms(200));   // kept, not identified again
        Assert.Equal([DebuffKind.빨강], s.Kinds);
        Assert.False(DebuffSchedule.Classify(s, 1, Ms(400)));   // the clock runs from the last identification (0)
    }

    [Fact]
    public void A_timed_box_whose_timer_was_not_read_is_identified_again_at_once()
    {
        var s = DebuffSchedule.Frames(DebuffSlots.None, 1, [DebuffKind.번개], [null], Ms(0));
        Assert.True(DebuffSchedule.Classify(s, 1, Ms(200)));
    }

    [Fact]
    public void A_timer_under_two_seconds_is_read_ten_times_a_second_between_frame_passes()
    {
        var s = DebuffSchedule.Frames(DebuffSlots.None, 2, [DebuffKind.빨강, DebuffKind.번개], [null, 1.5], Ms(0));
        Assert.Equal([1], DebuffSchedule.FastTimers(s));
        Assert.Equal(DebuffPass.None, DebuffSchedule.Due(s, Ms(50)));
        Assert.Equal(DebuffPass.Timers, DebuffSchedule.Due(s, Ms(100)));
        s = DebuffSchedule.Timers(s, new Dictionary<int, double?> { [1] = 1.4 }, Ms(100));
        Assert.Equal([null, 1.4], s.Seconds);
        Assert.Equal(DebuffPass.Frames, DebuffSchedule.Due(s, Ms(200)));   // the frame pass wins when both are due
    }

    [Fact]
    public void A_timer_at_two_seconds_or_more_is_read_only_on_the_frame_pass()
    {
        var s = DebuffSchedule.Frames(DebuffSlots.None, 1, [DebuffKind.교체], [2.0], Ms(0));
        Assert.Empty(DebuffSchedule.FastTimers(s));
        Assert.Equal(DebuffPass.None, DebuffSchedule.Due(s, Ms(150)));
    }

    [Fact]
    public void The_duelists_timer_is_read_ten_times_a_second_at_any_time()
    {
        // Review (2026-11-01): its 4 → 0 is dated from the tick it sees, so reading it 5/s put the count up to 0.2 s late (the
        // old engine read it 10/s throughout).
        var s = DebuffSchedule.Frames(DebuffSlots.None, 2, [DebuffKind.파랑, DebuffKind.결투자], [null, 38.0], Ms(0));
        Assert.Equal([1], DebuffSchedule.FastTimers(s));
        Assert.Equal(DebuffPass.Timers, DebuffSchedule.Due(s, Ms(100)));
    }

    [Fact]
    public void The_fast_timer_follows_the_box_its_debuff_moved_to()
    {
        // The user's example: [debuff, 번개 1.5 s, empty] → [번개 1.2 s, empty, empty]: box 1's timer at 10/s, box 2 no more.
        var s = DebuffSchedule.Frames(DebuffSlots.None, 2, [DebuffKind.빨강, DebuffKind.번개], [null, 1.5], Ms(0));
        Assert.Equal([1], DebuffSchedule.FastTimers(s));
        Assert.True(DebuffSchedule.Classify(s, 1, Ms(200)));   // one box fewer: identify again
        s = DebuffSchedule.Frames(s, 1, [DebuffKind.번개], [1.2], Ms(200));
        Assert.Equal([0], DebuffSchedule.FastTimers(s));
        Assert.Equal(2, DebuffSchedule.Boxes(s));
    }

    [Fact]
    public void A_failed_fast_read_asks_for_identification_on_the_next_frame_pass()
    {
        var s = DebuffSchedule.Frames(DebuffSlots.None, 1, [DebuffKind.번개], [1.5], Ms(0));
        s = DebuffSchedule.Timers(s, new Dictionary<int, double?> { [0] = null }, Ms(100));
        Assert.True(DebuffSchedule.Classify(s, 1, Ms(200)));
    }

    [Fact]
    public void Only_timed_debuffs_are_timed()
    {
        Assert.True(DebuffSchedule.Timed(DebuffKind.번개));
        Assert.True(DebuffSchedule.Timed(DebuffKind.교체));
        Assert.True(DebuffSchedule.Timed(DebuffKind.결투자));
        Assert.False(DebuffSchedule.Timed(DebuffKind.빨강));
        Assert.False(DebuffSchedule.Timed(null));
    }

    static TimeSpan? LightningEnd(DebuffStatus s) => s.Lightning?.EndsAt;
    static DebuffStatus Read(DebuffStatus s, double seconds, double at) =>
        DebuffTracker.Update(s, [new(DebuffKind.번개, seconds)], null, null, TimeSpan.FromSeconds(at));

    [Fact]
    public void A_whole_second_timer_keeps_its_end_while_reads_agree_and_moves_it_only_as_far_as_they_need()
    {
        // "N초" is N to N+1 s left. The first read takes the middle; later reads keep the end unless it falls outside their range.
        var s = Read(DebuffStatus.None, 5, 10);
        Assert.Equal(TimeSpan.FromSeconds(15.5), LightningEnd(s));
        s = Read(s, 5, 10.4);   // 15.4–16.4: 15.5 fits
        Assert.Equal(TimeSpan.FromSeconds(15.5), LightningEnd(s));
        s = Read(s, 4, 11.6);   // 15.6–16.6: the end moves up to 15.6, not to the middle
        Assert.Equal(TimeSpan.FromSeconds(15.6), LightningEnd(s));
    }

    [Fact]
    public void Tenths_are_exact()
    {
        var s = Read(DebuffStatus.None, 5, 10);
        s = Read(s, 0.8, 14.9);
        Assert.Equal(TimeSpan.FromSeconds(15.7), LightningEnd(s));
    }
}
