using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>내 오버레이: the game screen with your overlay and the party's on it (<see cref="OverlayCanvas"/>, editing the
/// live settings: a drag moves the windows in game at once), its tools above, and on the right the inspector: the rows of
/// what was clicked, under its name, with whom they apply to at the bottom.</summary>
public partial class SettingsWindow
{
    OverlayCanvas? _canvas;
    CanvasBox? _inspected;
    readonly StackPanel _contextNotes = new();   // what 편집 중's pick means: over the inspector's rows

    /// <summary>A drag or nudge on the canvas moved or sized a window: App puts the overlay windows at their new spots.</summary>
    public event Action? LayoutChanged;

    void BuildCanvasPage()
    {
        var canvas = _canvas = new OverlayCanvas(_s, _chars);
        canvas.Play.Auto = () => PreviewPlayer.Sections(_s, _chars, PreviewTarget);   // ▶ plays a fight on its own; a click still uses a box
        canvas.Selected += ShowInspector;
        canvas.GestureStarted += RecordStep;
        canvas.Changed += () => { ResetBaseline(); SettingsChanged?.Invoke(); LayoutChanged?.Invoke(); };
        canvas.Note += text => { if (text is not null) _canvasNote.Text = text; else ShowCanvasNote(); };
        CanvasHost.Child = canvas;
        InspectorHost.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        InspectorHost.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        InspectorFoot.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        CanvasTools.Children.Add(ShotButtons());
        CanvasTools.Children.Add(RunButtons(canvas, out _run));
        CanvasTools.Children.Add(ToolCheck("자석", canvas.Snap, v => canvas.Snap = v, "끌 때 다른 칸에 맞춰 붙습니다. Shift를 누르고 끌면 붙지 않습니다."));
        CanvasTools.Children.Add(ToolCheck("크기 연동", canvas.LinkSizes, v => canvas.LinkSizes = v, "모서리로 크기를 바꿀 때 같은 줄의 칸도 같이 바뀝니다."));
        CanvasTools.Children.Add(ToolCheck("나", canvas.ShowMine, v => canvas.ShowMine = v, null));
        CanvasTools.Children.Add(ToolCheck("파티원", canvas.ShowParty, v => canvas.ShowParty = v, null));
        var actual = new Button { Content = "1:1 크기", Margin = new(0, 2, 8, 2), ToolTip = "게임 화면과 같은 크기(100%)로 봅니다." };
        actual.Click += (_, _) => canvas.ActualSize();
        CanvasTools.Children.Add(actual);
        CanvasTools.Children.Add(_canvasNote);
        IsVisibleChanged += (_, _) => { if (!IsVisible) StopPlay(); };
        Loaded += (_, _) => { ShowCanvasShot(); canvas.Relayout(); };   // the picture at the window's real DPI
        ShowInspector(null);
    }

    readonly TextBlock _canvasNote = new() { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new(8, 0, 0, 0) };

    void ShowCanvasNote() => _canvasNote.Text = "칸을 누르면 오른쪽에 설정이 나옵니다 · 끌어서 옮기기, 모서리로 크기 · 화살표 키로 1px (Shift 10px) · 휠로 확대";

    static CheckBox ToolCheck(string text, bool value, Action<bool> set, string? help)
    {
        var c = new CheckBox { Content = text, IsChecked = value, MinWidth = 0, Margin = new(0, 2, 14, 2), ToolTip = help, VerticalAlignment = VerticalAlignment.Center };
        c.Click += (_, _) => set(c.IsChecked == true);
        return c;
    }

    /// <summary>▶ 실행해 보기 and 초기화: while it runs, a click uses the box (a cooling one: 1 s left) instead of moving it.</summary>
    ToggleButton? _run;

    /// <summary>▶ off (it stops the player): the window hid, another area opened, the 레이아웃 편집기 opened.</summary>
    void StopPlay()
    {
        if (_run is not null) _run.IsChecked = false;
    }

    static StackPanel RunButtons(OverlayCanvas canvas, out ToggleButton toggle)
    {
        var run = toggle = new ToggleButton { Content = "▶ 시뮬레이션", Padding = new(10, 3, 10, 3), Margin = new(0, 2, 6, 2) };
        var reset = new Button { Content = "초기화", Padding = new(10, 3, 10, 3), Margin = new(0, 2, 16, 2), IsEnabled = false };
        run.Checked += (_, _) => { canvas.Play.Start(); reset.IsEnabled = true; run.Content = "■ 멈추기"; };
        run.Unchecked += (_, _) => { canvas.Play.Stop(); reset.IsEnabled = false; run.Content = "▶ 시뮬레이션"; };
        reset.Click += (_, _) => canvas.Play.Reset();
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { run, reset } };
    }

    /// <summary>The inspector for <paramref name="box"/> (null: nothing picked, the overlay as a whole).</summary>
    void ShowInspector(CanvasBox? box)
    {
        _inspected = box;
        var (title, about, panels) = InspectorFor(box);
        InspectorHead.Children.Clear();
        InspectorHead.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        InspectorHead.Children.Add(new TextBlock { Text = about, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) });
        foreach (var p in panels) Detach(p);
        Detach(_contextNotes);
        Inspector.Children.Clear();
        if (ClassScoped(box)) Inspector.Children.Add(_contextNotes);
        foreach (var p in panels) Inspector.Children.Add(p);
        InspectorBody.ScrollToTop();
        var scope = new TextBlock
        {
            Text = box?.Kind switch
            {
                "Skills" => $"색과 효과: {ClassName(ShownClass)}에만 · 칸 모양: 모든 직업각인",
                "Plate" => $"창 모양: {ClassName(ShownClass)}에만 · 전체: 모든 직업각인",
                _ when ClassScoped(box) => $"{ClassName(ShownClass)}에만 적용",
                _ => "모든 직업각인에 적용",
            },
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var foot = new DockPanel();
        if (FooterReset(box) is { } reset)
        {
            reset.Margin = new(12, 0, 0, 0);
            DockPanel.SetDock(reset, Dock.Right);
            foot.Children.Add(reset);
        }
        foot.Children.Add(scope);
        InspectorFoot.Child = foot;
    }

    /// <summary>The panel's 기본값 (two clicks: the button asks once), the same reset its section had at the bottom.</summary>
    Button? FooterReset(CanvasBox? box) => box?.Kind switch
    {
        null or "Plate" or "Skills" => ResetButton("기본값", "전체와 칸 모양(불투명도, 남은 쿨, 쿨 진행, 초각스, 금테)을 되돌립니다. 위치, 크기, 판 색은 남습니다.", () => SettingsResets.Display(_s)),
        "BuffBar" => ResetButton("기본값", "버프 막대 설정을 되돌립니다. 분리 창의 위치와 크기는 남습니다.", () => SettingsResets.Buffs(_s)),
        "Debuff" => ResetButton("기본값", "디버프 칸 크기를 되돌립니다. 고른 레이드와 창의 위치와 크기는 남습니다.", () => SettingsResets.Debuffs(_s)),
        "Gauge" => ResetButton("기본값", "게이지의 색·두께·읽기 방식과 모든 클래스·직업각인의 막대 모양(구분선, 색, 발광)을 되돌립니다. 클래스의 게이지 규칙과 배운 게이지 값은 남습니다.", () => SettingsResets.Gauge(_s)),
        "Judgment" => ResetButton("기본값", "단심 표시 설정을 되돌립니다. 켠 직업각인, 데미지 폰트 크기, 창 위치와 크기는 남습니다.", () => SettingsResets.Judgment(_s)),
        _ => null,
    };

    /// <summary>Box options and class rows follow 편집 중; the debuff window, the party's window and the overlay as a whole do not.</summary>
    static bool ClassScoped(CanvasBox? box) => box?.Kind is "Skills" or "Gauge" or "BuffBar" or "Plate";

    (string Title, string About, UIElement[] Panels) InspectorFor(CanvasBox? box)
    {
        switch (box)
        {
            case { Kind: "Skills", Part: { } part }:
                ShowBox(part, plate: false);
                return (SlotWithKey(_s, (SlotId)part), "스킬 칸: 색과 효과는 이 칸, 크기와 보이기는 모든 칸에 적용됩니다.", [BoxTab, _secSkillLook, _skillsClass]);
            case { Kind: "Gauge" }:
                return ("아덴 게이지", "아덴 게이지의 모양, 색, 읽기 방식입니다.", [GaugeBox]);
            case { Kind: "BuffBar", Part: { } bar }:
                ShowBox(bar, plate: false);
                return ("버프 막대", "MP 바 위의 버프를 남은 시간 막대로 보여 줍니다.", [BoxTab, _secBuffs]);
            case { Kind: "Debuff", Debuff: { } d }:
                ShowDebuffBox(d);
                return ("디버프", "보스 디버프 칸입니다.", [BoxTab, _secBuffs]);
            case { Kind: "Raid" }:
                return ("레이드 줄 수 창", "보스 체력바의 줄 수를 보여 주고, 정한 줄 수에서 빛납니다. 설정은 왼쪽의 레이드에서 바꿉니다.", []);
            case { Kind: "Judgment" }:
                return ("단심", "단죄·심판 룬의 심판 시간입니다.", [_secOther]);
            case { Kind: "Amsu" }:
                return ("암흑수류탄", "보스에 걸린 암흑수류탄의 남은 시간입니다.", [_secAmsu]);
            case { Kind: "Party" }:
                return ("파티원 창", "파티원 칸의 모양입니다. 클래스마다 따로 저장됩니다.", [_secMembers]);
            case { Kind: "Plate" }:
                ShowBox(null, plate: true);
                return ("내 오버레이 창", "창 전체의 모양입니다.", [BoxTab, _secDisplay]);
            default:
                return ("전체", "오버레이 전체의 표시와 배치입니다. 칸을 누르면 그 칸의 설정이 나옵니다.", [_secDisplay, _secPlace]);
        }
    }

    /// <summary>다른 직업각인에 복사… (top bar): 복사 in the inspector, on 내 오버레이.</summary>
    void ShowCopy()
    {
        if (_canvas is null) return;
        _inspected = new("Copy", null, null);
        if (_area.Name != AreaOverlay) Go(AreaOverlay);   // ShowArea fills it
        else FillCopyInspector();
    }

    void FillCopyInspector()
    {
        InspectorHead.Children.Clear();
        InspectorHead.Children.Add(new TextBlock { Text = "다른 직업각인에 복사", FontSize = 18, FontWeight = FontWeights.SemiBold });
        Detach(_secCopy);
        Inspector.Children.Clear();
        Inspector.Children.Add(_secCopy);
        InspectorFoot.Child = new TextBlock { Text = $"{ClassName(ShownClass)}의 설정을 복사합니다", Opacity = 0.75 };
    }

    /// <summary>The inspector again for what it shows, after its rows were rebuilt.</summary>
    void RefreshInspector()
    {
        if (_canvas is null || _area.Name != AreaOverlay) return;   // hidden: its panels may be on the page shown (파티원 모양)
        if (_inspected?.Kind == "Copy") FillCopyInspector();
        else ShowInspector(_inspected);
    }
}
