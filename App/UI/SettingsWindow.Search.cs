using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 검색 (Ctrl+F, top of the rail), 즐겨찾기 (★ rows in one place) and the ★ on rows (right-click, or the ☆ in
/// a search result). The rows come from <see cref="SettingsRegistry"/>, filled while BuildAll builds the sections: each
/// section panel has a place (area › section, and the canvas box that shows it).</summary>
public partial class SettingsWindow
{
    const string AreaFavorites = "즐겨찾기";
    const int MaxResults = 12;

    readonly record struct Place(string Area, string Section, CanvasBox? Box);

    readonly SettingsRegistry _registry = new();
    readonly CardStack _secFavorites = new();
    readonly TextBox _search = new() { VerticalContentAlignment = VerticalAlignment.Center };
    readonly ListBox _results = new() { Visibility = Visibility.Collapsed, MaxHeight = 380, Margin = new(0, 0, 0, 8) };
    bool _collecting;   // BuildAll is filling the registry

    /// <summary>Where a section panel's rows are found; null: rows not listed (복사, the box options of one box).</summary>
    Place? PlaceOf(Panel p) =>
        p == _secCharacter ? new(AreaCharacter, AreaCharacter, null)
        : p == _secFit ? new(AreaFit, SecFit, null)
        : p == _secDetect ? new(AreaFit, SecDetect, null)
        : p == _secDisplay ? new(AreaOverlay, "배경판", null)
        : p == _secPlace ? new(AreaOverlay, "배치", null)
        : p == GaugeBox ? new(AreaGauge, AreaGauge, null)
        : p == _secApart ? new(AreaSkills, "창 구성", null)
        : p == _secBuffs ? new(AreaOverlay, "버프 막대", new("BuffBar", FreeLayout.FirstBuff, null))
        : p == _secOther ? new(AreaOverlay, "단심", new("Judgment", null, null))
        : p == _secAmsu ? new(AreaOverlay, "암흑수류탄", new("Amsu", null, null))
        : p == _secRaidCount ? new(AreaOverlay, "줄 수 창", RaidCountBox)
        : p == _skillsClass || p == _secSkillLook ? new(AreaOverlay, "스킬 칸", new("Skills", Part.일반1, null))
        : p == _secConnect ? new(AreaParty, "연결", null)
        : p == _secMembers ? new(AreaParty, SecMembers, null)
        : p == _secOrder ? new(AreaParty, "순서", null)
        : p == _secSend ? new(AreaParty, "보내기·받기", null)
        : p == _secRaid ? new(AreaRaid, "레이드", null)
        : p == _secMeter ? new(AreaMeter, AreaMeter, null)
        : p == _secShow ? new(AreaShow, AreaShow, null)
        : p == _secScreens ? new(AreaScreens, AreaScreens, null)
        : p == _secKeys ? new(AreaKeys, "게임 키", null)
        : p == _secHotkeys ? new(AreaKeys, "앱 단축키", null)
        : p == _secPerformance ? new(AreaPerformance, AreaPerformance, null)
        : p == _secApi ? new(AreaApi, AreaApi, null)
        : p == _secWarnings ? new(AreaWarnings, AreaWarnings, null)
        : null;

    /// <summary>The rows built from now on belong to <paramref name="p"/> (null: to nothing). Only while BuildAll collects.</summary>
    void Collect(Panel? p)
    {
        if (!_collecting || p is null || PlaceOf(p) is not { } place) { Report = null; return; }
        Report = (label, help, element, build) =>
        {
            var entry = _registry.Add(new SettingEntry($"{place.Area}/{place.Section}/{label}", label, $"{place.Area} › {place.Section}", help, place.Box?.Kind),
                element, build, () => GoTo(place));
            StarMenu(element, entry.Id);
        };
    }

    void StartCollecting()
    {
        _registry.Clear();
        _collecting = true;
    }

    void StopCollecting()
    {
        _collecting = false;
        Report = null;
    }

    void GoTo(Place place)
    {
        if (place.Area != AreaOverlay || _canvas is null) { Go(place.Area, place.Section); return; }
        Go(AreaOverlay);
        _canvas.Select(place.Box);   // the inspector follows (Selected)
    }

    // ── ★ ─────────────────────────────────────────────────────────────────────────

    bool IsFavorite(string id) => _s.FavoriteSettings.Contains(id);

    void ToggleFavorite(string id)
    {
        if (!_s.FavoriteSettings.Remove(id)) _s.FavoriteSettings.Add(id);
        Changed();
        if (_area.Name == AreaFavorites) FillFavorites();
    }

    /// <summary>Right-click on a row: ★ 즐겨찾기에 추가 / 즐겨찾기에서 빼기.</summary>
    void StarMenu(UIElement element, string id)
    {
        if (element is not FrameworkElement f || f.ContextMenu is not null) return;
        var item = new MenuItem();
        item.Click += (_, _) => ToggleFavorite(id);
        var menu = new ContextMenu { Items = { item } };
        menu.Opened += (_, _) => item.Header = IsFavorite(id) ? "즐겨찾기에서 빼기" : "★ 즐겨찾기에 추가";
        f.ContextMenu = menu;
    }

    // ── 설정 검색 ───────────────────────────────────────────────────────────────────

    /// <summary>The search box and its results, at the top of the rail.</summary>
    void BuildSearch()
    {
        var hint = Muted(new TextBlock { Text = "설정 검색   Ctrl+F", IsHitTestVisible = false, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        System.Windows.Automation.AutomationProperties.SetName(_search, "설정 검색");
        _search.TextChanged += (_, _) => { hint.Visibility = Shown(_search.Text.Length == 0); ShowResults(); };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _search.Clear(); e.Handled = true; }
            else if (e.Key == Key.Enter && FirstResult() is { } id) { _registry.Navigate(id); e.Handled = true; }
            else if (e.Key == Key.Down && _results.Items.Count > 0)
            {
                _results.SelectedIndex = 0;
                (_results.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
        };
        _results.MouseLeftButtonUp += (_, _) => OpenResult();
        _results.KeyDown += (_, e) => { if (e.Key == Key.Enter) { OpenResult(); e.Handled = true; } };
        Nav.Children.Insert(0, _results);
        Nav.Children.Insert(0, new Grid { Margin = new(0, 0, 0, 10), Children = { _search, hint } });
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control || KeyFieldCapturing()) return;
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
        };
    }

    string? FirstResult() => _results.Items.OfType<ListBoxItem>().Select(i => i.Tag as string).FirstOrDefault(id => id is not null);

    void OpenResult()
    {
        if (_results.SelectedItem is ListBoxItem { Tag: string id }) _registry.Navigate(id);
    }

    void ShowResults()
    {
        _results.Items.Clear();
        bool asked = _search.Text.Trim().Length > 0;
        _results.Visibility = Shown(asked);
        if (!asked) return;
        var found = SettingsSearch.Find(_registry.All, _search.Text, MaxResults);
        foreach (var e in found) _results.Items.Add(ResultItem(e));
        if (found.Count == 0) _results.Items.Add(new ListBoxItem { Content = "찾은 설정이 없습니다", IsEnabled = false });
    }

    /// <summary>"label" over "영역 › 섹션", with ☆ to star it.</summary>
    ListBoxItem ResultItem(SettingEntry e)
    {
        var star = new Button { Content = IsFavorite(e.Id) ? "★" : "☆", MinWidth = 0, Padding = new(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "즐겨찾기" };
        star.Click += (_, _) => { ToggleFavorite(e.Id); star.Content = IsFavorite(e.Id) ? "★" : "☆"; };   // the click is the button's: the result doesn't open
        var text = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = e.Label, TextWrapping = TextWrapping.Wrap },
                Muted(new TextBlock { Text = e.Path, FontSize = 12, TextWrapping = TextWrapping.Wrap }),
            },
        };
        var row = new DockPanel();
        DockPanel.SetDock(star, Dock.Right);
        row.Children.Add(star);
        row.Children.Add(text);
        return new ListBoxItem { Content = row, Tag = e.Id };
    }

    // ── 즐겨찾기 ────────────────────────────────────────────────────────────────────

    /// <summary>The starred rows, working here (a check box or slider is built again), each with a link to its place;
    /// a star whose setting no longer exists is skipped.</summary>
    void FillFavorites()
    {
        BuildAll();   // the registry's rows and values as they are now
        var p = _secFavorites;
        p.Children.Clear();
        foreach (var id in _s.FavoriteSettings.ToList())
        {
            if (_registry.Find(id) is not { } e) continue;
            var row = _registry.Build(id) ?? new TextBlock { Text = e.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var go = new Button { Content = $"→ {e.Path}", Margin = new(0, 4, 8, 0) };
            go.Click += (_, _) => _registry.Navigate(id);
            var unstar = new Button { Content = "★ 빼기", Margin = new(0, 4, 0, 0), ToolTip = "즐겨찾기에서 뺍니다." };
            unstar.Click += (_, _) => ToggleFavorite(id);
            p.Children.Add(Stack(row, Wrap(go, unstar)));
        }
        if (p.Children.Count == 0) p.Children.Add(Note("아직 즐겨찾기가 없습니다. 설정 행을 오른쪽 클릭하거나, 검색 결과의 ☆를 누르세요."));
    }
}
