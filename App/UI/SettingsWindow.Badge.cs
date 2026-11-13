using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The "프로필 N · 클래스" chip (user, 2026-11-13; <see cref="ProfileBadge"/>): one chip style, shown where
/// <see cref="ProfileBadge.Of"/> says, refreshed whenever 설정 is rebuilt (a profile or class change) or 편집 중 changes. It
/// names 편집 중's 직업각인, what 설정 edits (edit-follows-pick.plan.md), not the played one.</summary>
public partial class SettingsWindow
{
    const string CardBadgeTag = "ProfileBadge";
    const string SkillColorsNote = "칸의 색과 효과는 프로필과 상관없이 직업각인마다 저장됩니다.";
    readonly List<(TextBlock Text, bool ProfileOnly)> _badgeTexts = [];

    /// <summary>The badge text for the picked profile and 편집 중 (기본 → 기본).</summary>
    string BadgeText() => ProfileBadge.Text(_s.ActiveProfile, ShownClass);

    string BadgeText(bool profileOnly) => profileOnly ? ProfileBadge.ProfileText(_s.ActiveProfile) : BadgeText();

    /// <summary>A new chip; it keeps itself current through <see cref="RefreshBadges"/>. <paramref name="profileOnly"/>: a
    /// setting every class in the profile shares (버프 막대), so the chip names no class.</summary>
    UIElement ProfileBadgeChip(bool profileOnly = false)
    {
        var text = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Text = BadgeText(profileOnly) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        _badgeTexts.Add((text, profileOnly));
        var chip = new Border { CornerRadius = new(10), BorderThickness = new(1), Padding = new(10, 2, 10, 3), Child = text, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = profileOnly ? "지금 편집하는 프로필입니다. 이 값은 이 프로필의 모든 직업각인이 같이 씁니다."
                : "지금 편집하는 프로필과 편집 중 직업각인입니다. 이 값은 이 프로필의 이 직업각인에 저장됩니다." };
        chip.SetResourceReference(Border.BorderBrushProperty, "AccentTextFillColorPrimaryBrush");
        return chip;
    }

    /// <summary>The chip at the top of <paramref name="look"/>'s card when its page is where <see cref="ProfileBadge.Home"/> says
    /// (a rail page, not the inspector); nothing for looks the 내 오버레이 tab's chip covers.</summary>
    UIElement CardBadge(LookGroup look)
    {
        var (area, section) = ProfileBadge.Home(look);
        if (ProfileBadge.Of(area, section) != BadgeAt.Section) return new Border();
        var chip = ProfileBadgeChip(profileOnly: look == LookGroup.버프막대);
        // 스킬창: the boxes' 색과 효과 are the class's (Settings.Classes), not in any profile; one note says so (plan, phase 2).
        UIElement child = look == LookGroup.스킬칸 ? UiBuild.Stack(chip, UiBuild.Note(SkillColorsNote)) : chip;
        return new Border { Child = child, Margin = new(0, 2, 0, 4), Tag = CardBadgeTag };
    }

    /// <summary>The chips under <paramref name="element"/> shown (on a rail page) or hidden (the panel sits in the inspector,
    /// whose tab chip covers it).</summary>
    static void ShowCardBadges(DependencyObject element, bool on)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (child is Border { Tag: CardBadgeTag } host) host.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            else ShowCardBadges(child, on);
        }
    }

    void RefreshBadges()
    {
        foreach (var (t, profileOnly) in _badgeTexts) t.Text = BadgeText(profileOnly);
    }
}
