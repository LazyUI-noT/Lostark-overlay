using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 표시: every overlay window on one page, a card each with its show switch (the same one as tray → 표시,
/// <see cref="TrayToggles"/>), a live example drawn by the real renderer that cycles through the window's looks, and a way to
/// its own settings. The examples draw a copy of the settings, so cycling a look changes nothing saved.</summary>
public partial class SettingsWindow
{
    const string AreaShow = "표시";
    const double ExampleWidth = 220, ExampleHeight = 100, OffOpacity = 0.4;

    readonly CardStack _secShow = new();
    // The examples (about 20 renderers on settings copies) are drawn only once the page is on screen, not on every Reload.
    readonly List<(WrapPanel Into, OverlayPart Part, string Name)> _showPending = [];
    bool _showHooked;

    /// <summary>A window's display options: their names, how a copy of the settings takes option <c>i</c>, and the one in use.</summary>
    sealed record Looks(string[] Names, Action<Settings, int> Apply, Func<Settings, int> Current);

    void FillShow()
    {
        if (!_showHooked)
        {
            _showHooked = true;
            _secShow.IsVisibleChanged += (_, _) => { if (_secShow.IsVisible) DrawExamples(); };
        }
        var p = Refill(_secShow, AreaShow, "오버레이 창마다 켜고 끕니다. 트레이 메뉴의 표시와 같은 스위치입니다. 예시는 그 창의 표시 방식을 모두 나란히 보여 주고 " +
            "지금 쓰는 것에 테두리를 두릅니다. '설정 열기'는 그 창의 자세한 설정으로 갑니다.");
        p.Children.Add(Note("모두 '오버레이 표시'(내 오버레이 → 전체)를 따릅니다. 끄기면 여기서 켠 창도 보이지 않습니다."));
        _showPending.Clear();
        foreach (var (part, name) in Tray.PartNames) p.Children.Add(ShowCard(part, name));
        if (_secShow.IsVisible) DrawExamples();
    }

    void DrawExamples()
    {
        foreach (var (into, part, name) in _showPending)
        {
            // One example per display option, side by side, the one in use outlined; a window with one look: one example.
            if (LooksOf(part) is not { } looks) into.Children.Add(Example(part, $"{name} 예시", null, false));
            else
            {
                int current = looks.Current(_s);
                for (int i = 0; i < looks.Names.Length; i++)
                {
                    int at = i;
                    into.Children.Add(Example(part, looks.Names[i], s => looks.Apply(s, at), i == current));
                }
            }
        }
        _showPending.Clear();
    }

    UIElement ShowCard(OverlayPart part, string name)
    {
        bool on = TrayToggles.IsShown(_s, part);
        var examples = new WrapPanel { Opacity = on ? 1 : OffOpacity };
        _showPending.Add((examples, part, name));

        // Named after the window, so search and ★ tell the eight switches apart.
        var check = Check($"{name} 표시", on, v => TrayToggles.Show(_s, part, v), () => { Changed(); Dispatcher.BeginInvoke(BuildAll); }, ShowHelp(part));
        if (part == OverlayPart.Judgment && _s.ActiveCharacter is null) check.IsEnabled = false;
        var open = new Button { Content = "설정 열기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(16, 0, 0, 0) };
        open.Click += (_, _) => GoTo(PlaceOfPart(part));

        var head = new WrapPanel { Margin = new(0, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0) });
        head.Children.Add(check);
        head.Children.Add(open);

        var body = new StackPanel();
        body.Children.Add(head);
        if (ShowNote(part) is { } note) body.Children.Add(new TextBlock { Text = note, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 6) });
        body.Children.Add(examples);
        var card = new Border { Child = body, Padding = new(14), CornerRadius = new(8), BorderThickness = new(1), Margin = new(0, 0, 0, 8) };
        card.SetResourceReference(Border.BackgroundProperty, CardBrushKey);
        card.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        CardStack.SetBreak(card, true);   // its own card
        return card;
    }

    /// <summary>One example: the window drawn on a copy of the settings with <paramref name="look"/> applied, its name under it,
    /// outlined when it is the look in use.</summary>
    UIElement Example(OverlayPart part, string label, Action<Settings>? look, bool inUse)
    {
        var copy = ExampleSettings(part);
        look?.Invoke(copy);
        var frame = new Border
        {
            Width = ExampleWidth, Height = ExampleHeight, CornerRadius = new(6), ClipToBounds = true, BorderThickness = new(2),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F)), BorderBrush = inUse ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x3C)) : Brushes.Transparent,
            Child = new Viewbox { Child = ExampleRenderer(part, copy), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new(6) },
        };
        var cell = new StackPanel { Margin = new(0, 0, 10, 6) };
        cell.Children.Add(frame);
        cell.Children.Add(new TextBlock { Text = inUse ? $"{label} (사용 중)" : label, Opacity = inUse ? 1 : 0.7, Margin = new(2, 3, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(cell, inUse ? $"{label} 예시, 사용 중" : $"{label} 예시");
        return cell;
    }

    /// <summary>A copy of the settings with the part on (an example of a window that's off still shows it, dimmed).</summary>
    Settings ExampleSettings(OverlayPart part)
    {
        var copy = JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(_s, SettingsStore.Json), SettingsStore.Json)!;
        TrayToggles.Show(copy, part, true);
        if (part == OverlayPart.BarCount) copy.Raids.Gate(RaidSettings.MordumGate).CountWindow = true;
        if (part == OverlayPart.Buffs) copy.Buffs.Detached = true;   // the buff bars alone, as their own window draws them
        return copy;
    }

    OverlayRenderer ExampleRenderer(OverlayPart part, Settings copy)
    {
        var r = new OverlayRenderer
        {
            Settings = copy, Peer = part == OverlayPart.Party ? 0 : -1, ShowHidden = true, IsHitTestVisible = false,
            BuffsOnly = part == OverlayPart.Buffs, DebuffsOnly = part == OverlayPart.Debuffs, JudgmentOnly = part == OverlayPart.Judgment,
            MeterOnly = part == OverlayPart.Meter, RaidOnly = part == OverlayPart.BarCount,
        };
        var self = PreviewPlayer.SampleFor(_chars);
        var sample = part == OverlayPart.Party ? DemoSnapshots.Sample(self.Snapshot.At).Peers[0] : self;
        r.Update(sample, self.Snapshot.At);
        return r;
    }

    /// <summary>The window's display options shown side by side: their names, how a copy takes option <c>i</c>, and which one
    /// the settings use now (null: one look).</summary>
    static Looks? LooksOf(OverlayPart part) => part switch
    {
        OverlayPart.Skills => new(["회전", "채움", "채움 + 빗금"], (s, i) => s.Overlay.CooldownStyle = (CooldownStyle)i, s => (int)s.Overlay.CooldownStyle),
        OverlayPart.Buffs => new(["가로", "세로", "가로 · 줄지 않음", "세로 · 줄지 않음"], (s, i) => (s.Buffs.Side, s.Buffs.Drain) = (i % 2 == 1, i < 2),
            s => (s.Buffs.Side ? 1 : 0) + (s.Buffs.Drain ? 0 : 2)),
        OverlayPart.Judgment => new(["막대", "아이콘"], (s, i) => s.Judgment.Display = (JudgmentDisplay)i, s => (int)s.Judgment.Display),
        OverlayPart.Meter => new(["행", "세로 막대", "선 그래프", "한 줄 막대", "파티 비교"], (s, i) =>
        {
            s.Meter.Look = (MeterLook)i;
            if (s.Meter.Look == MeterLook.파티비교) s.Meter.Scope = MeterScope.공대;   // shown only for the 공대
        }, s => (int)s.Meter.Look),
        OverlayPart.Party => new(["아이콘 + 이름", "이름", "아이콘"], (s, i) => s.Party.Label = (PeerLabel)i, s => (int)s.Party.Label),
        _ => null,
    };

    static string? ShowHelp(OverlayPart part) => part switch
    {
        OverlayPart.Skills => "지금 캐릭터의 스킬 칸 전부입니다. 끄기 전에 꺼 둔 칸은 다시 켤 때 그대로 꺼져 있습니다.",
        OverlayPart.Gauge => "지금 캐릭터의 아덴 게이지입니다.",
        OverlayPart.Buffs => "공증, 아덴 피증, 초각스 피증, 뎀증 막대 전부입니다. 끄기 전에 꺼 둔 막대는 다시 켤 때 그대로입니다.",
        OverlayPart.Debuffs => "레이드의 디버프 창(3막 모르둠)입니다. 켜면 늘 보입니다. 관문마다는 레이드에서 정합니다.",
        OverlayPart.Judgment => "지금 캐릭터의 단심 창입니다.",
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
        OverlayPart.Meter => new(AreaMeter, AreaMeter, null),
        OverlayPart.BarCount => new(AreaRaid, "레이드", null),
        _ => new(AreaParty, SecMembers, null),
    };
}
