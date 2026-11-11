using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>A look group's 모든 배치에 적용 mark (display-split.plan.md, step 4; status/decisions.md): at the top of its card,
/// a checkbox and a line saying whether 배치 1–5 share it or which 배치 it is, and which settings it covers.</summary>
public partial class SettingsWindow
{
    const string ShareMarkHelp = "켜면 배치 1–5가 같은 값을 씁니다. 다른 배치에 따로 둔 값 대신 지금 배치의 값을 씁니다. "
        + "끄면 배치마다 따로 저장되고, 모든 배치가 지금 값에서 시작합니다.";

    /// <summary>The mark for <paramref name="look"/>; <paramref name="refill"/> redraws its page so the line follows the box.</summary>
    UIElement ShareMark(LookGroup look, Action refill)
    {
        bool shared = _s.SharedLooks?.Contains(look) == true;
        var line = new TextBlock { Text = LookShare.Status(look, shared, _s.ActiveProfile), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new(26, 0, 0, 8) };
        var check = Check("모든 배치에 적용", shared, v => _s.ShareLook(look, v), () => { Changed(); Dispatcher.BeginInvoke(refill); }, ShareMarkHelp);
        return Stack(check, line);
    }
}
