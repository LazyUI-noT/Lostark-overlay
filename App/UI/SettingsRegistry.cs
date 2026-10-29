using System.Windows;
using System.Windows.Media.Animation;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Every setting row 설정 built last (<see cref="SettingsWindow"/>'s BuildAll): what search finds, what 즐겨찾기
/// lists, and how to get to each. Rebuilt with the sections, so its elements are the ones on screen.</summary>
sealed class SettingsRegistry
{
    sealed record Row(SettingEntry Entry, UIElement Element, Func<UIElement>? Build, Action Navigate);

    const double FlashSeconds = 1;

    readonly List<Row> _rows = [];
    readonly Dictionary<string, Row> _byId = [];

    public IEnumerable<SettingEntry> All => _rows.Select(r => r.Entry);

    public void Clear()
    {
        _rows.Clear();
        _byId.Clear();
    }

    /// <summary>A row. A second row with the same id (two "색" in one section) gets "#2", "#3".</summary>
    public SettingEntry Add(SettingEntry e, UIElement element, Func<UIElement>? build, Action navigate)
    {
        string id = e.Id;
        for (int n = 2; _byId.ContainsKey(id); n++) id = $"{e.Id}#{n}";
        var row = new Row(e with { Id = id }, element, build, navigate);
        _rows.Add(row);
        _byId[id] = row;
        return row.Entry;
    }

    public SettingEntry? Find(string id) => _byId.GetValueOrDefault(id)?.Entry;

    /// <summary>A working copy of the row (a check box or slider), for 즐겨찾기; null for rows built only in place.</summary>
    public UIElement? Build(string id) => _byId.GetValueOrDefault(id)?.Build?.Invoke();

    /// <summary>Goes to the row's page (selecting its box on the canvas), scrolls it into view and flashes it.</summary>
    public void Navigate(string id)
    {
        if (_byId.GetValueOrDefault(id) is not { } row) return;
        row.Navigate();
        if (row.Element is not FrameworkElement f) return;
        f.Dispatcher.BeginInvoke(() =>
        {
            f.BringIntoView();
            f.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(FlashSeconds)) { FillBehavior = FillBehavior.Stop });
            if (f.Focusable) f.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
