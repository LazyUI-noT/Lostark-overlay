using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정's rail orbs (settings-reorg.plan.md, order 14): a yellow dot beside a tab with something new under it. What
/// counts is in Core (<see cref="RailNews"/>, <see cref="ClassSupport.New"/>); this file gathers the keys from the window's data
/// and draws the dots.</summary>
public partial class SettingsWindow
{
    readonly Dictionary<string, Ellipse> _railOrbs = [];

    /// <summary>A class or 직업각인 shipped since 직업 인식 was last shown (<see cref="ClassSupport.New"/> over Settings.SeenSupport).</summary>
    bool _classNews;

    /// <summary>A small yellow dot with a glow, hidden until its tab has something new.</summary>
    static Ellipse RailOrb() => new()
    {
        Width = 8, Height = 8, Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x00)), Visibility = Visibility.Collapsed,
        Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD8, 0x3A), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 },
        ToolTip = "새로 추가되거나 바뀐 것이 있습니다",
    };

    /// <summary>The shipped raids.json's fingerprint, read once: the file only changes with an update, so not on every refresh.</summary>
    readonly Lazy<string> _raidFingerprint = new(() => RaidData.Fingerprint(CharacterService.DefaultProfilesDir));

    /// <summary>What each tab holds now, for <see cref="RailNews"/>: its setting rows (their ids begin with the tab's name), the
    /// shipped raid data and the learned or pending HP values under 레이드, the characters and their places under 원정대 (레이드 선택).</summary>
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
    /// window is built. The tab on screen is seen at once, so a change made while it shows never lights it; a tab that was lit
    /// and is seen is saved (<see cref="SettingsChanged"/>).</summary>
    void RefreshRailOrbs()
    {
        var now = RailKeys();
        _s.RailSeen ??= RailNews.Baseline(now);
        var lit = RailNews.Lit(now, _s.RailSeen).ToHashSet();
        if (_classNews) lit.Add(AreaCharacter);
        if (IsVisible && lit.Contains(_area.Name))
        {
            _s.RailSeen = RailNews.Opened(now, _s.RailSeen, _area.Name);
            if (_area.Name == AreaCharacter) _classNews = false;   // MarkSupportSeen recorded them while the tab shows
            lit.Remove(_area.Name);
            SettingsChanged?.Invoke();
        }
        foreach (var (tab, orb) in _railOrbs) orb.Visibility = Shown(lit.Contains(tab));
    }
}
