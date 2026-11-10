using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>스킬 사용 횟수: the meter's count from the slots, the timeline's average, and whose pick each one reads.</summary>
public class SkillRateTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    static FightClockState Clock(int timer, double now, int fight = 1) => new(timer, S(now), S(now), fight, false);

    static IReadOnlyList<SlotState> Slots(SlotPhase q) =>
        [.. Snapshot.Empty(S(0)).Slots.Select(s => s.Slot == SlotId.일반1 ? s with { Phase = q } : s)];

    static void Cast(CastLog log, int timer, int fight = 1)
    {
        log.Observe(Slots(SlotPhase.Ready), Clock(timer, timer, fight), S(timer));
        log.Observe(Slots(SlotPhase.Cooldown), Clock(timer, timer, fight), S(timer + 0.1));
    }

    [Fact]
    public void Whole_fight_rate_is_casts_over_the_minutes_on_the_timer()
    {
        var log = new CastLog();
        foreach (int t in new[] { 10, 40, 70 }) Cast(log, t);
        Assert.Equal(1.5, log.PerMinute(SlotId.일반1, 120, recent: false));
    }

    [Fact]
    public void Recent_rate_counts_only_the_last_minute()
    {
        var log = new CastLog();
        foreach (int t in new[] { 10, 40, 70, 100 }) Cast(log, t);
        Assert.Equal(2, log.PerMinute(SlotId.일반1, 120, recent: true));
    }

    [Fact]
    public void Recent_rate_of_a_fight_under_a_minute_is_scaled_to_a_minute()
    {
        var log = new CastLog();
        Cast(log, 10);
        Assert.Equal(2, log.PerMinute(SlotId.일반1, 30, recent: true));
    }

    [Fact]
    public void The_first_seconds_give_no_rate()
    {
        var log = new CastLog();
        Cast(log, 2);
        Assert.Null(log.PerMinute(SlotId.일반1, CastLog.MinSeconds - 1, recent: false));
    }

    [Fact]
    public void A_slot_never_read_gives_no_rate_and_a_read_one_without_casts_gives_zero()
    {
        var log = new CastLog();
        Assert.Null(log.PerMinute(SlotId.일반1, 60, recent: false));
        log.Observe(Slots(SlotPhase.Ready), Clock(20, 20), S(20));
        Assert.Equal(0, log.PerMinute(SlotId.일반1, 60, recent: false));
        Assert.Null(log.PerMinute(SlotId.일반2, 60, recent: false));
    }

    [Fact]
    public void A_cast_while_the_timer_stands_still_is_not_counted()
    {
        var log = new CastLog();
        var stopped = Clock(30, 30) with { Frozen = true };
        log.Observe(Slots(SlotPhase.Ready), stopped, S(35));
        log.Observe(Slots(SlotPhase.Cooldown), stopped, S(35.1));
        Assert.Equal(0, log.PerMinute(SlotId.일반1, 60, recent: false));
    }

    [Fact]
    public void A_new_record_starts_the_count_over()
    {
        var log = new CastLog();
        Cast(log, 10);
        Cast(log, 12, fight: 2);
        Assert.Equal(1, log.PerMinute(SlotId.일반1, 60, recent: false));
    }

    static Settings Picked(SlotId? slot, string? name = null)
    {
        var s = new Settings();
        s.Meter.On = true;
        s.Overlay.SkillRead = true;
        var w = ClassProfiles.EditWindows(s, "워로드");
        (w.MeterSkill, w.MeterSkillName) = (slot, name);
        return s;
    }

    static string Key(SlotId slot) => SlotInfo.DefaultKey(slot);

    [Fact]
    public void No_pick_no_line()
    {
        Assert.Null(MeterSkill.Line(Picked(null), "워로드", new MeterHistory(), 60, Key));
    }

    [Fact]
    public void The_line_is_named_by_the_key_until_the_user_names_it()
    {
        Assert.Equal("Q", MeterSkill.Line(Picked(SlotId.일반1), "워로드", new MeterHistory(), 60, Key)!.Name);
        Assert.Equal("Q", MeterSkill.Line(Picked(SlotId.일반1, "  "), "워로드", new MeterHistory(), 60, Key)!.Name);
        Assert.Equal("돌진", MeterSkill.Line(Picked(SlotId.일반1, "돌진"), "워로드", new MeterHistory(), 60, Key)!.Name);
    }

    [Fact]
    public void With_skill_reading_off_the_line_says_so_instead_of_a_stale_number()
    {
        var s = Picked(SlotId.일반1);
        var h = new MeterHistory();
        Cast(h.Casts, 10);
        Assert.Equal(1, MeterSkill.Line(s, "워로드", h, 60, Key)!.PerMinute);
        s.Overlay.SkillRead = false;
        Assert.Null(MeterSkill.Line(s, "워로드", h, 60, Key)!.PerMinute);
    }

    [Fact]
    public void The_count_follows_the_graph_range_only_when_asked()
    {
        var s = Picked(SlotId.일반1);
        s.Meter.Range = MeterRange.최근1분;
        var h = new MeterHistory();
        foreach (int t in new[] { 10, 20, 100 }) Cast(h.Casts, t);
        Assert.Equal(1, MeterSkill.Line(s, "워로드", h, 120, Key)!.PerMinute);     // one cast in the last minute
        s.Meter.SkillFollowsRange = false;
        Assert.Equal(1.5, MeterSkill.Line(s, "워로드", h, 120, Key)!.PerMinute);   // three over two minutes
    }

    [Fact]
    public void The_timeline_pick_and_the_meters_skill_are_kept_apart()   // user, 2026-11-09: not shared
    {
        var w = new CharacterWindows { MeterSkill = SlotId.일반1 };
        Assert.Null(w.TimelineSkill);   // the meter's skill isn't the timeline's
        w.TimelineSkill = SlotId.일반3;
        Assert.Equal(SlotId.일반1, w.MeterSkill);
        w.SetMeterSkill(SlotId.일반2);
        Assert.Equal(SlotId.일반3, w.TimelineSkill);
    }

    [Fact]
    public void A_settings_file_with_the_old_none_flag_still_loads()
    {
        var w = System.Text.Json.JsonSerializer.Deserialize<CharacterWindows>("""{"TimelineSkill":null,"TimelineNoSkill":true}""");
        Assert.Null(w!.TimelineSkill);
    }

    static RecordEvent Use(double t, SlotId slot, string who = "나") => new(t, who, RecordEventKind.사용, (int)slot);

    static FightRecord Fight(double duration, params RecordEvent[] events) =>
        new(DateTime.MinValue, null, duration, [], "나", 0, null, [], [], [], events);

    [Fact]
    public void Timeline_average_counts_my_casts_of_that_slot_over_the_fight()
    {
        var r = Fight(120, Use(1, SlotId.일반1), Use(2, SlotId.일반1), Use(3, SlotId.일반1), Use(4, SlotId.일반2), Use(5, SlotId.일반1, "민수"));
        Assert.Equal(1.5, Timeline.SkillRate(r, SlotId.일반1, []));
    }

    [Fact]
    public void Excluded_time_takes_off_its_minutes_and_its_casts()
    {
        var r = Fight(120, Use(10, SlotId.일반1), Use(70, SlotId.일반1));   // the second inside the band (user, 2026-11-09)
        Assert.Equal(1, Timeline.SkillRate(r, SlotId.일반1, [(60, 120)]));
    }

    [Fact]
    public void Excluded_time_past_the_fight_is_not_taken_off()
    {
        var r = Fight(120, Use(10, SlotId.일반1));
        Assert.Equal(1, Timeline.SkillRate(r, SlotId.일반1, [(60, 180)]));   // 60 s left
    }

    [Fact]
    public void A_fight_with_no_time_left_has_no_average()
    {
        Assert.Null(Timeline.SkillRate(Fight(60), SlotId.일반1, [(0, 60)]));
        Assert.Null(Timeline.SkillRate(Fight(0), SlotId.일반1, []));
    }

    [Fact]
    public void A_new_meter_skill_starts_on_its_key_not_the_old_skills_name()
    {
        var w = new CharacterWindows { MeterSkill = SlotId.일반1, MeterSkillName = "폭풍" };
        w.SetMeterSkill(SlotId.일반1);
        Assert.Equal("폭풍", w.NameOf(SlotId.일반1));
        w.SetMeterSkill(SlotId.일반3);
        Assert.Null(w.NameOf(SlotId.일반3));
    }
}
