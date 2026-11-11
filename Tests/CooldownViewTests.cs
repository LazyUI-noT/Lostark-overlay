using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>쿨 진행 표시 (style + direction) and 남은 쿨 (초) are per class (display-split.plan.md step 5): your boxes draw
/// with the class's, a party member's with your 나 settings of their class, and 복사 copies each on its own box.</summary>
public class CooldownViewTests
{
    const string Bard = "바드", Warlord = "워로드";

    [Fact]
    public void Your_boxes_draw_with_the_classs_cooldown_view()
    {
        var s = new Settings();
        var bard = ClassProfiles.Edit(s, Bard).Mine;
        (bard.CooldownStyle, bard.FillFrom, bard.ShowTimer) = (CooldownStyle.Fill, FillFrom.Left, false);

        Assert.Equal(new CooldownView(CooldownStyle.Fill, FillFrom.Left, false), ClassProfiles.Mine(s, Bard).Cooldown);
        Assert.Equal(new CooldownView(), ClassProfiles.Mine(s, Warlord).Cooldown);   // follows 기본
    }

    [Fact]
    public void A_party_members_boxes_take_your_style_for_their_class_or_기본s()
    {
        var s = new Settings();
        ClassProfiles.Edit(s, ClassProfiles.Template).Mine.CooldownStyle = CooldownStyle.FillHatched;
        ClassProfiles.Edit(s, Bard).Mine.CooldownStyle = CooldownStyle.Fill;

        Assert.Equal(CooldownStyle.Fill, ClassProfiles.Member(s, Bard, Role.서포터, null).Cooldown.Style);
        Assert.Equal(CooldownStyle.FillHatched, ClassProfiles.Member(s, Warlord, Role.딜러, null).Cooldown.Style);
    }

    [Fact]
    public void The_progress_box_copies_style_and_direction_together()
    {
        var (_, to) = CopyFromBard(CopyParts.Progress);
        Assert.Equal(new CooldownView(CooldownStyle.Fill, FillFrom.Right, true), to.Cooldown);
    }

    [Fact]
    public void The_timer_box_copies_남은_쿨_only()
    {
        var (_, to) = CopyFromBard(CopyParts.Timer);
        Assert.Equal(new CooldownView(ShowTimer: false), to.Cooldown);
    }

    [Fact]
    public void The_older_copy_boxes_leave_the_cooldown_view()
    {
        var (_, to) = CopyFromBard(CopyParts.Color | CopyParts.Size | CopyParts.Layout | CopyParts.Display | CopyParts.Effects);
        Assert.Equal(new CooldownView(), to.Cooldown);
    }

    static (Settings, MineProfile) CopyFromBard(CopyParts parts)
    {
        var s = new Settings();
        var from = ClassProfiles.Edit(s, Bard).Mine;
        (from.CooldownStyle, from.FillFrom, from.ShowTimer) = (CooldownStyle.Fill, FillFrom.Right, false);
        var to = ClassProfiles.Edit(s, Warlord).Mine;
        ClassProfiles.Copy(s, from, [to], parts);
        return (s, to);
    }
}
