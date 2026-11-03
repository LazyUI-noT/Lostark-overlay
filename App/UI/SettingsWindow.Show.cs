using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
        var p = Refill(_secShow, AreaShow, "오버레이 창을 켜고 끕니다 (트레이 메뉴의 표시와 같은 스위치). 그림은 게임 화면 위의 모습으로, 창마다 표시 방식을 " +
            "차례로 보여 줍니다. 마우스를 올리면 멈추고, 누르면 그 창의 설정으로 갑니다. 꺼진 창은 흐리게 점선으로 보입니다.");
        p.Children.Add(HelpRow("오버레이 표시", "끄기: 내 오버레이와 파티원 창을 모두 숨깁니다. 켜기: 게임이나 설정 창이 앞에 있을 때 보여 줍니다 (게임이 꺼져 있어도). " +
            "항상: 어느 창이 앞에 있어도 보여 줍니다. 트레이 메뉴나 '오버레이 켜기/끄기' 단축키는 끄기와 마지막 켠 방식 사이를 오갑니다.",
            EnumCombo([(OverlayShow.끄기, "끄기"), (OverlayShow.켜기, "켜기 (게임·설정이 앞에 있을 때)"), (OverlayShow.항상, "항상")], o.Mode(), o.SetMode)));
        p.Children.Add(Check("스킬창이 안 보이면 숨기기", o.HideWithoutHud, v => o.HideWithoutHud = v, Changed,
            "게임이 앞에 있을 때, 컷신·로딩처럼 스킬창이 안 보이는 동안 오버레이를 숨깁니다. 끄면 마지막 모습 그대로 둡니다. DPS 미터기는 따르지 않습니다."));

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
        OverlayPart.Amsu => new(3, (s, i) => s.Amsu.Look = (AmsuLook)i, s => (int)s.Amsu.Look),
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
        OverlayPart.Debuffs => "3막 모르둠의 디버프 창을 직접 켭니다: 레이드 안에서는 늘 보입니다. 끄면 레이드의 디버프 창 자동만 따릅니다 (보스를 알아본 동안).",
        OverlayPart.Judgment => "지금 캐릭터의 단심 창입니다.",
        OverlayPart.Amsu => "보스에 걸린 암흑수류탄의 남은 시간입니다. 켜면 모든 보스에서 읽습니다.",
        OverlayPart.Meter => "끄면 창만 숨깁니다. 전투 기록을 위해 계속 읽습니다. 읽기까지 끄려면 DPS 미터기에서 끕니다.",
        OverlayPart.BarCount => "모든 레이드의 줄 수 읽기입니다. 관문마다 켠 줄 수 창은 그대로 남습니다. 레이드의 맨 위 스위치와 같습니다.",
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
