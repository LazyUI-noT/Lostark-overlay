using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 표시: whether the overlays show (오버레이 표시, 스킬창이 안 보이면 숨기기), every window's show switch (the same
/// as tray → 표시, <see cref="TrayToggles"/>), and a scene of the game screen with every window at its spot, playing a sample
/// fight and stepping through each window's looks. The scene draws a copy of the settings: nothing it shows is saved. A
/// window's looks are chosen on its own page; a click in the scene goes there.</summary>
public partial class SettingsWindow
{
    const string AreaShow = "표시";
    const double SceneHeight = 420, SwitchesWidth = 250;
    /// <summary>Every second one window in three steps to its next look: each window every 3 s, not all at once.</summary>
    const int LookEvery = 3;

    readonly CardStack _secShow = new();
    readonly Border _sceneHost = new() { Height = SceneHeight, CornerRadius = new(8), ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F)) };
    readonly DispatcherTimer _lookTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<OverlayPart, int> _look = [];
    OverlayCanvas? _scene;
    Settings? _sceneSettings;
    OverlayPart? _hovered;   // its look holds
    int _lookTick;
    bool _showHooked, _sceneStale = true;

    /// <summary>A window's display options: how a copy of the settings takes option <c>i</c>, how many, and the one in use.</summary>
    sealed record Looks(int Count, Action<Settings, int> Apply, Func<Settings, int> Current);

    void FillShow()
    {
        if (!_showHooked)
        {
            _showHooked = true;
            _lookTimer.Tick += (_, _) => NextLooks();
            _secShow.IsVisibleChanged += (_, _) => SceneShown(_secShow.IsVisible);
        }
        var o = _s.Overlay;
        var p = Refill(_secShow, AreaShow, "오버레이 창을 켜고 끕니다 (트레이 메뉴의 표시와 같은 스위치). 그림은 게임 화면 위의 모습이고, 창마다 표시 방식을 " +
            "돌아가며 보여 줍니다. 마우스를 올리면 멈추고, 누르면 그 창의 설정으로 갑니다. 꺼진 창은 흐린 점선으로 보입니다.");
        p.Children.Add(HelpRow("오버레이 표시", "끄기: 내 오버레이와 파티원 창을 모두 숨깁니다. 자동: 게임이나 설정 창이 앞에 있을 때 아래 조건에 맞으면 보여 줍니다. 항상 표시: 어떤 창이 앞에 있어도 보여 줍니다. 트레이 메뉴와 '오버레이 켜기/끄기' 단축키는 끄기와 마지막으로 켠 방식을 오갑니다.",
            EnumCombo([(OverlayShow.끄기, "끄기"), (OverlayShow.자동, "자동"), (OverlayShow.항상, "항상 표시")], o.Mode(), v => { o.SetMode(v); Dispatcher.BeginInvoke(BuildAll); })));
        if (o.Mode() == OverlayShow.자동)
        {
            p.Children.Add(HelpRow("자동일 때", "스킬창이 보일 때: 컷신이나 로딩으로 스킬창이 안 보이면 숨깁니다. 던전 안에서: 게임의 전투 분석기나 보스 체력이 보이면 던전으로 봅니다. 전투 분석기를 게임에서 열어 두어야 하고, 이 항목을 고를 때 미터기가 꺼져 있으면 표시 안 함으로 켜집니다. DPS 미터기 창은 이 설정을 따르지 않습니다.", AutoWhenCombo(o)));
            if (o.Auto() == OverlayAuto.던전) p.Children.Add(Note(_s.Meter.On ? "게임에서 전투 분석기를 열어 두세요. 안 보이면 오버레이도 나오지 않습니다." : "미터기가 꺼져 있어 던전을 알 수 없습니다. 게임이 앞에 있으면 보여 줍니다."));
        }

        AddMap(p);
        p.Children.Add(Heading("창 미리보기"));

        var switches = new StackPanel { Margin = new(14, 0, 0, 0) };
        foreach (var (part, name) in Tray.PartNames) switches.Children.Add(ShowSwitch(part, name));
        Detach(_sceneHost);
        var grid = new Grid { Margin = new(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwitchesWidth) });
        grid.Children.Add(_sceneHost);
        Grid.SetColumn(switches, 1);
        grid.Children.Add(switches);
        CardStack.SetBreak(grid, true);
        p.Children.Add(grid);

        _sceneStale = true;   // the settings changed: a new copy when the page shows
        if (_secShow.IsVisible) SceneShown(true);
    }

    /// <summary>The boxes of the map opened by hand, kept while 설정 is open (they start folded).</summary>
    readonly HashSet<string> _mapOpen = [];
    string? _mapPin, _mapNote;
    FeatureMapView? _map;
    IReadOnlyDictionary<string, Detection> _detect = DetectBoard.Empty;
    TimeSpan _detectAt;

    /// <summary>감지 중 from the engine's latest snapshot: kept for the next map, and passed on while the 표시 page shows.</summary>
    public void SetDetect(Snapshot mine)
    {
        (_detect, _detectAt) = (mine.Detect, mine.At);
        if (_secShow.IsVisible) _map?.SetDetect(mine.Detect, mine.At);
        DetectAreasChanged(mine);
    }

    /// <summary>--demo --map-open=all|id,id --map-pin=id --map-fail=id,id: the map with those boxes open, one box's lines held and those readers failing, for screenshots.</summary>
    internal void DemoMap(string? open, string? pin, string? fail = null)
    {
        if (fail is not null) _detect = fail.Split(',').ToDictionary(id => id, _ => new Detection(DetectMark.Failed, TimeSpan.Zero));
        if (open == "all") _mapOpen.UnionWith(FeatureMap.Boxes.Select(b => b.Id));
        else if (open is not null) _mapOpen.UnionWith(open.Split(','));
        _mapPin = pin;
        FillShow();
    }

    /// <summary>The 표시 map (FeatureMapView): 모두 접기 / 모두 펼치기 above it, a note line under it for what a switch also did.</summary>
    void AddMap(Panel p)
    {
        var note = new TextBlock { Opacity = 0.8, Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        var map = _map = new FeatureMapView(_s, _mapOpen, _mapPin, () => { Changed(); Dispatcher.BeginInvoke(FillShow); }, area => Go(area),
            text => _mapNote = text, pin => _mapPin = pin) { Value = GameValue, Body = GameBody, ShowResources = _checkOn, ShowDetect = id => { _detectPicked = id; Go(AreaFit, SecDetect); DrawDetect(); } };
        map.SetDetect(_detect, _detectAt);
        note.Text = _mapNote ?? "";
        _mapNote = null;
        var fold = new Button { Content = "모두 접기", Padding = new(10, 3, 10, 3), Margin = new(0, 0, 6, 0) };
        var open = new Button { Content = "모두 펼치기", Padding = new(10, 3, 10, 3) };
        fold.Click += (_, _) => map.SetAll(false);
        open.Click += (_, _) => map.SetAll(true);
        p.Children.Add(HelpRow("기능 맵", "왼쪽 게임 설정에서 오른쪽 기능으로, 무엇이 무엇을 쓰는지 보여 줍니다. 칸을 누르면 펼쳐지거나 접히고, 펼친 칸에서 켜고 끕니다. "
            + "⚙는 그 설정 화면으로 갑니다. 점 색: 초록 켜짐, 파랑 표시 안 함(읽기는 함), 회색 꺼짐. 금색 테는 캐릭터마다 따로 정하는 것입니다. 분홍 점선은 파티와 주고받는 것, 빨간 점선은 끊긴 것입니다. 진한 주황 빛이 게임 설정 쪽으로 흐르면 그 기능이 읽지 못하는 중이니 게임 설정을 확인하세요.",
            new StackPanel { Orientation = Orientation.Horizontal, Children = { fold, open, CheckButton() } }));
        var host = new ScrollViewer { Content = map, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new(0, 8, 0, 0) };
        host.PreviewMouseWheel += (_, e) =>   // the page scrolls, not this sideways-only viewer, which would swallow the wheel
        {
            if (host.Parent is not UIElement parent) return;
            e.Handled = true;
            parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = host });
        };
        CardStack.SetBreak(host, true);
        p.Children.Add(host);
        p.Children.Add(note);
        Detach(_checkTable);
        CardStack.SetBreak(_checkTable, true);
        p.Children.Add(_checkTable);
        ShowCheck();
    }

    /// <summary>표시 화면 changed: the windows move.</summary>
    public event Action? DisplayChosen;

    readonly TextBlock _behindLine = new() { Margin = new(0, 2, 0, 6) };

    BehindStatus? _behindStatus;

    /// <summary>창 밖에서도 읽기's state from the engine, on each change.</summary>
    public void SetBehindStatus(BehindStatus status)
    {
        _behindStatus = status;
        ShowBehindLine();
    }

    /// <summary>Shown only while the switch is on.</summary>
    void ShowBehindLine()
    {
        _behindLine.Text = _s.Screens.ReadBehind && _behindStatus is { } status ? $"지금: {status.Text}" : "";
        _behindLine.Visibility = _behindLine.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    readonly CardStack _secScreens = new();

    void FillScreens()
    {
        var p = Refill(_secScreens, AreaScreens, "게임과 디스코드 같은 다른 앱을 서로 다른 모니터에 두고 쓸 때의 설정입니다.");
        p.Children.Add(HelpRow("표시 화면", "오버레이를 띄울 모니터입니다. 바꾸면 오버레이 창들이 그 모니터의 같은 자리쯤으로 옮겨 갑니다. 그 모니터를 빼면 게임이 있는 모니터로 돌아옵니다.", DisplayCombo()));
        p.Children.Add(Check("다른 앱을 쓰는 중에도 읽기", _s.Screens.ReadBehind, v => { _s.Screens.ReadBehind = v; ShowBehindLine(); }, Changed,
            "켜면 디스코드나 브라우저를 눌러 쓰고 있어도 게임을 계속 읽고, 오버레이도 그대로 보여 줍니다. 끄면 게임을 다시 누를 때까지 멈춥니다. 게임 화면이 모니터에 보이고 있어야 읽을 수 있어서, 게임을 최소화하거나 다른 창이 게임을 덮으면 그동안은 멈춥니다. 게임이 전체 화면 모드면 다른 앱을 누르는 순간 게임이 내려가니, 전체 창 모드로 쓰세요."));
        Detach(_behindLine);
        p.Children.Add(_behindLine);
        ShowBehindLine();
    }

    /// <summary>스킬창이 보일 때 and 던전 안에서; the old 켜기 (게임앞) only while it's still the one in use.</summary>
    ComboBox AutoWhenCombo(OverlaySettings o)
    {
        var items = new List<ComboBoxItem> { new() { Content = "스킬창이 보일 때", Tag = OverlayAuto.스킬창 }, new() { Content = "던전 안에서", Tag = OverlayAuto.던전 } };
        if (o.Auto() == OverlayAuto.게임앞) items.Insert(0, new() { Content = "게임이 앞에 있을 때 (지금 설정)", Tag = OverlayAuto.게임앞 });
        var c = Combo();
        c.ItemsSource = items;
        c.SelectedItem = items.First(i => (OverlayAuto)i.Tag == o.Auto());
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is not ComboBoxItem { Tag: OverlayAuto v } || v == o.Auto()) return;
            _s.ChooseOverlayAuto(v);
            Changed();
            Dispatcher.BeginInvoke(BuildAll);
        };
        return c;
    }

    ComboBox DisplayCombo()
    {
        var items = new List<ComboBoxItem> { new() { Content = "지금 위치 그대로", Tag = "" }, new() { Content = "게임이 있는 화면", Tag = DisplayMap.GameDisplay } };
        int n = 0;
        foreach (var (device, width, height, primary) in Displays.Connected())
            items.Add(new() { Content = $"화면 {++n} ({width} × {height}){(primary ? " 주 화면" : "")}", Tag = device });
        var c = Combo();
        c.ItemsSource = items;
        c.SelectedItem = items.FirstOrDefault(i => (string)i.Tag == _s.Screens.Display) ?? items[0];
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is not ComboBoxItem { Tag: string device } || device == _s.Screens.Display) return;
            _s.Screens.Display = device;
            Changed();
            DisplayChosen?.Invoke();
        };
        return c;
    }

    UIElement ShowSwitch(OverlayPart part, string name)
    {
        // Named after the window, so search and ★ tell the eight switches apart.
        var check = Check($"{name} 표시", TrayToggles.IsShown(_s, part), v => TrayToggles.Show(_s, part, v), () => { Changed(); Dispatcher.BeginInvoke(BuildAll); }, ShowHelp(part));
        if (part == OverlayPart.Judgment && _s.ActiveCharacter is null) check.IsEnabled = false;
        var cell = new StackPanel { Margin = new(0, 0, 0, 6) };
        cell.Children.Add(check);
        if (ShowNote(part) is { } note) cell.Children.Add(new TextBlock { Text = note, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(24, 0, 0, 0) });
        return cell;
    }

    /// <summary>The page shown: a fresh scene if the settings changed, playing; hidden: everything stops.</summary>
    void SceneShown(bool shown)
    {
        CheckRun();   // 리소스 검사 measures only while this page shows
        if (!shown)
        {
            _scene?.Play.Stop();
            _lookTimer.Stop();
            _hovered = null;   // the mouse left with the page
            return;
        }
        if (_sceneStale || _scene is null) BuildScene();
        else _scene.Play.Start();
        if (SystemParameters.ClientAreaAnimation) _lookTimer.Start();   // Windows' animations off: the looks hold
    }

    void BuildScene()
    {
        _sceneStale = false;
        _scene?.Play.Stop();
        var copy = _sceneSettings = SceneSettings();
        var scene = _scene = new OverlayCanvas(copy, _chars, scene: true) { IsOn = part => TrayToggles.IsShown(_s, part) };
        scene.Play.Auto = () => PreviewPlayer.Sections(copy, _chars);
        scene.ScenePicked += part => GoTo(PlaceOfPart(part));
        scene.SceneHover += part => _hovered = part;
        System.Windows.Automation.AutomationProperties.SetName(scene, "오버레이 창 미리보기");
        _hovered = null;
        // Each window keeps its place in its cycle across a rebuild (a switch clicked): no jump back to the first look.
        foreach (var (part, _) in Tray.PartNames)
            if (LooksOf(part) is { } looks)
            {
                if (_look.TryGetValue(part, out int at)) looks.Apply(copy, at);
                else _look[part] = looks.Current(copy);
            }
        scene.Loaded += (_, _) =>
        {
            if (SavedShot() is var (picture, area)) scene.ShowPicture(picture, area);   // else the canvas's dark backdrop
            scene.Relayout();
            if (_secShow.IsVisible) scene.Play.Start();   // Loaded comes again on every re-parent (BuildAll), seen or not
        };
        _sceneHost.Child = scene;
    }

    /// <summary>A copy of the settings with the windows that need a switch to draw at all turned on (an off window is then drawn
    /// faint over it). The skill boxes, gauge and buff bars keep their own state: hidden ones are drawn dim already.</summary>
    Settings SceneSettings()
    {
        var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(_s, SettingsStore.Json), SettingsStore.Json)!;
        foreach (var part in new[] { OverlayPart.Debuffs, OverlayPart.Judgment, OverlayPart.Amsu, OverlayPart.Meter, OverlayPart.BarCount, OverlayPart.Party })
            TrayToggles.Show(copy, part, true);
        copy.Raids.Gate(RaidSettings.MordumGate).CountWindow = true;   // a 줄 수 창 to draw
        return copy;
    }

    /// <summary>One window in <see cref="LookEvery"/> steps to its next look (not the one under the mouse).</summary>
    void NextLooks()
    {
        if (_scene is null || _sceneSettings is null) return;
        _lookTick++;
        int i = 0;
        bool changed = false;
        foreach (var (part, _) in Tray.PartNames)
        {
            if (LooksOf(part) is not { } looks) continue;
            if ((_lookTick + i++) % LookEvery != 0 || part == _hovered) continue;
            int next = (_look.GetValueOrDefault(part) + 1) % looks.Count;
            _look[part] = next;
            looks.Apply(_sceneSettings, next);
            changed = true;
        }
        if (changed) _scene.Relayout();
    }

    /// <summary>The window's display options the scene steps through (null: one look).</summary>
    static Looks? LooksOf(OverlayPart part) => part switch
    {
        OverlayPart.Skills => new(3, (s, i) => s.Overlay.CooldownStyle = (CooldownStyle)i, s => (int)s.Overlay.CooldownStyle),
        OverlayPart.Buffs => new(4, (s, i) => (s.Buffs.Side, s.Buffs.Drain) = (i % 2 == 1, i < 2), s => (s.Buffs.Side ? 1 : 0) + (s.Buffs.Drain ? 0 : 2)),
        OverlayPart.Judgment => new(2, (s, i) => s.Judgment.Display = (JudgmentDisplay)i, s => (int)s.Judgment.Display),
        OverlayPart.Amsu => new(4, (s, i) => s.Amsu.Look = (AmsuLook)i, s => (int)s.Amsu.Look),
        OverlayPart.Meter => new(5, (s, i) =>
        {
            s.Meter.Look = (MeterLook)i;
            if (s.Meter.Look == MeterLook.파티비교) s.Meter.Scope = MeterScope.공대;   // shown only for the 공대
        }, s => (int)s.Meter.Look),
        OverlayPart.Party => new(3, (s, i) => s.Party.Label = (PeerLabel)i, s => (int)s.Party.Label),
        _ => null,
    };

    static string? ShowHelp(OverlayPart part) => part switch
    {
        OverlayPart.Skills => "지금 캐릭터의 스킬 칸 전부입니다. 끄기 전에 꺼 둔 칸은 다시 켤 때 그대로 꺼져 있습니다.",
        OverlayPart.Gauge => "지금 캐릭터의 아덴 게이지입니다.",
        OverlayPart.Buffs => "공증, 아덴 피증, 초각스 피증, 뎀증 막대 전부입니다. 끄기 전에 꺼 둔 막대는 다시 켤 때 그대로입니다.",
        OverlayPart.Debuffs => "3막 모르둠의 디버프 창을 직접 켭니다. 레이드 안에서는 늘 보입니다. 끄면 레이드의 디버프 창 자동 설정대로 보스를 알아본 동안만 보입니다.",
        OverlayPart.Judgment => "지금 캐릭터의 단심 창입니다.",
        OverlayPart.Amsu => "보스에 걸린 암흑수류탄의 남은 시간입니다. 켜면 모든 보스에서 읽습니다.",
        OverlayPart.Meter => "끄면 창만 숨깁니다. 전투 기록에 쓰려고 계속 읽습니다. 읽기까지 끄려면 DPS 미터기에서 끄세요.",
        OverlayPart.BarCount => "모든 레이드의 줄 수 읽기입니다. 관문마다 켠 줄 수 창 설정은 남습니다. 레이드 화면 맨 위 스위치와 같습니다.",
        OverlayPart.Party => "파티원 창 전부입니다. 파티 연결은 그대로입니다.",
        _ => null,
    };

    string? ShowNote(OverlayPart part) => part switch
    {
        OverlayPart.BarCount => $"관문마다 켠 줄 수 창: {_s.Raids.Gates.Values.Count(g => g.CountWindow)}개",
        OverlayPart.Judgment when _s.ActiveCharacter is null => "캐릭터를 고르면 켤 수 있습니다.",
        _ => null,
    };

    static Place PlaceOfPart(OverlayPart part) => part switch
    {
        OverlayPart.Skills => new(AreaOverlay, "스킬 칸", new("Skills", Part.일반1, null)),
        OverlayPart.Gauge => new(AreaOverlay, "아덴 게이지", new("Gauge", Part.게이지, null)),
        OverlayPart.Buffs => new(AreaOverlay, "버프 막대", new("BuffBar", FreeLayout.FirstBuff, null)),
        OverlayPart.Debuffs => new(AreaOverlay, "디버프", new("Debuff", null, 0)),
        OverlayPart.Judgment => new(AreaOverlay, "단심", new("Judgment", null, null)),
        OverlayPart.Amsu => new(AreaOverlay, "암흑수류탄", new("Amsu", null, null)),
        OverlayPart.Meter => new(AreaMeter, AreaMeter, null),
        OverlayPart.BarCount => new(AreaRaid, "레이드", null),
        _ => new(AreaParty, SecMembers, null),
    };
}
