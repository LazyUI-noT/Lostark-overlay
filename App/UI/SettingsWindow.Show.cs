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

/// <summary>설정 → 기능 Map: whether the overlays show (오버레이 표시, 스킬창이 안 보이면 숨기기) and the 표시 map, whose boxes hold every
/// window's show switch (the same as tray → 표시, <see cref="TrayToggles"/>) and, on hover, each window's styles playing
/// (<see cref="MapPreview"/>). The scene of every window over the game screen (창 미리보기) is gone (user, 2026-11-10).</summary>
public partial class SettingsWindow
{
    const string AreaShow = "기능 맵";

    readonly CardStack _secShow = new();
    bool _showHooked;

    void FillShow()
    {
        if (!_showHooked)
        {
            _showHooked = true;
            _secShow.IsVisibleChanged += (_, _) => CheckRun();   // 리소스 검사 measures only while this page shows
        }
        var o = _s.Overlay;
        var p = Refill(_secShow, AreaShow, "오버레이 창을 켜고 끕니다 (트레이 메뉴의 기능 맵과 같은 스위치). 상자에 마우스를 올리면 그 창의 표시 방식을 " +
            "나란히 움직여 보여 줍니다.");
        p.Children.Add(HelpRow("오버레이 표시", "끄기: 내 오버레이와 파티원 창을 모두 숨깁니다. 자동: 게임이나 설정 창이 앞에 있을 때 아래 조건에 맞으면 보여 줍니다. 항상 표시: 어떤 창이 앞에 있어도 보여 줍니다. 트레이 메뉴와 '오버레이 켜기/끄기' 단축키는 끄기와 마지막으로 켠 방식을 오갑니다.",
            EnumCombo([(OverlayShow.끄기, "끄기"), (OverlayShow.자동, "자동"), (OverlayShow.항상, "항상 표시")], o.Mode(), v => { o.SetMode(v); Dispatcher.BeginInvoke(BuildAll); })));
        if (o.Mode() == OverlayShow.자동)
        {
            p.Children.Add(HelpRow("자동일 때", "스킬창이 보일 때: 컷신이나 로딩으로 스킬창이 안 보이면 숨깁니다. 전투 중일 때: 게임의 전투 분석기나 보스 체력바가 보이면 전투 중으로 봅니다. 게임에서 전투 분석기를 열어 두세요. 이 항목을 고를 때 미터기가 꺼져 있으면 미터기를 '표시 안 함'으로 켭니다. DPS 미터기 창은 이 설정을 따르지 않습니다.", AutoWhenCombo(o)));
            if (o.Auto() == OverlayAuto.던전) p.Children.Add(Note(_s.Meter.On ? "게임에서 전투 분석기를 열어 두세요. 안 보이면 오버레이도 나오지 않습니다." : "미터기가 꺼져 있어 전투 중인지 알 수 없습니다. 게임이 앞에 있으면 보여 줍니다."));
        }

        AddMap(p);
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

    /// <summary>--demo --map-open=all|id,id --map-pin=id --map-fail=id,id --map-seen=id,id --map-preview=id --map-scroll=px --map-line=id --map-cut=id: the map with those boxes
    /// open, one box's lines held, those readers failing or ✓ and one box's hover card under it (a popup PrintWindow can't see), scrolled, one box's line to 파티 hovered and one cut, for screenshots.</summary>
    internal void DemoMap(string? open, string? pin, string? fail = null, string? seen = null, string? preview = null, string? scroll = null, string? line = null, string? cut = null)
    {
        var marks = new Dictionary<string, Detection>();
        foreach (var id in fail?.Split(',') ?? []) marks[id] = new Detection(DetectMark.Failed, TimeSpan.Zero, Why: "데모: 읽어야 하는데 못 읽었습니다.");
        foreach (var id in seen?.Split(',') ?? []) marks[id] = new Detection(DetectMark.Concrete, TimeSpan.Zero);
        if (marks.Count > 0) _detect = marks;
        if (preview is not null)
        {
            var bgra = new byte[320 * 80 * 4];
            for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (0x60, (byte)(i / 4 % 320 * 255 / 320), 0x30, 255);
            _detectPictures = new Dictionary<string, DetectPicture> { [preview] = new DetectPicture(new Core.Domain.Image(320, 80, bgra), default, TimeSpan.Zero) };
            _mapDemoCard = preview;
        }
        if (open == "all") _mapOpen.UnionWith(FeatureMap.Boxes.Select(b => b.Id));
        else if (open is not null) _mapOpen.UnionWith(open.Split(','));
        _mapPin = pin;
        if (cut is not null) FeatureMap.SetCut(_s, FeatureMap.Edges.First(e => e.From == cut && FeatureMap.Cuttable(e)), true);
        FillShow();
        if (scroll is null && line is null) return;
        _ = Dispatcher.InvokeAsync(async () =>   // once the page is up: --tab goes there after this
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            Body.ScrollToVerticalOffset(double.Parse(scroll ?? "0"));
            if (line is not null) _map?.DemoLine(line);
        });
    }

    string? _mapDemoCard;

    /// <summary>A map box's hover card: its window's GIF (보스 체력 · 대난투 체력, drawn over the game's own bar: theirs recorded
    /// over footage, user 2026-11-10), its reader's capture from the moment its mark turned.</summary>
    UIElement? MapCard(string id) => MapPreview.Card(FeatureMap.Boxes.First(b => b.Id == id).Title,
        id switch { "boss" => "Boss", "shadow" => "Shadow", _ => FeatureMap.WindowOf(id)?.ToString() }, _detect.GetValueOrDefault(id),
        _detectPictures.TryGetValue(id, out var picture) ? BitmapOf(picture) : null, _detectAt);

    /// <summary>The 감지 중 chip's card: the stored moment until Live's first frame, then each Live frame (DetectAreasChanged).</summary>
    System.Windows.Controls.Image? _mapLivePicture;

    UIElement? MapLiveCard(string id)
    {
        _mapLivePicture = new System.Windows.Controls.Image { Source = _detectPictures.TryGetValue(id, out var kept) ? BitmapOf(kept) : null };
        return MapPreview.LiveCard(FeatureMap.Boxes.First(b => b.Id == id).Title, _detect.GetValueOrDefault(id), _mapLivePicture, _detectAt);
    }

    void MapLive(string? id)
    {
        if (id is null) _mapLivePicture = null;
        SetLive(id);
    }

    /// <summary>The 표시 map (FeatureMapView): 모두 접기 / 모두 펼치기 above it, a note line under it for what a switch also did.</summary>
    void AddMap(Panel p)
    {
        var note = Muted(new TextBlock { Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var map = _map = new FeatureMapView(_s, _mapOpen, _mapPin, () => { Changed(); Dispatcher.BeginInvoke(FillShow); }, area => Go(area),
            text => _mapNote = text, pin => _mapPin = pin) { Value = GameValue, Body = GameBody, Preview = MapCard, DetectPreview = MapLiveCard, DetectHover = MapLive, ShowResources = _checkOn, ShowDetect = id => { _detectPicked = id; Go(AreaFit, SecDetect); DrawDetect(); } };
        map.SetDetect(_detect, _detectAt);
        note.Text = _mapNote ?? "";
        _mapNote = null;
        var fold = new Button { Content = "모두 접기", Padding = new(10, 3, 10, 3), Margin = new(0, 0, 6, 0) };
        var open = new Button { Content = "모두 펼치기", Padding = new(10, 3, 10, 3) };
        fold.Click += (_, _) => map.SetAll(false);
        open.Click += (_, _) => map.SetAll(true);
        p.Children.Add(HelpRow("기능 맵", "왼쪽 게임 설정에서 오른쪽 기능으로, 무엇이 무엇을 쓰는지 보여 줍니다. 칸을 누르면 펼쳐지거나 접히고, 펼친 칸에서 켜고 끕니다. "
            + "⚙는 그 설정 화면으로 갑니다. 점: 초록 켜짐, 파랑 표시 안 함(읽기는 함), 회색 꺼짐. 금색 테: 캐릭터마다 따로 정함. 분홍 점선은 파티와 주고받는 것, 회색 점선은 끊긴 것이고, 파티 옆 선을 누르면 끊거나 잇습니다. 칸이나 선에 마우스를 올리면 이어진 곳과 창의 모습이 보입니다. 주황 빛이 게임 설정 쪽으로 흐르면 그 기능이 못 읽는 중이니 게임 설정을 확인하세요.",
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
        if (_mapDemoCard is { } demo && MapCard(demo) is { } card) p.Children.Add(card);
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
        p.Children.Add(Check("Alt+Tab 중에도 항상 표시", _s.Screens.ReadBehind, v => { _s.Screens.ReadBehind = v; ShowBehindLine(); }, Changed,
            "켜면 디스코드나 브라우저를 쓰는 동안에도 게임을 읽고 오버레이를 보여 줍니다. 끄면 게임을 다시 누를 때까지 멈춥니다. 켜 두어도 게임이 최소화되거나 다른 창에 가려지면 멈춥니다. 전체 화면 모드에서는 다른 앱을 누르면 게임이 내려가니 전체 창 모드로 쓰세요."));
        Detach(_behindLine);
        p.Children.Add(_behindLine);
        ShowBehindLine();
    }

    /// <summary>스킬창이 보일 때 and 던전 안에서; the old 켜기 (게임앞) only while it's still the one in use.</summary>
    ComboBox AutoWhenCombo(OverlaySettings o)
    {
        var items = new List<ComboBoxItem> { new() { Content = "스킬창이 보일 때", Tag = OverlayAuto.스킬창 }, new() { Content = "전투 중일 때", Tag = OverlayAuto.던전 } };
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
        if (ShowNote(part) is { } note) cell.Children.Add(Muted(new TextBlock { Text = note, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(24, 0, 0, 0) }));
        return cell;
    }

    /// <summary>A copy of the settings for a 설정 page's 미리보기, with the windows that need a switch to draw at all turned on (an
    /// off window is then drawn faint over it). The skill boxes, gauge and buff bars keep their own state.</summary>
    Settings SceneSettings()
    {
        var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(_s, SettingsStore.Json), SettingsStore.Json)!;
        foreach (var part in new[] { OverlayPart.Debuffs, OverlayPart.Judgment, OverlayPart.Amsu, OverlayPart.Meter, OverlayPart.BarCount, OverlayPart.Party })
            TrayToggles.Show(copy, part, true);
        // 자동 buff bars: the renderer draws them as in a raid in every preview (OverlayRenderer.ShowHidden).
        return copy;
    }

    static string? ShowHelp(OverlayPart part) => part switch
    {
        OverlayPart.Skills => "지금 캐릭터의 스킬 칸 전부입니다. 끄기 전에 꺼 둔 칸은 다시 켤 때 그대로 꺼져 있습니다.",
        OverlayPart.Gauge => "지금 캐릭터의 아덴 게이지입니다.",
        OverlayPart.Buffs => "공증, 아덴 피증, 초각스 피증, 초각성기 뎀증 막대 전부입니다. 끄기 전에 꺼 둔 막대는 다시 켤 때 그대로입니다.",
        OverlayPart.Debuffs => "3막 모르둠의 디버프 창을 직접 켭니다. 레이드 안에서는 늘 보입니다. 끄면 레이드의 디버프 창 자동 설정대로 보스를 알아본 동안만 보입니다.",
        OverlayPart.Judgment => "지금 캐릭터의 단심 창입니다.",
        OverlayPart.Amsu => "보스에 걸린 암흑수류탄의 남은 시간입니다. 켜면 모든 보스에서 읽습니다.",
        OverlayPart.Meter => "끄면 창만 숨깁니다. 전투 기록에 쓰려고 계속 읽습니다. 읽기까지 끄려면 DPS 미터기에서 끄세요.",
        OverlayPart.BarCount => "모든 레이드의 줄 수 읽기입니다. 관문마다 켠 줄 수 창 설정은 남습니다. 내 오버레이 → 줄 수의 스위치와 같습니다.",
        OverlayPart.Party => "파티원 창 전부입니다. 공유 연결은 그대로입니다.",
        _ => null,
    };

    string? ShowNote(OverlayPart part) => part switch
    {
        OverlayPart.BarCount => _s.Raids.CountEnabled ? "모든 관문에서 켜짐" : "줄 수 읽기 꺼짐",
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
        OverlayPart.BarCount => new(AreaOverlay, "줄 수 창", RaidCountBox),
        _ => new(AreaParty, SecMembers, null),
    };
}
