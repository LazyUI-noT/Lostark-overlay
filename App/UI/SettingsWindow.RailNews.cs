using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;
using NewsKind = GaugeOverlay.Core.Domain.RailNews.NewsKind;

namespace GaugeOverlay.App.UI;

/// <summary>설정's rail orbs (settings-reorg.plan.md, order 14): a dot beside a tab with something new under it, green for what an
/// update brought, yellow for what the app found while playing (user, 2026-11-10). Opening the tab marks the new things on the
/// page (rows: a dot at their left; the rest: a line under the title) until the tab is left. What counts is in Core
/// (<see cref="RailNews"/>, <see cref="ClassSupport.New"/>); this file gathers the keys from the window's data and draws.</summary>
public partial class SettingsWindow
{
    static readonly Color PatchColor = Color.FromRgb(0x3D, 0xDC, 0x84), AppColor = Color.FromRgb(0xFF, 0xC8, 0x00);

    static Color ColorOf(NewsKind kind) => kind == NewsKind.Patch ? PatchColor : AppColor;

    readonly Dictionary<(string Tab, NewsKind Kind), Ellipse> _railOrbs = [];

    /// <summary>A class or 직업각인 shipped since 직업 인식 was last shown (<see cref="ClassSupport.New"/> over Settings.SeenSupport).</summary>
    bool _classNews;

    /// <summary>The tab whose news is marked on the page, and what was new there when it was opened; cleared on leaving it.</summary>
    string? _newsTab;
    readonly HashSet<string> _newsKeys = [];
    readonly List<(AdornerLayer Layer, Adorner Mark)> _newsMarks = [];
    UIElement? _newsLines;

    /// <summary>A small dot with a glow in the kind's colour, hidden until its tab has that kind of news.</summary>
    static Ellipse RailOrb(NewsKind kind) => new()
    {
        Width = 8, Height = 8, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Fill = new SolidColorBrush(ColorOf(kind)), Visibility = Visibility.Collapsed,
        Effect = new DropShadowEffect { Color = ColorOf(kind), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
        ToolTip = kind == NewsKind.Patch ? "업데이트로 새로 생긴 것이 있습니다" : "앱이 새로 읽은 것이 있습니다",
    };

    /// <summary>The tab's two dots (update, app), registered for <see cref="RefreshRailOrbs"/>.</summary>
    UIElement[] RailOrbsFor(string tab) =>
        [.. new[] { NewsKind.Patch, NewsKind.App }.Select(kind => _railOrbs[(tab, kind)] = RailOrb(kind))];

    /// <summary>The shipped raids.json's fingerprint, read once: the file only changes with an update, so not on every refresh.</summary>
    readonly Lazy<string> _raidFingerprint = new(() => RaidData.Fingerprint(CharacterService.DefaultProfilesDir));

    /// <summary>What each tab holds now, for <see cref="RailNews"/>: its setting rows (their ids begin with the tab's name), the
    /// shipped raid data and the learned or pending HP values under 레이드, the characters and their places under 레이드 숙제.</summary>
    Dictionary<string, List<string>> RailKeys()
    {
        var now = new Dictionary<string, List<string>>();
        void Add(string tab, IEnumerable<string> keys)
        {
            if (!now.TryGetValue(tab, out var list)) now[tab] = list = [];
            list.AddRange(keys);
        }
        foreach (var key in RailNews.RowKeys(_registry.All.Select(row => row.Id))) Add(key.Split('/')[0], [key]);
        Add(AreaRaid, [_raidFingerprint.Value]);
        foreach (var (gateId, gate) in _s.Raids.Gates)
        {
            Add(AreaRaid, Difficulties(gate.LearnedHp).Select(d => $"hp:{gateId}/{d}"));
            Add(AreaRaid, Difficulties(gate.PendingHp).Select(d => $"hp:{gateId}/{d}"));
        }
        Add(AreaHomework, _s.Homework.Characters.Select(c => $"char:{c.Name}"));
        Add(AreaHomework, _s.Homework.Order.Select((name, place) => $"place:{place}:{name}"));
        return now;
    }

    static IEnumerable<string> Difficulties<T>(IReadOnlyDictionary<string, T>? hp) => hp is null ? [] : hp.Keys;

    /// <summary>Whether a class or 직업각인 shipped since 직업 인식 was last shown (its rows the NEW marks show). Read when the window
    /// opens, not on every refresh: the list is only read from the shipped data and changes when 직업 인식 is shown.</summary>
    void FindClassNews() => _classNews = ClassSupport.New(_chars.Support(slot => slot.ToString()), _s.SeenSupport).Count > 0;

    /// <summary>Lights each tab that has something new (settings-reorg order 14). The first call records what is there as seen, so
    /// a first install lights nothing; App.Wiring saves that record, since <see cref="SettingsChanged"/> has no subscriber while the
    /// window is built. The tab on screen is seen at once, so a change made while it shows never lights it; what was new there is
    /// kept for the page's marks first, and the record is saved (<see cref="SettingsChanged"/>).</summary>
    void RefreshRailOrbs()
    {
        if (_newsTab != _area.Name) ForgetNews();
        var now = RailKeys();
        _s.RailSeen ??= RailNews.Baseline(now);
        var lit = RailNews.LitKinds(now, _s.RailSeen).ToHashSet();
        if (_classNews) lit.Add((AreaCharacter, NewsKind.Patch));
        if (IsVisible && lit.Any(l => l.Tab == _area.Name))
        {
            _newsTab = _area.Name;
            _newsKeys.UnionWith(RailNews.Unseen(now, _s.RailSeen, _area.Name));
            _s.RailSeen = RailNews.Opened(now, _s.RailSeen, _area.Name);
            if (_area.Name == AreaCharacter) _classNews = false;   // MarkSupportSeen recorded them while the tab shows
            lit.RemoveWhere(l => l.Tab == _area.Name);
            SettingsChanged?.Invoke();
        }
        foreach (var (key, orb) in _railOrbs) orb.Visibility = Shown(lit.Contains(key));
        ShowNews();
    }

    /// <summary>Marks what was new on the tab on screen: a dot left of each new row, a line under the title for the rest. Redrawn
    /// after every refresh, since BuildAll and a section switch put new rows on screen.</summary>
    void ShowNews()
    {
        ClearNewsMarks();
        if (_newsTab != _area.Name || _newsKeys.Count == 0) return;
        Dispatcher.BeginInvoke(() =>   // once laid out: a row gets its adorner layer when it is on screen
        {
            if (_newsTab != _area.Name) return;
            foreach (var row in _registry.ElementsOf(_newsKeys))
                if (AdornerLayer.GetAdornerLayer(row) is { } layer)
                {
                    var mark = new NewsDot(row, PatchColor);
                    layer.Add(mark);
                    _newsMarks.Add((layer, mark));
                }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        var lines = RailNews.Describe(_newsKeys);
        if (lines.Count == 0 || SectionPage.Visibility != Visibility.Visible) return;
        var panel = new StackPanel { Margin = new(0, 6, 0, 0) };
        foreach (var (kind, line) in lines)
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new(0, 2, 0, 0),
                Children = { new Ellipse { Width = 8, Height = 8, Margin = new(0, 0, 8, 0), Fill = new SolidColorBrush(ColorOf(kind)), VerticalAlignment = VerticalAlignment.Center }, Note(line) },
            });
        Header.Children.Add(_newsLines = panel);
    }

    void ClearNewsMarks()
    {
        foreach (var (layer, mark) in _newsMarks) layer.Remove(mark);
        _newsMarks.Clear();
        if (_newsLines is not null) Header.Children.Remove(_newsLines);
        _newsLines = null;
    }

    /// <summary>The tab was left (or 설정 closed): its marks go.</summary>
    void ForgetNews()
    {
        _newsTab = null;
        _newsKeys.Clear();
        ClearNewsMarks();
    }

    /// <summary>A rail-style dot just left of a new row, in the row's own left margin so it covers nothing.</summary>
    sealed class NewsDot(UIElement row, Color color) : Adorner(row)
    {
        const double Radius = 3.5, Gap = 8, MaxCenterY = 18;   // inside the card's 16 px padding, glow included

        readonly Brush _fill = Frozen(new SolidColorBrush(color));
        readonly Brush _glow = Frozen(new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)));

        static Brush Frozen(Brush b) { b.Freeze(); return b; }

        protected override void OnRender(DrawingContext dc)
        {
            var at = new Point(-Gap, Math.Min(AdornedElement.RenderSize.Height / 2, MaxCenterY));
            dc.DrawEllipse(_glow, null, at, Radius * 1.8, Radius * 1.8);
            dc.DrawEllipse(_fill, null, at, Radius, Radius);
        }

        protected override HitTestResult? HitTestCore(PointHitTestParameters hit) => null;   // clicks go to the row
    }
}
