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
/// chips, ± 줄 when per gate, 디버프 창 and 자동 with 예시 보기), the alert offset and glow, and the 줄 수 창's look.</summary>
public partial class SettingsWindow
{
    const string AreaRaid = "레이드";
    readonly CardStack _secRaid = new();
    static readonly IReadOnlyList<RaidGate> RaidGates = RaidData.Load(CharacterService.DefaultProfilesDir);
    string? _raidSeen;
    string? _raidTab;   // the tab shown (카제로스 레이드 …); null: the recognised gate's, else the first
    HashSet<string>? _raidOpen;   // the raid boxes open; null until first shown (then the recognised, else last recognised, raid)
    string? _raidLast;   // the gate recognised last (kept after the fight ends)

    /// <summary>Redraws 레이드 (the 2-1 figures filled from the party's meter); any thread.</summary>
    public void RefreshRaid() => Dispatcher.BeginInvoke(FillRaid);

    /// <summary>What 자동 인식 recognises now (a gate id, null: none), shown under 보스 인식.</summary>
    public void SetRaidSeen(string? id)
    {
        _raidSeen = id;
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
        int counts = gates.Count(g => r.Gates.GetValueOrDefault(g.Id)?.CountWindow == true);
        var debuffGates = gates.Where(g => g.DebuffRaid != Raid.없음).ToList();
        int debuffs = debuffGates.Count(g => r.Gates.GetValueOrDefault(g.Id)?.DebuffWindow == true);
        string summary = (r.CountEnabled ? $"줄 수 창 {counts}/{gates.Count}" : "줄 수 읽기 꺼짐")
            + (debuffGates.Count > 0 ? $" · 디버프 창 {debuffs}/{debuffGates.Count}" : "");

        var icons = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        var chevron = new TextBlock { Text = open ? "" : "", FontFamily = icons, FontSize = 12, Margin = new(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var info = new TextBlock { Text = summary, Opacity = 0.7, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
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

    void FillRaid()
    {
        var r = _s.Raids;
        void Again() => Dispatcher.BeginInvoke(FillRaid);
        var p = Refill(_secRaid, "레이드", "보스 체력바 위의 이름으로 레이드·관문을 알아보고, 체력바 오른쪽 끝의 줄 수(X 494)를 줄 수 창에 따로 보여 줍니다. " +
            "정한 줄 수가 되면 창이 빛납니다. 알아보지 못한 보스에는 아무것도 띄우지 않습니다.");
        p.Children.Add(Note("아직 실제 레이드 화면으로 확인하지 않았습니다. 스크린샷 몇 장으로만 맞췄습니다."));

        // The main 줄 수 switch first: it gates every gate's own 줄 수 창 below, which stays as set.
        p.Children.Add(Row("", Check("줄 수 읽기 (모든 레이드)", r.CountEnabled, v => r.CountEnabled = v, () => { Changed(); Again(); },
            "끄면 모든 레이드에서 줄 수(X 494)를 읽지 않고 줄 수 창을 숨깁니다. 관문마다 켜 둔 줄 수 창은 그대로 남아, 다시 켜면 돌아옵니다. " +
            "디버프 창은 따르지 않습니다. 트레이 메뉴의 레이드에도 있습니다.")));
        if (!r.CountEnabled) p.Children.Add(Note("줄 수 읽기가 꺼져 있습니다. 아래 관문의 줄 수 창 설정은 그대로 남아 있고, 다시 켜면 그대로 돌아옵니다."));

        p.Children.Add(SubHeading("보스 인식"));
        p.Children.Add(Row("", Check("자동 인식", r.Auto, v => r.Auto = v, () => { Changed(); Again(); },
            "보스 이름으로 관문을 알아봅니다. 끄면 아래에서 고른 관문으로 정합니다. 트레이 메뉴의 레이드와 같습니다.")));
        if (r.Auto) p.Children.Add(Row("지금 인식", Label(RaidData.Find(RaidGates, _raidSeen) is { } seen ? RaidData.Label(seen) : "인식 안 됨", 0.7)));
        else p.Children.Add(Row("관문", GateCombo()));

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

        p.Children.Add(SubHeading("알림", "목표 줄 수에 ±줄을 더한 곳에서 빛납니다. 750에 +5면 755에서 빛납니다."));
        p.Children.Add(Row("", Check("전 관문 공통", !r.OffsetPerGate, v => r.OffsetPerGate = !v, () => { Changed(); Again(); },
            "끄면 관문마다 ±줄을 따로 정합니다.")));
        if (!r.OffsetPerGate) p.Children.Add(OffsetRow("± 줄", r.Offset, v => r.Offset = v));
        p.Children.Add(Row("", Check("빛나기", r.Glow, v => r.Glow = v, () => { Changed(); Again(); })));
        if (r.Glow)
        {
            p.Children.Add(Row("빛나는 동안", EnumCombo([(GlowUntil.Seconds, "정한 초 동안"), (GlowUntil.Bars, "정한 줄만큼 지날 때까지")], r.GlowUntil,
                v => { r.GlowUntil = v; Again(); })));
            p.Children.Add(r.GlowUntil == GlowUntil.Seconds
                ? SliderRow("초", 1, 120, r.GlowSeconds, 1, v => $"{v:0}초", v => r.GlowSeconds = (int)v, Changed)
                : SliderRow("줄", 1, 50, r.GlowBars, 1, v => $"{v:0}줄", v => r.GlowBars = (int)v, Changed));
        }

        p.Children.Add(SubHeading("줄 수 창 모양"));
        p.Children.Add(Row("표시", EnumCombo([(BarCountLook.숫자, "숫자"), (BarCountLook.화면그대로, "화면 그대로")], r.Look, v => r.Look = v)));
        p.Children.Add(ColorRow(Label("배경 색"), r.Background, v => r.Background = v, swatch: false));
        p.Children.Add(SliderRow("불투명도", 0.1, 1, r.Opacity, 0.05, v => $"{v * 100:0}%", v => r.Opacity = v, Changed));
        p.Children.Add(SliderRow("크기", ClassProfiles.MinScale, ClassProfiles.MaxScale, r.Width, 0.05, v => $"{v * 100:0}%", v => (r.Width, r.Height) = (v, v), Changed));
        p.Children.Add(Note("편집 모드나 내 오버레이 화면에서 줄 수 창을 끌어 옮깁니다."));
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
                g.Shields.Count == 0 ? null : "실드 기믹: " + string.Join(", ", g.Shields.Select(x => $"{x.At}줄에서 +{x.Lines}줄")) + " (HP 뒤 괄호 수치로 읽어 DPS에 넣습니다).",
            }.OfType<string>()) is { Length: > 0 } help ? help : null));
        // Greyed, kept as set, while the main 줄 수 읽기 is off.
        var count = Row("줄 수 창",
            Check("켜기", row.CountWindow, v => r.Gate(g.Id).CountWindow = v, () => { Changed(); again(); }),
            Check("자동", row.CountAuto, v => r.Gate(g.Id).CountAuto = v, Changed, "이 관문 보스를 알아본 동안만 보여 줍니다. 끄면 이 관문 알림으로 늘 보여 줍니다."));
        count.IsEnabled = r.CountEnabled;
        p.Children.Add(count);
        if (row.CountWindow)
        {
            var alerts = Row(g.HiddenHp.Count > 0 ? "알림 HP %" : "알림 줄 수", AlertChips(g, again));
            alerts.IsEnabled = r.CountEnabled;
            p.Children.Add(alerts);
        }
        void Extras()   // after the gate's own rows: the ??? estimate and the 에스더 plan (종막 EX)
        {
            if (g.HiddenHp.Count > 0) HiddenRows(p, g);
            if (g.Esthers.Count > 0) EstherPlanRows(p, g);
        }
        if (r.OffsetPerGate) p.Children.Add(OffsetRow("± 줄 (이 관문)", row.Offset, v => r.Gate(g.Id).Offset = v));
        if (g.DebuffRaid == Raid.없음)
        {
            p.Children.Add(Row("디버프 창", Label("디버프 없음", 0.7)));
            Extras();
            return;
        }
        var demo = new Button { Content = "예시 보기", Margin = new(0, 2, 0, 2), IsEnabled = row.DebuffWindow, ToolTip = "내 오버레이에서 디버프 창의 예시를 보여 줍니다." };
        demo.Click += (_, _) => GoTo(new(AreaOverlay, "디버프", new("Debuff", null, 0)));
        p.Children.Add(Row("디버프 창",
            Check("켜기", row.DebuffWindow, v => { r.Gate(g.Id).DebuffWindow = v; r.SyncDebuffRaid(_s.Debuffs); }, () => { Changed(); again(); FillBuffs(); }),
            Check("자동", row.DebuffAuto, v => r.Gate(g.Id).DebuffAuto = v, Changed, "이 관문 보스를 알아본 동안만 보여 줍니다. 끄면 늘 보여 줍니다."),
            demo));
        Extras();
    }

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
            ToolTip = g.Alerts.Count == 0 ? "이 관문의 기본 알림은 아직 없습니다." : $"기본값: {string.Join(", ", g.Alerts)}" };
        reset.Click += (_, _) => Set(null);
        wrap.Children.Add(box);
        wrap.Children.Add(add);
        wrap.Children.Add(reset);
        return wrap;
    }

    /// <summary>A ??? gate (종막 EX 2-3): its HP left is estimated (HiddenHp) from the parties' damage, the planned 에스더 and the
    /// gimmick; the share, the other party's multiple and the 에스더 are the user's.</summary>
    void HiddenRows(StackPanel p, RaidGate g)
    {
        var r = _s.Raids;
        var row = r.Gates.GetValueOrDefault(g.Id) ?? new GateSettings();
        p.Children.Add(Note("HP가 ???로 나와 줄 수 창에 남은 HP를 추정해 보여 줍니다(≈62% 1.2조). 2-2 바로 뒤 이어지는 전투에서, DPS 미터기를 켜고 " +
            "각 파티의 딜러 셋이나 서포터가 앱으로 피해량을 보내야 합니다. 25%에서 모두 쓰러진 뒤 10%로 줄어드는 기믹도 반영합니다. " +
            "추정 최대 HP: " + string.Join(", ", g.HiddenHp.Select(h => $"{h.Key} {h.Value:N0}"))));
        var from = RaidData.Find(RaidGates, g.CalibrateFrom);
        p.Children.Add(SliderRow("조력 비율", RaidSettings.MinAssistShare, RaidSettings.MaxAssistShare, r.AssistShare, 0.5, v => $"{v:0.0}%",
            v => r.AssistShare = v, Changed, "서포터의 조력 피해량이 파티 피해의 몇 %인지. 딜러 셋이 모두 보내지 않는 파티는 조력 피해량 × 100 ÷ 이 값으로 셉니다."));
        p.Children.Add(SliderRow("다른 파티 배수", 0, RaidSettings.MaxOtherParty, r.OtherParty, 0.1, v => $"×{v:0.0}",
            v => r.OtherParty = v, Changed, "한 파티만 피해량을 알 때, 다른 파티를 그 파티의 몇 배로 볼지. 1.0이면 같다고 봅니다. 아래 2-1 결과가 두 파티 다 있으면 그 비율을 씁니다."));
        if (from is null) return;
        p.Children.Add(Row($"{from.Gate} 끝 조력 피해량 (억)",
            Label("1파티 "), EokBox(row.Assist1, v => r.Gate(g.Id).Assist1 = v, "1파티 서포터의 조력 피해량 (억). 앱을 쓰는 서포터는 클리어 때 채워집니다", refill: true),
            Label("  2파티 "), EokBox(row.Assist2, v => r.Gate(g.Id).Assist2 = v, "2파티 서포터의 조력 피해량 (억). 앱을 쓰는 서포터는 클리어 때 채워집니다", refill: true)));
        if (!(row.Assist1 > 0 && row.Assist2 > 0))
        {
            p.Children.Add(Note($"두 파티 값을 다 알면 파티 비율을, {from.Gate}의 에스더 계획까지 있으면 조력 비율도 {from.Gate} 결과로 계산합니다."));
            return;
        }
        double esther21 = Esther.Total(r.Gates.GetValueOrDefault(from.Id)?.Esthers ?? []);
        string ratio = $"2파티 = 1파티 × {row.Assist2 / row.Assist1:0.00}";
        if (esther21 > 0)
        {
            var shares = from.Hp.Select(h => HiddenHp.CalibratedShare(h.Value, row.Assist1, row.Assist2, esther21) is { } s
                && s is >= RaidSettings.MinAssistShare and <= RaidSettings.MaxAssistShare ? $"{h.Key} {s:0.0}%" : $"{h.Key} –").ToList();   // – : out of range, not used
            p.Children.Add(Note($"{from.Gate} 결과로: {ratio}, 조력 비율 {string.Join(", ", shares)} (에스더 계획 {esther21:N0}억을 뺀 피해로 계산해 위 조력 비율 대신 씁니다)"));
        }
        else
        {
            var est = string.Join(", ", from.Hp.Select(h => HiddenHp.EstherShare(h.Value, row.Assist1, row.Assist2, r.AssistShare) is { } s ? $"{h.Key} {s * 100:0.0}%" : null).OfType<string>());
            p.Children.Add(Note($"{from.Gate} 결과로: {ratio}, 에스더 ≈ HP의 {est} (2-3 에스더 계획이 비어 있으면 2-3 최대 HP에 곱해 {HiddenHp.FallbackAt}%에서 뺍니다)"));
            if (from.Hp.Values.Any(hp => HiddenHp.EstherShare(hp, row.Assist1, row.Assist2, r.AssistShare) > 0.3))
                p.Children.Add(Note("에스더가 HP의 30%를 넘게 나옵니다. 조력 피해량이 억 단위로 맞는지, 조력 비율이 맞는지 확인하세요."));
        }
    }

    /// <summary>A gate's 에스더 계획 (종막 EX): each use (name, damage 억, when: bar count, or HP % on a ??? gate) with ✕, and a skill
    /// to add from the gate's list (its EX 나이트메어 damage filled in, editable).</summary>
    void EstherPlanRows(StackPanel p, RaidGate g)
    {
        var r = _s.Raids;
        var plan = r.Gates.GetValueOrDefault(g.Id)?.Esthers ?? [];
        string unit = g.HiddenHp.Count > 0 ? "%" : "줄";
        void Set(List<EstherUse> next) { r.Gate(g.Id).Esthers = next; Changed(); Dispatcher.BeginInvoke(FillRaid); }
        p.Children.Add(SubHeading($"에스더 계획 · 합계 {Esther.Total(plan):N0}억",
            (g.HiddenHp.Count > 0 ? "추정 HP가 정한 %에 닿을 때 그 피해를 뺍니다. 10% 이하에 둔 에스더(히든 아만)는 25% 기믹 뒤 10%에서 바로 뺍니다. 레이드 중에 바꿔도 바로 다시 계산합니다. "
                : "이 관문에서 쓸 에스더와 쓰는 줄 수. 합계는 2-3 추정의 조력 비율을 맞추는 데 씁니다. ") +
            $"피해는 {Esther.Basis} 기준이고, 다른 난이도는 보스 HP 비율로 맞춥니다."));
        for (int i = 0; i < plan.Count; i++)
        {
            int at = i;
            var use = plan[i];
            var remove = new Button { Content = "✕", Margin = new(6, 0, 0, 0), ToolTip = "이 에스더 빼기" };
            remove.Click += (_, _) => Set([.. plan.Where((_, k) => k != at)]);
            var when = new TextBox { Width = 60, Text = use.At.ToString(CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = unit == "%" ? "추정 HP %" : "줄 수" };
            void SetAt() { if (int.TryParse(when.Text.Trim(), out int v) && v >= 0 && v != use.At) Set([.. plan.Select((u, k) => k == at ? u with { At = v } : u)]); }
            when.LostFocus += (_, _) => SetAt();
            when.KeyDown += (_, e) => { if (e.Key == Key.Enter) SetAt(); };
            p.Children.Add(Row(use.Name, EokBox(use.Damage, v => Set([.. plan.Select((u, k) => k == at ? u with { Damage = v } : u)]), $"피해 (억, {Esther.Basis} 기준)"),
                Label(" 억   "), when, Label($" {unit}에서"), remove));
        }
        var pick = Combo();
        pick.ItemsSource = g.Esthers.Select(e => new ComboBoxItem { Tag = e,
            Content = $"{e.Name} ({string.Join(", ", e.Damage.Select(d => $"{d.Key} 약 {d.Value:N0}억"))})" }).ToList();
        pick.SelectedIndex = 0;
        var add = new Button { Content = "+ 추가", Margin = new(6, 0, 0, 0) };
        add.Click += (_, _) =>
        {
            if (pick.SelectedItem is not ComboBoxItem { Tag: EstherSkill skill }) return;
            double damage = skill.Damage.GetValueOrDefault("EX 나이트메어", skill.Damage.Values.FirstOrDefault());
            Set([.. plan, new EstherUse(skill.Name, damage, skill.At ?? (unit == "%" ? HiddenHp.FallbackAt : 0))]);
        };
        p.Children.Add(Row("에스더 추가", pick, add));
    }

    /// <summary>A number box in 억 (empty = 0): set on Enter or leaving the box; <paramref name="refill"/> redraws the page (derived lines).</summary>
    TextBox EokBox(double value, Action<double> set, string tip, bool refill = false)
    {
        var box = new TextBox { Width = 90, Text = value > 0 ? value.ToString("0.#", CultureInfo.InvariantCulture) : "",
            VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip };
        void Set()
        {
            double v = double.TryParse(box.Text.Trim().Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 ? Math.Min(d, RaidSettings.MaxEsther) : 0;
            if (v == value) return;
            value = v;
            set(v);
            Changed();
            if (refill) Dispatcher.BeginInvoke(FillRaid);
        }
        box.LostFocus += (_, _) => Set();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Set(); };
        return box;
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
        return [SliderRow(prefix + "너비", BarSizes.MinDebuff, BarSizes.MaxDebuff, box.Width, 0.1, v => $"{v * 100:0}%", v => _s.Debuffs.Box(i).Width = v, Changed),
            SliderRow(prefix + "높이", BarSizes.MinDebuff, BarSizes.MaxDebuff, box.Height, 0.1, v => $"{v * 100:0}%", v => _s.Debuffs.Box(i).Height = v, Changed)];
    }
}
