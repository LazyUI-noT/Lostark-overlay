using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
    CheckBox? _offCheck;   // 꺼진 창
    CanvasBox? _inspected;
    readonly StackPanel _contextNotes = new();   // what 편집 중's pick means: over the inspector's rows

    /// <summary>A drag or nudge on the canvas moved or sized a window: App puts the overlay windows at their new spots.</summary>
    public event Action? LayoutChanged;

    void BuildCanvasPage()
    {
        var canvas = _canvas = new OverlayCanvas(_s, _chars) { IsOn = CanvasWindowOn };   // 꺼진 창 are drawn faint
        canvas.Play.Auto = () => PreviewPlayer.Gauge(_s, _chars, PreviewTarget);   // ▶ plays a fight on its own; a click still uses a box
        canvas.Selected += ShowInspector;
        canvas.GestureStarted += RecordStep;
        canvas.Changed += () => { ResetBaseline(); SettingsChanged?.Invoke(); LayoutChanged?.Invoke(); };
        canvas.Note += text => { if (text is not null) _canvasNote.Text = text; else ShowCanvasNote(); };
        CanvasHost.Child = WithScreenBox(canvas);
        InspectorHost.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        InspectorHost.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        InspectorFoot.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        CanvasBadgeHost.Children.Add(ProfileBadgeChip());
        CanvasTools.Children.Add(ShotButtons());
        CanvasTools.Children.Add(RunButtons(canvas, out _run));
        var apartCheck = ToolCheck("칸 하나씩", false, v => { canvas.BoxesApart = v; ShowCanvasShot(); }, "켜면 스킬 칸을 하나씩 옮기고 크기를 바꿉니다. 끄면 칸을 끌어도 내 스킬창 전체가 움직입니다.");
        CanvasTools.Children.Add(apartCheck);
        CanvasTools.Children.Add(ToolCheck("자석", canvas.Snap, v => canvas.Snap = v, "끌 때 다른 칸에 맞춰 붙습니다. Shift를 누르고 끌면 붙지 않습니다."));
        CanvasTools.Children.Add(ToolCheck("크기 연동", canvas.LinkSizes, v => canvas.LinkSizes = v, "모서리로 크기를 바꿀 때 같은 줄의 칸도 같이 바뀝니다."));
        CanvasTools.Children.Add(ToolCheck("나", canvas.ShowMine, v => canvas.ShowMine = v, null));
        CanvasTools.Children.Add(ToolCheck("파티원", canvas.ShowParty, v => canvas.ShowParty = v, null));
        var offCheck = _offCheck = ToolCheck("꺼진 창", canvas.ShowOff, v => canvas.ShowOff = v, "설정에서 꺼 둔 창도 흐리게 보여 줍니다. 끄면 켜 둔 창만 보여서 자리를 잡기 쉽습니다.");
        CanvasTools.Children.Add(offCheck);
        var actual = new Button { Content = "1:1 크기", Margin = new(0, 2, 8, 2), ToolTip = "게임 화면과 같은 크기(100%)로 봅니다." };
        actual.Click += (_, _) => canvas.ActualSize();
        CanvasTools.Children.Add(actual);
        CanvasTools.Children.Add(_canvasNote);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { StopPlay(); return; }
            offCheck.IsChecked = canvas.ShowOff = true;   // 꺼진 창 is on each time 설정 opens, never saved
            apartCheck.IsChecked = canvas.BoxesApart = false;   // 칸 하나씩 is off each time (user, 2026-11-13: no accidental box moves)
            ShowCanvasShot();   // and the note says so
        };
        Loaded += (_, _) => { ShowCanvasShot(); canvas.Relayout(); };   // the picture at the window's real DPI
        ShowInspector(null);
    }

    /// <summary>Whether a window is on, as the canvas draws it: the 디버프 and 줄 수 windows also follow their gates (자동).</summary>
    bool CanvasWindowOn(OverlayPart part) => part switch
    {
        OverlayPart.Debuffs => _s.Raids.DebuffShown(_s.Debuffs),
        OverlayPart.BarCount => _s.Raids.CountShown(),
        _ => TrayToggles.IsShown(_s, part),
    };

    /// <summary>--hide-off: 꺼진 창 off, for screenshots.</summary>
    internal void DemoHideOff() { if (_offCheck is not null && _canvas is not null) _offCheck.IsChecked = _canvas.ShowOff = false; }

    readonly TextBlock _canvasNote = new() { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new(8, 0, 0, 0) };

    void ShowCanvasNote() => _canvasNote.Text = _canvas?.BoxesApart == true
        ? "칸을 눌러 설정 · 끌어서 이동 · 모서리로 크기 · 막대 끝으로 길이 · 화살표 키로 1px (Shift 10px) · Ctrl+화살표로 크기 · 휠로 확대"
        : "칸을 눌러 설정 · 끌어서 이동 (내 스킬창은 칸 하나씩을 켜야 칸마다) · 모서리로 크기 · 막대 끝으로 길이 · 화살표 키로 1px (Shift 10px) · Ctrl+화살표로 크기 · 휠로 확대";

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

    /// <summary>The demo's --pick: as if <paramref name="box"/> were clicked in the preview.</summary>
    internal void Inspect(CanvasBox box)
    {
        _canvas?.Select(box);   // outlined on the canvas too
        ShowInspector(box);
    }

    /// <summary>The inspector for <paramref name="box"/> (null: nothing picked, the overlay as a whole).</summary>
    void ShowInspector(CanvasBox? box)
    {
        _inspected = box;
        var (title, about, panels) = InspectorFor(box);
        InspectorHead.Children.Clear();
        InspectorHead.Children.Add(InspectorChips(box, title));
        InspectorHead.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        InspectorHead.Children.Add(Muted(new TextBlock { Text = about, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) }));
        if (SizeHint(box) is { } hint)
            InspectorHead.Children.Add(new TextBlock { Text = hint, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) });
        if (PageOf(box) is { } page)
        {
            var go = new Button { Content = $"{page} 설정 열기 →", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
            go.Click += (_, _) => Go(page);
            InspectorHead.Children.Add(go);
        }
        foreach (var p in panels) Detach(p);
        Detach(_contextNotes);
        Inspector.Children.Clear();
        if (ClassScoped(box)) Inspector.Children.Add(_contextNotes);
        foreach (var p in panels) { Inspector.Children.Add(p); ShowCardBadges(p, false); }
        InspectorBody.ScrollToTop();
        var scope = new TextBlock
        {
            Text = box?.Kind switch
            {
                "Skills" => $"색과 효과: {ClassName(ShownClass)}에만 · 칸 모양: {BadgeText()}",
                "Plate" => $"쿨 진행·남은 쿨: {ClassName(ShownClass)}에만 · 나머지: {BadgeText()}",
                "BuffBar" => $"프로필 {_s.ActiveProfile + 1}의 모든 직업각인에 적용",   // 버프 막대 is one per profile (user, 2026-11-13)
                "Gauge" => $"프로필 {_s.ActiveProfile + 1}의 {ClassName(ShownClass)}에만 적용",   // 게이지 is per profile · class since 0.23 (user, 2026-11-13)
                _ when ClassScoped(box) => $"{ClassName(ShownClass)}에만 적용",
                // Looks and spots are per profile · played class (class-profiles.plan.md A), the same text as the badge.
                _ => $"{BadgeText()}에 적용",
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

    /// <summary>What the inspector can show, as chips over its title (user, 2026-11-12): the canvas's windows can be picked from
    /// here too, the one shown is lit like its gold outline on the canvas, and 전체 goes back. 스킬창 is the skill window as a whole,
    /// 스킬 칸 the first box (the picked one's name while one is picked), 복사 the copy page (user, 2026-11-13: both were hard to find).</summary>
    WrapPanel InspectorChips(CanvasBox? box, string title)
    {
        var chips = new List<(string Text, CanvasBox? Box)>
        {
            ("전체", null), ("스킬창", new("Plate", null, null)),
            (box?.Kind == "Skills" ? title : "스킬 칸", box?.Kind == "Skills" ? box : new("Skills", Part.일반1, null)),
            ("게이지", new("Gauge", Part.게이지, null)), ("버프 막대", new("BuffBar", FreeLayout.FirstBuff, null)), ("디버프", new("Debuff", null, 0)),
        };
        if (_s.Party.ShowOthersBySlot.Count > 0) chips.Add(("파티원", new("Party", null, null)));
        chips.AddRange([("단심", new("Judgment", null, null)), ("암수", new("Amsu", null, null)), ("줄 수", new("Raid", null, null)), ("복사", new("Copy", null, null))]);
        var row = new WrapPanel { Margin = new(0, 0, 0, 10) };
        foreach (var (text, target) in chips)
        {
            var chip = new ToggleButton { Content = text, IsChecked = target?.Kind == box?.Kind, Padding = new(8, 1, 8, 2), Margin = new(0, 0, 4, 4), FontSize = 12 };
            if (target?.Kind == "Copy") chip.Click += (_, _) => { _canvas?.Select(null); ShowCopy(); };
            else chip.Click += (_, _) => { _canvas?.Select(target); ShowInspector(target); };   // ShowInspector also for a box the canvas can't pick (no party)
            row.Children.Add(chip);
        }
        return row;
    }

    /// <summary>The panel's 기본값 (two clicks: the button asks once), the same reset its section had at the bottom. Gone where
    /// every row has its own ↺ (버프 막대, 디버프, 단심: decisions.md, "Row rule").</summary>
    Button? FooterReset(CanvasBox? box) => box?.Kind switch
    {
        null or "Plate" or "Skills" => ResetButton("기본값", "전체와 칸 모양(불투명도, 남은 쿨, 쿨 진행, 초각스, 금테)을 되돌립니다. 위치, 크기, 배경판 색은 남습니다.", () => SettingsResets.Display(_s)),
        "Gauge" => ResetButton("기본값", "게이지의 색·두께·읽기 방식과 모든 클래스·직업각인의 막대 모양(구분선, 색, 빛)을 되돌립니다. 클래스의 게이지 규칙과 배운 게이지 값은 남습니다.", () => SettingsResets.Gauge(_s)),
        _ => null,
    };

    /// <summary>Box options and class rows follow 편집 중; the debuff window, the party's window and the overlay as a whole do not.</summary>
    static bool ClassScoped(CanvasBox? box) => box?.Kind is "Skills" or "Gauge" or "Plate";

    readonly Dictionary<BuffKind, UIElement> _buffHeads = [];

    readonly Dictionary<int, UIElement> _debuffHeads = [];

    void ScrollToDebuff(int i) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (_debuffHeads.TryGetValue(i, out var row)) (row as FrameworkElement)?.BringIntoView(); });

    void ScrollToBuff(BuffKind k) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (_buffHeads.TryGetValue(k, out var head)) (head as FrameworkElement)?.BringIntoView(); });

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
                // The buff section already holds every bar's rows: no second copy in 칸 옵션, scroll to the clicked bar's.
                ScrollToBuff((BuffKind)(bar - FreeLayout.FirstBuff));
                return ("버프 막대", "MP 바 위의 버프를 남은 시간 막대로 보여 줍니다.", [_secBuffs]);
            case { Kind: "Debuff", Debuff: { } d }:
                // Same as the buff bars: the section already holds this box's size rows.
                ScrollToDebuff(d);
                return ("디버프", "디버프 칸입니다. MP 바 위의 내 디버프와 보스 체력바 아래 빨강·파랑 숫자를 보여 줍니다.", [_secBuffs]);
            case { Kind: "Raid" }:
                return ("레이드 줄 수 창", "보스 체력바의 줄 수를 보여 주고, 정한 줄 수에서 빛납니다. 관문과 빛날 줄 수는 레이드에서 정합니다.", [_secRaidCount]);
            case { Kind: "Judgment" }:
                return ("단심", "단죄·심판 룬의 심판 시간입니다.", [_secOther]);
            case { Kind: "Amsu" }:
                return ("암흑수류탄", "보스에 걸린 암흑수류탄의 남은 시간입니다.", [_secAmsu]);
            case { Kind: "Party" }:
                return ("파티원 창", "파티원 칸의 모양입니다. 클래스마다 따로 저장됩니다.", [_secMembers]);
            case { Kind: "Plate" }:
                ShowBox(null, plate: true);
                return ("스킬창", "스킬 칸과 게이지가 든 창 전체의 모양입니다.", [BoxTab, _secApart, _secDisplay]);
            default:
                return ("전체", "오버레이 전체의 표시와 배치입니다. 왼쪽 화면에서 칸을 누르거나 위에서 고르면 그 칸의 설정이 여기에 나옵니다.", [_secCopyLink, _secDisplay, _secPlace]);
        }
    }

    /// <summary>The rail page with the rest of <paramref name="box"/>'s settings, for the inspector's shortcut (user, 2026-11-13);
    /// null: the overlay as a whole or the copy page, which have no page of their own.</summary>
    static string? PageOf(CanvasBox? box) => box?.Kind switch
    {
        "Skills" or "Plate" => AreaSkills,
        "Gauge" => AreaGauge,
        "BuffBar" or "Debuff" => AreaBuffs,
        "Judgment" => AreaJudgment,
        "Amsu" => AreaAmsu,
        "Raid" => AreaRaid,
        "Party" => AreaParty,
        _ => null,
    };

    /// <summary>Where a window whose size slider went to the canvas (settings-copy.plan.md) is sized now; null: it has its rows.</summary>
    static string? SizeHint(CanvasBox? box) => box?.Kind switch
    {
        "Judgment" or "Amsu" or "Raid" => "크기: 화면에서 테두리를 끌거나 Ctrl+화살표",
        "BuffBar" => "길이: 화면에서 막대 끝을 끌어 바꿉니다 (자유 배치에서는 칸 모서리)",
        _ => null,
    };

    /// <summary>The inspector again for what it shows, after its rows were rebuilt.</summary>
    void RefreshInspector()
    {
        if (_canvas is null || _area.Name != AreaOverlay) return;   // hidden: its panels may be on the page shown (파티원 모양)
        if (_inspected?.Kind == "Copy") FillCopyInspector();
        else ShowInspector(_inspected);
    }
}
