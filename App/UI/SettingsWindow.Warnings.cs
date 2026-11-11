using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 경고 (user, 2026-11-10): the warnings asked before an action, each on until 다시 보지 않기 or this page turns it
/// off (<see cref="Settings.MutedWarnings"/>).</summary>
public partial class SettingsWindow
{
    void FillWarnings()
    {
        var p = Refill(_secWarnings, AreaWarnings, "되돌리기 어려운 작업 전에 묻는 경고입니다. 경고 창에서 '다시 보지 않기'를 고르면 여기서 꺼집니다.");
        foreach (var (kind, name, help) in Warnings.All)
            p.Children.Add(Row("", Check(name, !_s.MutedWarnings.Contains(kind), on =>
            {
                if (on) _s.MutedWarnings.Remove(kind);
                else _s.MutedWarnings.Add(kind);
            }, Changed, help)));
        // Windows 알림 (user, 2026-11-11): the tray's notices by kind, all on until turned off here (Tray.NoticeShown).
        p.Children.Add(SubHeading("Windows 알림", "화면 오른쪽 아래에 뜨는 알림을 종류별로 켜고 끕니다. 꺼도 설정 창에 나오는 결과와 파티 상태는 그대로 보입니다."));
        foreach (var (kind, name, help) in Notices.All)
            p.Children.Add(Row("", Check(name, Notices.Shows(_s, kind), on =>
            {
                if (on) _s.MutedNotices.Remove(kind);
                else _s.MutedNotices.Add(kind);
            }, Changed, help)));
    }

    /// <summary>Asks before <paramref name="kind"/>'s action unless muted; true: go ahead. 다시 보지 않기 with 확인 mutes it.</summary>
    bool Warn(WarningKind kind, string message)
    {
        if (_s.MutedWarnings.Contains(kind)) return true;
        var mute = new CheckBox { Content = "다시 보지 않기", Margin = new(0, 14, 0, 0) };
        var ok = new Button { Content = "확인", IsDefault = true, MinWidth = 80, Margin = new(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 80 };
        var dialog = new Window
        {
            Owner = this, Title = Warnings.All.First(w => w.Kind == kind).Name, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new(20),
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
                    mute,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0), Children = { ok, cancel } },
                },
            },
        };
        Themes.Apply(dialog, _s.Theme);
        dialog.SetResourceReference(BackgroundProperty, Themes.BackgroundBrushKey);
        ok.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() != true) return false;
        if (mute.IsChecked == true)
        {
            _s.MutedWarnings.Add(kind);
            Changed();
            FillWarnings();
        }
        return true;
    }
}
