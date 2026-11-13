using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Where the "프로필 N · 클래스" badge shows in 설정 (class-profiles.plan.md, Intent F): once per tab or section, never per setting.</summary>
public class ProfileBadgeTests
{
    [Fact]
    public void Every_LookGroup_has_exactly_one_badge_home()
    {
        foreach (var look in Enum.GetValues<LookGroup>())
        {
            var (area, section) = ProfileBadge.Home(look);
            var at = ProfileBadge.Of(area, section);
            Assert.True(at is BadgeAt.Tab or BadgeAt.Section, $"{look}: {at}");
        }
    }

    [Fact]
    public void Inspector_hosted_section_has_no_badge()
    {
        Assert.Equal(BadgeAt.None, ProfileBadge.Of(ProfileBadge.OverlayTab, "배경판", inInspector: true));
        Assert.Equal(BadgeAt.None, ProfileBadge.Of("단심", "단심", inInspector: true));
    }

    [Fact]
    public void Global_tabs_have_none()
    {
        foreach (var area in new[] { "경고", "키·단축키", "로아 API", "표시할 화면", "시스템 성능", "테마·업데이트" })
            Assert.Equal(BadgeAt.None, ProfileBadge.Of(area));
    }

    [Fact]
    public void Overlay_tab_has_the_tab_badge_and_the_raid_count_note_the_option_badge()
    {
        Assert.Equal(BadgeAt.Tab, ProfileBadge.Of(ProfileBadge.OverlayTab));
        Assert.Equal(BadgeAt.Option, ProfileBadge.Of("레이드", option: ProfileBadge.RaidCountNote));
        Assert.Equal(BadgeAt.None, ProfileBadge.Of("레이드"));
    }

    [Fact]
    public void Text_names_class_when_known()
    {
        Assert.Equal("프로필 2 · 블래스터 · 포격 모드", ProfileBadge.Text(1, "블래스터 · 포격 모드"));
        Assert.Equal("프로필 1 · 기본", ProfileBadge.Text(0, null));
        Assert.Equal("프로필 1 · 기본", ProfileBadge.Text(0, ""));
    }

    [Fact]
    public void Gauge_look_and_the_split_switch_have_their_own_pages()
    {
        Assert.Equal(("아덴 게이지", (string?)null), ProfileBadge.Home(LookGroup.게이지));
        Assert.Equal(BadgeAt.Section, ProfileBadge.Of("아덴 게이지"));
        Assert.Equal(BadgeAt.Section, ProfileBadge.Of("스킬창", "창 구성"));
    }
}
