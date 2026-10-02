using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>빛 색 직접 고르기: the ready glow is white until it is on, then the group's colour, or a skill's own where the skill
/// has its own colour (as 쿨타임 colours go). 준비 후 경과 시간: seconds a skill has been ready and unused, from 1 to 29.</summary>
public sealed class GlowAndCountTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void The_glow_is_white_until_its_colour_is_chosen()
    {
        var s = new Settings();
        var mine = ClassProfiles.Edit(s, null).Mine;
        mine.PulseColor[SlotGroup.초각스] = "#FF0000";
        Assert.Equal("#FFFFFF", ClassProfiles.Mine(s).GlowOf(SlotId.초각스, SlotGroup.초각스));

        mine.CustomGlow = true;
        Assert.Equal("#FF0000", ClassProfiles.Mine(s).GlowOf(SlotId.초각스, SlotGroup.초각스));
        Assert.Equal("#FFFFFF", ClassProfiles.Mine(s).GlowOf(SlotId.각성기, SlotGroup.각성기));
    }

    [Fact]
    public void A_skill_with_its_own_colour_glows_in_its_own_glow_colour()
    {
        var s = new Settings();
        var mine = ClassProfiles.Edit(s, null).Mine;
        (mine.CustomGlow, mine.Color, mine.SkillsUnified) = (true, ColorSource.Personal, false);
        mine.SlotColors[SlotId.일반1] = "#00FF00";
        mine.SlotPulseColors[SlotId.일반1] = "#0000FF";
        mine.PulseColor[SlotGroup.일반스킬] = "#FF00FF";
        Assert.Equal("#0000FF", ClassProfiles.Mine(s).GlowOf(SlotId.일반1, SlotGroup.일반스킬));
        Assert.Equal("#FF00FF", ClassProfiles.Mine(s).GlowOf(SlotId.일반2, SlotGroup.일반스킬));

        mine.SkillsUnified = true;   // 일반스킬 모두 같은 색: the group's glow too
        Assert.Equal("#FF00FF", ClassProfiles.Mine(s).GlowOf(SlotId.일반1, SlotGroup.일반스킬));
    }

    [Fact]
    public void Copying_effects_copies_the_glow_colours()
    {
        var s = new Settings();
        var from = ClassProfiles.Edit(s, null).Mine;
        (from.CustomGlow, from.SlotPulseColors[SlotId.일반3]) = (true, "#123456");
        var to = new MineProfile();
        ClassProfiles.Copy(s, from, [to], CopyParts.Effects);
        Assert.True(to.CustomGlow);
        Assert.Equal("#123456", to.SlotPulseColors[SlotId.일반3]);
        Assert.NotSame(from.SlotPulseColors, to.SlotPulseColors);
    }

    /// <summary>Before 빛 색 직접 고르기 a chosen glow colour always showed: a profile with one keeps showing it.</summary>
    [Fact]
    public void A_glow_colour_chosen_before_the_switch_turns_it_on()
    {
        var s = new Settings { Version = 3 };
        ClassProfiles.Edit(s, null).Mine.PulseColor[SlotGroup.일반스킬] = "#ffffff";
        ClassProfiles.Edit(s, "워로드").Mine.PulseColor[SlotGroup.초각스] = "#FF0000";
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(s);

        var back = store.Load(out _);
        Assert.False(ClassProfiles.Of(back, null).Mine.CustomGlow);
        Assert.True(ClassProfiles.Of(back, "워로드").Mine.CustomGlow);
        Assert.Equal(Settings.CurrentVersion, back.Version);
    }

    [Fact]
    public void The_ready_count_is_off_by_default_and_set_per_group_or_skill()
    {
        var m = new MineProfile();
        Assert.False(m.Counts(SlotId.일반1, SlotGroup.일반스킬));
        m.ReadyCount.Add(SlotGroup.일반스킬);
        m.SlotReadyCount[SlotId.일반2] = false;
        Assert.True(m.Counts(SlotId.일반1, SlotGroup.일반스킬));
        Assert.False(m.Counts(SlotId.일반2, SlotGroup.일반스킬));
        Assert.Equal("#000000", new OverlaySettings().ReadyCountColor);
    }

    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    static SlotState ReadyAt(double t) => new(SlotId.일반1, SlotPhase.Ready, null, null, false, S(t));

    [Fact]
    public void The_count_starts_at_1_a_second_after_ready_and_holds_at_29()
    {
        var s = ReadyAt(10);
        Assert.Null(CooldownLook.ReadyCount(s, S(10)));
        Assert.Null(CooldownLook.ReadyCount(s, S(10.99)));
        Assert.Equal(1, CooldownLook.ReadyCount(s, S(11)));
        Assert.Equal(7, CooldownLook.ReadyCount(s, S(17.5)));
        Assert.Equal(29, CooldownLook.ReadyCount(s, S(50)));
    }

    [Fact]
    public void There_is_no_count_while_cooling_greyed_or_unread()
    {
        var cooling = new SlotState(SlotId.일반1, SlotPhase.Cooldown, S(20), S(10), false, S(10));
        Assert.Null(CooldownLook.ReadyCount(cooling, S(15)));
        Assert.Null(CooldownLook.ReadyCount(ReadyAt(0) with { Phase = SlotPhase.Unavailable }, S(5)));
        Assert.Null(CooldownLook.ReadyCount(ReadyAt(0) with { Estimated = true }, S(5)));
    }

    [Fact]
    public void Holding_the_count_after_use_is_off_by_default_and_set_per_group_or_skill()
    {
        var m = new MineProfile();
        Assert.False(m.Holds(SlotId.일반1, SlotGroup.일반스킬));
        BoxOptions.SetHeldCount(m, SlotId.일반1, SlotGroup.일반스킬, true, onlyThis: false, []);
        BoxOptions.SetHeldCount(m, SlotId.일반2, SlotGroup.일반스킬, false, onlyThis: true, []);
        Assert.True(m.Holds(SlotId.일반1, SlotGroup.일반스킬));
        Assert.False(m.Holds(SlotId.일반2, SlotGroup.일반스킬));
    }

    static SlotState CoolingAt(double t) => new(SlotId.일반1, SlotPhase.Cooldown, S(t + 10), S(10), false, S(t));

    // Cooling, ready at `ready`, used at `used`: the held count as the renderer steps it frame by frame.
    static HeldCount Used(double ready, double used) =>
        new HeldCount().Next(CoolingAt(0)).Next(ReadyAt(ready)).Next(ReadyAt(ready)).Next(CoolingAt(used));

    [Fact]
    public void After_use_the_held_seconds_show_for_3_s()
    {
        var h = Used(ready: 10, used: 17.5);
        Assert.Equal(7, h.Shown(S(17.5)));
        Assert.Equal(7, h.Next(CoolingAt(17.5)).Shown(S(20.4)));
        Assert.Null(h.Shown(S(20.5)));
    }

    [Fact]
    public void The_held_count_is_capped_at_29_and_skipped_under_1_s()
    {
        Assert.Equal(29, Used(ready: 10, used: 60).Shown(S(60)));
        Assert.Null(Used(ready: 10, used: 10.9).Shown(S(10.9)));
    }

    [Fact]
    public void No_held_count_when_the_ready_period_was_not_read_from_its_start()
    {
        // Already ready when the overlay started (no cooldown seen before it).
        Assert.Null(new HeldCount().Next(ReadyAt(0)).Next(CoolingAt(8)).Shown(S(8)));
        // Ready only by estimate.
        Assert.Null(new HeldCount().Next(CoolingAt(0)).Next(ReadyAt(10) with { Estimated = true }).Next(CoolingAt(18)).Shown(S(18)));
        // Unread in between (loading screen).
        Assert.Null(new HeldCount().Next(CoolingAt(0)).Next(ReadyAt(10)).Next(ReadyAt(10) with { Phase = SlotPhase.Unknown })
            .Next(CoolingAt(18)).Shown(S(18)));
        // A use and its cooldown missed while the overlay wasn't drawn: the next ready period started unseen.
        Assert.Null(new HeldCount().Next(CoolingAt(0)).Next(ReadyAt(10)).Next(ReadyAt(30)).Next(CoolingAt(35)).Shown(S(35)));
    }

    [Fact]
    public void The_held_count_stops_when_the_skill_is_ready_again()
    {
        var h = Used(ready: 10, used: 15).Next(ReadyAt(16));
        Assert.Null(h.Shown(S(16)));
    }
}
