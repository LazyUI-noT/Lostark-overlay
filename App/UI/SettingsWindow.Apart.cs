using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>합쳐서 / 따로 (class-profiles.plan.md "합쳐서 / 따로"): the one switch, built the same on the 스킬창, 버프 줄 and 아덴 게이지
/// panels. It is <c>Settings.Apart</c> (<c>Buffs.Detached</c>, one per profile like all of 버프 막대), so changing it in one shows in all.</summary>
public partial class SettingsWindow
{
    enum Split { 합쳐서, 따로 }

    /// <summary>스킬창 page's 창 구성 section, and the inspector's 스킬창 panel.</summary>
    readonly CardStack _secApart = new();

    const string ApartHelp = "합쳐서: 스킬 칸, 아덴 게이지, 버프 막대가 한 창에 있습니다. 따로: 아덴 게이지와 버프 막대가 각자 창을 갖고, 화면에서 따로 옮기고 크기를 바꿉니다. " +
        "프로필마다 저장되고, 그 프로필의 모든 직업각인이 같이 씁니다.";

    void FillApart()
    {
        var p = Refill(_secApart, "창 구성");
        p.Children.Add(SwitchBadge());
        p.Children.Add(ApartRow());
    }

    /// <summary>The switch row, the same on every panel that shows it.</summary>
    UIElement ApartRow() => Defaulted(HelpRow("합쳐서 / 따로", ApartHelp,
            EnumCombo([(Split.합쳐서, "합쳐서"), (Split.따로, "따로")], _s.Apart ? Split.따로 : Split.합쳐서, v => { _s.Apart = v == Split.따로; Dispatcher.BeginInvoke(RefillApart); })),
        s => s.Apart, (s, v) => s.Apart = v);

    /// <summary>The chip for the switch (one per profile, like all of 버프 막대) on the 스킬창 page; hidden in the inspector, whose tab chip covers it.</summary>
    UIElement SwitchBadge() => new Border { Child = ProfileBadgeChip(profileOnly: true), Margin = new(0, 2, 0, 4), Tag = CardBadgeTag };

    /// <summary>A change of the switch: every panel that shows it (and what depends on it) is built again.</summary>
    void RefillApart()
    {
        FillApart();
        FillBuffs();
        FillGauge();
        RefreshInspector();
    }

    /// <summary>A row that only means something under 따로: greyed with <paramref name="why"/> under it on 합쳐서; its value is kept.</summary>
    UIElement[] ApartOnly(UIElement row, string why) =>
        _s.Apart ? [row] : [Greyed(row), Note(why)];

    static UIElement Greyed(UIElement row)
    {
        row.IsEnabled = false;
        return row;
    }
}
