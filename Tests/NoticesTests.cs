using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 경고 → Windows 알림: the tray's notices in three kinds (안내 / 파티 / 문제), each on until turned off.</summary>
public class NoticesTests
{
    [Theory]
    [InlineData(NoticeKind.Guide)]
    [InlineData(NoticeKind.Party)]
    [InlineData(NoticeKind.Problem)]
    public void Every_kind_shows_by_default(NoticeKind kind) =>
        Assert.True(Notices.Shows(new Settings(), kind));

    [Fact]
    public void A_muted_kind_is_silent_and_the_others_still_show()
    {
        var s = new Settings { MutedNotices = [NoticeKind.Party] };

        Assert.False(Notices.Shows(s, NoticeKind.Party));
        Assert.True(Notices.Shows(s, NoticeKind.Guide));
        Assert.True(Notices.Shows(s, NoticeKind.Problem));
    }

    [Fact]
    public void The_page_lists_each_kind_once_with_a_name_and_help()
    {
        Assert.Equal(Enum.GetValues<NoticeKind>(), Notices.All.Select(n => n.Kind));
        Assert.All(Notices.All, n => Assert.False(string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Help)));
    }

    [Fact]
    public void Muted_kinds_survive_a_save_and_load() =>
        Assert.Equal([NoticeKind.Party, NoticeKind.Problem],
            SettingsStore.Copy(new Settings { MutedNotices = [NoticeKind.Party, NoticeKind.Problem] }).MutedNotices.Order());

    [Fact]
    public void A_settings_file_with_a_null_list_loads_with_every_kind_on()
    {
        var s = SettingsStore.Normalize(new Settings { MutedNotices = null! });

        Assert.Empty(s.MutedNotices);
    }
}
