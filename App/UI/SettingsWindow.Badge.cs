using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The "프로필 N · 클래스" chip (user, 2026-11-13; <see cref="ProfileBadge"/>): one chip style, shown where
/// <see cref="ProfileBadge.Of"/> says, refreshed whenever 설정 is rebuilt (a profile or class change). The class is the played
/// 직업각인 (<see cref="ClassProfiles.LookKey"/>), also when 편집 중 shows another.</summary>
public partial class SettingsWindow
{
    const string CardBadgeTag = "ProfileBadge";
    readonly List<TextBlock> _badgeTexts = [];

    /// <summary>The badge text for the picked profile and the character played (null class → 기본).</summary>
    string BadgeText() => ProfileBadge.Text(_s.ActiveProfile, ClassProfiles.LookKey(_s));

    /// <summary>A new chip; it keeps itself current through <see cref="RefreshBadges"/>.</summary>
    UIElement ProfileBadgeChip()
    {
        var text = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Text = BadgeText() };
        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        _badgeTexts.Add(text);
        var chip = new Border { CornerRadius = new(10), BorderThickness = new(1), Padding = new(10, 2, 10, 3), Child = text, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "지금 편집하는 프로필과 플레이 중인 직업각인입니다. 이 값은 이 프로필의 이 직업각인에 저장됩니다." };
        chip.SetResourceReference(Border.BorderBrushProperty, "AccentTextFillColorPrimaryBrush");
        return chip;
    }

    /// <summary>The chip at the top of <paramref name="look"/>'s card when its page is where <see cref="ProfileBadge.Home"/> says
    /// (a rail page, not the inspector); nothing for looks the 내 오버레이 tab's chip covers.</summary>
    UIElement CardBadge(LookGroup look)
    {
        var (area, section) = ProfileBadge.Home(look);
        if (ProfileBadge.Of(area, section) != BadgeAt.Section) return new Border();
        var chip = ProfileBadgeChip();
        var host = new Border { Child = chip, Margin = new(0, 2, 0, 4), Tag = CardBadgeTag };
        return host;
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
        string text = BadgeText();
        foreach (var t in _badgeTexts) t.Text = text;
    }
}
