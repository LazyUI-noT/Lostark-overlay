using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>레이드 (spec § B): 자동 인식 or a gate picked by hand, one block per gate (줄 수 창 and 자동, its alert counts as
/// chips, ± 줄 when per gate, 디버프 창 and 자동 with 예시 보기) and the alert offset. The 줄 수 창's switch, glow and look are on
/// 내 오버레이 → 줄 수 (<c>SettingsWindow.RaidCount.cs</c>), linked from the top row.</summary>
public partial class SettingsWindow
{
    const string AreaRaid = "레이드";
    readonly CardStack _secRaid = new();
    static readonly IReadOnlyList<RaidGate> RaidGates = RaidData.Load(CharacterService.DefaultProfilesDir);
    string? _raidSeen;
    string? _raidSeenDifficulty;   // the dungeon panel's difficulty when the gate was recognised (지금 인식's capsule)
    string? _raidTab;   // the tab shown (카제로스 레이드 …); null: the recognised gate's, else the first
    HashSet<string>? _raidOpen;   // the raid boxes open; null until first shown (then the recognised, else last recognised, raid)
    string? _raidLast;   // the gate recognised last (kept after the fight ends)

    /// <summary>--demo --raid-open=name[,name] [--raid-scroll=px]: 레이드 with those raid boxes open, scrolled (for shot.ps1).</summary>
    internal void DemoRaidOpen(string raids, string? scroll)
    {
        _raidOpen = [.. raids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        FillRaid();
        if (!double.TryParse(scroll, NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) return;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            Body.ScrollToVerticalOffset(y);
        });
    }

    /// <summary>What 자동 인식 recognises now (a gate id, null: none), shown under 보스 인식.</summary>
    public void SetRaidSeen(string? id, string? difficulty = null)
    {
        (_raidSeen, _raidSeenDifficulty) = (id, difficulty);
        if (RaidData.Find(RaidGates, id) is { } seen && _raidLast != id)
        {
            _raidLast = id;
            _raidOpen?.Add(seen.Raid);   // a newly recognised raid opens; the others stay as the user left them
        }
        FillRaid();
    }

    /// <summary>One raid as a box: a header (chevron, name, what's on) that opens or closes it, and when open its gates inside.</summary>
    UIElement RaidBox(string name, IReadOnlyList<RaidGate> gates, Action again)
    {
        var r = _s.Raids;
        bool open = _raidOpen!.Contains(name);
        string summary = r.CountEnabled ? "줄 수 창 켜짐" : "줄 수 읽기 꺼짐";

        var icons = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        var chevron = new TextBlock { Text = open ? "" : "", FontFamily = icons, FontSize = 12, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var info = Muted(new TextBlock { Text = summary, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var head = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(info, Dock.Right);
        head.Children.Add(info);
        head.Children.Add(chevron);
        head.Children.Add(title);
        var toggle = new Button
        {
            Content = head, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(14, 10, 14, 10), Cursor = Cursors.Hand,
            Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new(0), ToolTip = open ? "접기" : "펼치기",
        };
        System.Windows.Automation.AutomationProperties.SetName(toggle, $"{name} {(open ? "접기" : "펼치기")}");
        toggle.Click += (_, _) => { if (!_raidOpen.Remove(name)) _raidOpen.Add(name); again(); };

        var inside = new StackPanel { Children = { toggle } };
        if (open)
        {
            inside.Children.Add(Divider(0, 0));
            var body = new CardStack { Margin = new(14, 4, 14, 8) };
            foreach (var g in gates) GateRows(body, g, again);
            inside.Children.Add(body);
        }
        var box = new Border { Child = inside, CornerRadius = new(8), BorderThickness = new(1), Margin = new(0, 0, 0, 8) };
        box.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        box.SetResourceReference(Border.BackgroundProperty, CardBrushKey);   // reads as a box even when closed
        CardStack.SetBreak(box, true);   // its own box, not a row in the page's cards
        return box;
    }

    /// <summary>The 줄 수 창 row's state line, under the profile · class badge (the switch is per profile · class).</summary>
    UIElement CountState(bool on)
    {
        var state = Label(on ? "켜짐 (빛나기와 모양은 내 오버레이 → 줄 수)" : "꺼짐: 아래 관문 설정은 남고 흐리게 보입니다", 0.7);
        if (ProfileBadge.Of(AreaRaid, option: ProfileBadge.RaidCountNote) != BadgeAt.Option) return state;
        var chip = ProfileBadgeChip();
        ((FrameworkElement)chip).Margin = new(0, 0, 0, 4);
        return Stack(chip, state);
    }

    void FillRaid()
    {
        var r = _s.Raids;
        void Again() => Dispatcher.BeginInvoke(FillRaid);
        var p = Refill(_secRaid, "레이드", "보스 체력바 위의 이름으로 레이드·관문을 인식하고, 체력바 오른쪽 끝의 줄 수(예: X 494)를 줄 수 창에 따로 보여 줍니다. " +
            "정한 줄 수가 되면 창이 빛납니다. 인식하지 못한 보스에는 아무것도 띄우지 않습니다.");

        // The main 줄 수 switch gates every gate's own 줄 수 창 below; it lives with the window on 내 오버레이 (per profile ·
        // class, class-profiles.plan.md E), shown here with a way there, as 버프/디버프 shows the 디버프 창's state.
        var toCount = new Button { Content = "줄 수 창 열기", Margin = new(0, 2, 0, 2) };
        toCount.Click += (_, _) => ShowRaidCount();
        p.Children.Add(Row("줄 수 창", CountState(r.CountEnabled), toCount));
        // One 디버프 창 switch for every raid (user, 2026-11-03); a raid turned on by hand is the tray's (표시 → 디버프 창).
        p.Children.Add(DefaultedCheck(Check("디버프 창 자동 (모든 레이드)", r.DebuffsAuto, v => r.DebuffsAuto = v, () => { Changed(); Again(); FillBuffs(); },
            "켜면 디버프가 있는 레이드(3막 모르둠)에서 보스를 알아본 동안 디버프 창을 보여 줍니다. " +
            "보스를 못 알아봐도 보고 싶으면 트레이 메뉴 → 표시 → 디버프 창을 켜세요. 레이드 안에서는 늘 보입니다. 마을·지옥·필드에서는 어느 쪽이든 보이지 않습니다."), s => s.Raids.DebuffsAuto, (s, v) => s.Raids.DebuffsAuto = v));

        p.Children.Add(SubHeading("보스 인식"));
        p.Children.Add(DefaultedCheck(Check("자동 인식", r.Auto, v => r.Auto = v, () => { Changed(); Again(); },
            "보스 이름으로 관문을 알아봅니다. 끄면 아래에서 고른 관문을 씁니다. 트레이 메뉴의 레이드와 같습니다."), s => s.Raids.Auto, (s, v) => s.Raids.Auto = v));
        if (r.Auto) p.Children.Add(RaidData.Find(RaidGates, _raidSeen) is { } seen
            ? Row("지금 인식", RaidBadgeView.Of(seen.Raid, _raidSeenDifficulty), Label(RaidData.Label(seen), 0.7))
            : Row("지금 인식", Label("인식 안 됨", 0.7)));
        else p.Children.Add(Row("관문", GateCombo()));

        // 기타 인식 (user, 2026-11-08): the boss bar's own reads, and 대난투 체력 as one switch for every raid with 대난투.
        p.Children.Add(SubHeading("기타 인식"));
        p.Children.Add(DefaultedCheck(Check("보스 체력 읽기", r.BossHpRead, v => r.BossHpRead = v, Changed,
            "DPS 미터기가 꺼져 있어도 보스 체력바의 글자를 읽어 간소화와 대난투 체력에 씁니다. 끄면 미터기, 줄 수, 암수 중 하나가 켜져 있을 때만 읽습니다."), s => s.Raids.BossHpRead, (s, v) => s.Raids.BossHpRead = v));
        p.Children.Add(WithExample(BossBarExample.EasyHp(), DefaultedCheck(Check("보스 체력 간소화", r.BossHpEasy, v => r.BossHpEasy = v, Changed,
            "보스 이름 옆에 남은 체력을 조 · 억으로 보여 줍니다 (예: [1조 5,392억]). 보호막이 있으면 +(1,200억)처럼 함께 보여 줍니다. " +
            "대난투 중에는 넣은 피해를 숫자 옆에 같은 방식으로 보여 줍니다."), s => s.Raids.BossHpEasy, (s, v) => s.Raids.BossHpEasy = v)));
        string daenantu = string.Join(", ", RaidGates.Where(g => g.IsShadow).Select(g => g.Raid).Distinct());
        p.Children.Add(WithExample(BossBarExample.Daenantu(), DefaultedCheck(Check("대난투 체력 표시 (모든 대난투 레이드)", r.ShadowHpAll(RaidGates), v => r.SetShadowHp(v, RaidGates), Changed,
            "대난투(보스 체력바 대신 채우는 막대와 숫자가 나올 때) 동안, 마지막 체력에서 대난투 중 넣은 피해를 뺀 남은 줄과 체력을 체력바 자리에 보여 줍니다. " +
            "대난투 중에 보스가 죽으면 전투 기록을 클리어로 끝냅니다 (DPS 미터기가 켜져 있을 때만). " +
            "끄면 체력바 자리의 표시만 사라집니다. 대난투 숫자는 계속 읽으므로 전투 기록의 클리어와 숙제(보스 체력바 읽기)는 그대로 동작합니다. " +
            $"대난투가 있는 레이드 모두에 한 번에 켜고 끕니다{(daenantu.Length > 0 ? $" (지금: {daenantu})" : "")}."),
            s => s.Raids.ShadowHpAll(RaidGates), (s, v) => s.Raids.SetShadowHp(v, RaidGates))));

        // Tabs (카제로스 레이드, 그림자 레이드, 어비스 던전), then each raid as a heading with its gates, in raids.json order.
        var tabs = RaidGates.Select(g => g.TabName).Distinct().ToList();
        string tab = _raidTab is { } t && tabs.Contains(t) ? t : RaidData.Find(RaidGates, _raidSeen)?.TabName ?? tabs.FirstOrDefault() ?? "";
        var bar = new WrapPanel { Margin = new(0, 8, 0, 4) };
        foreach (var name in tabs)
        {
            var b = new System.Windows.Controls.Primitives.ToggleButton { Content = name, IsChecked = name == tab, Padding = new(12, 4, 12, 4), Margin = new(0, 0, 6, 0) };
            b.Click += (_, _) => { _raidTab = name; Again(); };
            bar.Children.Add(b);
        }
        CardStack.SetBreak(bar, true);   // between the cards, like a heading
        p.Children.Add(bar);
        // Opened at first: the raid recognised now, else the one recognised last.
        _raidOpen ??= RaidData.Find(RaidGates, _raidSeen ?? _raidLast) is { } open ? [open.Raid] : [];
        foreach (var raid in RaidGates.Where(g => g.TabName == tab).GroupBy(g => g.Raid))
            p.Children.Add(RaidBox(raid.Key, [.. raid], Again));
        HpChangeRows(p, Again);

        p.Children.Add(SubHeading("빛남 기준", "목표 줄 수에서 이만큼 더하거나 뺀 곳에서 빛납니다. 750에 +5면 755에서 빛납니다."));
        p.Children.Add(DefaultedCheck(Check("전 관문 공통", !r.OffsetPerGate, v => r.OffsetPerGate = !v, () => { Changed(); Again(); },
            "끄면 관문마다 ±줄을 따로 정합니다."), s => !s.Raids.OffsetPerGate, (s, v) => s.Raids.OffsetPerGate = !v));
        if (!r.OffsetPerGate) p.Children.Add(OffsetRow("± 줄", r.Offset, v => r.Offset = v));
    }

    /// <summary>One gate: its 줄 수 창, alerts, own ± 줄 (per gate) and 디버프 창. Its row is added to the settings only when changed.</summary>
    void GateRows(StackPanel p, RaidGate g, Action again)
    {
        var r = _s.Raids;
        var row = r.Gates.GetValueOrDefault(g.Id) ?? new GateSettings();
        string gate = string.IsNullOrWhiteSpace(g.Gate) ? g.Raid : g.Gate;   // the raid is the heading above
        p.Children.Add(SubHeading(g.Bosses.Count == 0 ? gate : $"{gate} · {string.Join(", ", g.Bosses)}",
            string.Join(" ", new[]
            {
                g.Hp.Count == 0 ? null : "보스 HP로 알아봅니다: " + string.Join(", ", g.Hp.Select(h => $"{h.Key} {h.Value:N0}")) + ".",
                g.Shields.Count == 0 ? null : "보호막 기믹: " + string.Join(", ", g.Shields.Select(x => $"{x.At}줄에서 +{x.Lines}줄")) + " (HP 뒤 괄호 수치로 읽어 DPS에 넣습니다).",
            }.OfType<string>()) is { Length: > 0 } help ? help : null));
        // Greyed, kept as set, while the main 줄 수 읽기 is off.
        var count = Row("줄 수 창",
            Check("자동", row.CountAuto, v => r.Gate(g.Id).CountAuto = v, Changed, "이 관문 보스를 알아본 동안만 보여 줍니다. 끄면 이 관문의 줄 수 창을 항상 보여 줍니다."));
        count.IsEnabled = r.CountEnabled;
        p.Children.Add(count);
        var alerts = Row(g.Hidden ? "빛날 HP %" : "빛날 줄 수", AlertChips(g, again));
        alerts.IsEnabled = r.CountEnabled;
        p.Children.Add(alerts);
        if (r.OffsetPerGate) p.Children.Add(OffsetRow("± 줄 (이 관문)", row.Offset, v => r.Gate(g.Id).Offset = v));
        if (g.Hidden) p.Children.Add(HiddenNote());
    }

    /// <summary>A ??? gate (종막 2-3): how its HP is estimated (HiddenHp: my 2-2 share, 0.9 × 2-2's HP, the 70% target).</summary>
    static UIElement HiddenNote() => Note(
        $"HP ??? 구간: 2-2에서 잰 내 피해량 비율로 깎은 HP를 추정해 줄 수 창에 보여 줍니다 (최대 HP는 2-2의 {HiddenHp.HpOfBefore:0.#}배, 목표 {HiddenHp.Target * 100:0}%). " +
        "인게임 전분을 열어 두고, 딜러는 피해량 열 · 서포터는 조력 피해량 열로 두세요.");

    /// <summary>The gate's alert counts as chips (✕ removes one), a box to add one, and 기본값 (the gate's shipped counts).</summary>
    UIElement AlertChips(RaidGate g, Action again)
    {
        var r = _s.Raids;
        var alerts = r.AlertsOf(g);
        var wrap = new WrapPanel();
        void Set(List<int>? list) { r.Gate(g.Id).Alerts = list; Changed(); again(); }
        foreach (int a in alerts)
        {
            var chip = new Button { Content = $"{a}  ✕", Margin = new(0, 0, 6, 4), ToolTip = "이 줄 수 지우기" };
            chip.Click += (_, _) => Set([.. alerts.Where(x => x != a)]);
            wrap.Children.Add(chip);
        }
        if (alerts.Count == 0) wrap.Children.Add(Label("없음  ", 0.7));
        var box = new TextBox { Width = 64, Margin = new(0, 0, 6, 4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "줄 수 (1–99999)" };
        void Add()
        {
            if (int.TryParse(box.Text.Trim(), out int v) && v is > 0 and < 100_000) Set([.. alerts.Append(v).Distinct().OrderDescending()]);
            else box.Text = "";
        }
        var add = new Button { Content = "추가", Margin = new(0, 0, 6, 4) };
        add.Click += (_, _) => Add();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(); };
        var reset = new Button { Content = "기본값", Margin = new(0, 0, 0, 4), IsEnabled = r.Gates.GetValueOrDefault(g.Id)?.Alerts is not null,
            ToolTip = g.Alerts.Count == 0 ? "이 관문의 기본 빛남 기준은 아직 없습니다." : $"기본값: {string.Join(", ", g.Alerts)}" };
        reset.Click += (_, _) => Set(null);
        wrap.Children.Add(box);
        wrap.Children.Add(add);
        wrap.Children.Add(reset);
        return wrap;
    }

    UIElement OffsetRow(string label, int value, Action<int> set) =>
        SliderRow(label, -20, 20, Math.Clamp(value, -20, 20), 1, v => v > 0 ? $"+{v:0}" : $"{v:0}", v => set((int)v), Changed,
            "목표 줄 수에 더합니다. 750에 +5면 755에서 빛납니다.");

    /// <summary>자동 인식 off: the gate the 줄 수 창 and 디버프 창 follow.</summary>
    ComboBox GateCombo()
    {
        var c = Combo();
        c.ItemsSource = RaidGates.Select(g => new ComboBoxItem { Content = RaidData.Label(g), Tag = g.Id }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _s.Raids.ManualGate);
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: string id }) { _s.Raids.ManualGate = id; Changed(); } };
        return c;
    }

    /// <summary>The debuff boxes, left to right.</summary>
    static readonly string[] DebuffNames = ["내 색", "교체", "번개"];

    /// <summary>Debuff box <paramref name="i"/>'s 너비 and 높이, 100% = the box at the window's size; in 버프/디버프 and in the
    /// 레이아웃 편집기's 칸 옵션.</summary>
    UIElement[] DebuffSizeSliders(int i, string prefix = "")
    {
        var box = _s.Debuffs.Box(i);
        // Peek, not Box, in the getters: reading must not add an entry (Box adds one the first time).
        DebuffBox Peek(Settings s) => s.Debuffs.Boxes.ElementAtOrDefault(i) ?? new();
        return [Defaulted(SliderRow(prefix + "너비", BarSizes.MinDebuff, BarSizes.MaxDebuff, box.Width, 0.1, v => $"{v * 100:0}%", v => _s.Debuffs.Box(i).Width = v, Changed),
                s => Peek(s).Width, (s, v) => s.Debuffs.Box(i).Width = v),
            Defaulted(SliderRow(prefix + "높이", BarSizes.MinDebuff, BarSizes.MaxDebuff, box.Height, 0.1, v => $"{v * 100:0}%", v => _s.Debuffs.Box(i).Height = v, Changed),
                s => Peek(s).Height, (s, v) => s.Debuffs.Box(i).Height = v)];
    }

    /// <summary>A row with its example under it (BossBarExample), one child so the card's divider stays between rows, not inside one.</summary>
    static UIElement WithExample(FrameworkElement? example, UIElement row) => example is null ? row : Stack(row, example);
}
