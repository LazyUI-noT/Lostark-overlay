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
        var o = Target.Overlay;   // 편집 중's 스킬칸 look (BoxLooks), one per profile
        p.Children.Add(Check("빈 칸 줄이기", o.CloseGaps, v => o.CloseGaps = v, Changed, CloseGapsHelp));
    }

    const string CloseGapsHelp = "표시를 끈 칸 자리를 옆 칸이 채우고, 줄이나 열이 다 비면 창을 줄입니다. 버프 막대도 같습니다.";

    /// <summary>The switch row, the same on every panel that shows it.</summary>
    UIElement ApartRow() => Defaulted(HelpRow("합쳐서 / 따로", ApartHelp,
            EnumCombo([(Split.합쳐서, "합쳐서"), (Split.따로, "따로")], _s.Apart ? Split.따로 : Split.합쳐서, v => { _s.Apart = v == Split.따로; Dispatcher.BeginInvoke(RefillApart); })),
        s => s.Apart, (s, v) => s.Apart = v);

    /// <summary>The chip for the switch (one per profile, like all of 버프 막대) on the 스킬창 page; hidden in the inspector, whose tab chip covers it.</summary>
    UIElement SwitchBadge() => new Border { Child = ProfileBadgeChip(profileOnly: true), Margin = new(0, Tokens.Space.Hair, 0, Tokens.Space.Tight), Tag = CardBadgeTag };

    /// <summary>A change of the switch: every panel that shows it (and what depends on it) is built again.</summary>
    void RefillApart()
    {
        FillApart();
        FillBuffs();
        FillGauge();
        RefreshInspector();
    }

    const string DockDirectionHelp = "자동은 스킬 칸 위나 아래에 붙으면 가로로, 옆에 붙으면 세로로 그립니다. 붙는 쪽은 편집 모드에서 끌어서 바꿉니다.";

    /// <summary>방향, the same row on the 게이지 and 버프 panels (slot-layout.plan.md §3). 합쳐서: 자동/가로/세로 in the dock
    /// order (<see cref="DockOrder"/>). 따로: 가로/세로 on the group's own window (<paramref name="get"/>/<paramref name="set"/>).</summary>
    UIElement DirectionRow(DockGroup group, string apartHelp, Func<Settings, bool> get, Action<Settings, bool> set, Settings? on = null)
    {
        if (_s.Apart)
        {
            var combo = EnumCombo([(Direction.Flat, "가로"), (Direction.Upright, "세로")],
                get(on ?? _s) ? Direction.Upright : Direction.Flat, v => { set(on ?? _s, v == Direction.Upright); Changed(); });
            if (group == DockGroup.Buffs && ClassProfiles.BuffCells(_s) is not null)
            {
                // 자유 배치 with moved bars draws them where they were put; 방향 waits for 칸 맞춤 (the cells are kept).
                (combo.IsEnabled, combo.ToolTip) = (false, "자유 배치에서 막대를 옮기면 옮긴 자리대로 그립니다. 방향은 칸 맞춤으로 바꾸면 다시 적용됩니다.");
                ToolTipService.SetShowOnDisabled(combo, true);
            }
            return Defaulted(HelpRow("방향", apartHelp, combo), get, set, on);
        }
        Direction Of(Settings s) => DockOrder.DirectionOf(DockOrder.Of(s.Overlay.DockOrder), group);
        void Put(Settings s, Direction v) => s.Overlay.DockOrder = DockOrder.Format(DockOrder.WithDirection(DockOrder.Of(s.Overlay.DockOrder), group, v));
        string help = group == DockGroup.Gauge ? DockDirectionHelp + " 게이지는 스킬 칸 위나 아래에 붙으면 세로를 골라도 가로로 그립니다." : DockDirectionHelp;
        return Defaulted(HelpRow("방향", help, EnumCombo([(Direction.Auto, "자동"), (Direction.Flat, "가로"), (Direction.Upright, "세로")],
            Of(_s), v => { Put(_s, v); Changed(); })), Of, Put);
    }
}
