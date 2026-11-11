using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>DPS 미터기 (spec § C): on, 내 피해량 보내기 (also in 파티 → 보내기·받기), 범위 / 수치 / 표시 방식 and add-ons, colours,
/// the window, and the 전분 panel's spot for this game-area size (set by hand until 전분 위치 찾기 has footage to work from).
/// Also 파티 → 순서's 1·2파티 picks for the host.</summary>
public partial class SettingsWindow
{
    const string AreaMeter = "DPS 미터기";
    readonly CardStack _secMeter = new();
    IReadOnlyList<PartyMember> _partyMembers = [];
    bool _partyHost;

    /// <summary>The game-area size key ("W×H") the 전분 spot is saved for; null before a fit.</summary>
    public Func<string?>? GameArea { get; set; }

    /// <summary>The HUD of the fit in use (Engine.FitHud): 스킬창 맞추기's 테스트 row.</summary>
    public Func<double?>? FitHud { get; set; }

    /// <summary>The host puts a member in 1파티 or 2파티 (id, party); false: refused (that party is full).</summary>
    public Func<ulong, int, bool>? PartyMove { get; set; }

    /// <summary>The session's members and whether I host (파티 → 순서's 1·2파티 picks).</summary>
    public void SetPartyMembers(IReadOnlyList<PartyMember> members, bool host)
    {
        if (host == _partyHost && members.SequenceEqual(_partyMembers)) return;
        (_partyMembers, _partyHost) = ([.. members], host);
        FillOrder();
    }

    ComboBox BuffShareCombo(MeterSettings m, Action again)
    {
        var c = Combo();
        c.ItemsSource = m.BuffChoices().Select(v => new ComboBoxItem { Content = v == ThreeWay.표시안함 ? "표시 안 함" : v.ToString(), Tag = v }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().First(i => (ThreeWay)i.Tag == m.BuffShare());
        c.SelectionChanged += (_, _) =>
        {
            if (c.SelectedItem is not ComboBoxItem { Tag: ThreeWay v } || v == m.BuffShare()) return;
            m.SetBuffWish(v);
            Changed();
            again();
        };
        return c;
    }

    void FillMeter()
    {
        var m = _s.Meter;
        void Again() => Dispatcher.BeginInvoke(FillMeter);
        var p = Refill(_secMeter, AreaMeter, "보스가 받은 피해 중 각자의 몫(딜러 딜지분, 서포터 찬조)을 전분(전투 분석기)이 기록을 시작한 때부터 보여 줍니다. " +
            "파티원 몫은 나와 그 파티원이 모두 '내 피해량 보내기'를 켜야 보입니다. 같은 보스 수치로 나누기 때문에 누구 화면에서나 숫자가 같습니다.");
        p.Children.Add(ShareMark(LookGroup.미터기, FillMeter));
        p.Children.Add(HelpRow("미터기", "끄기: 아무것도 읽지 않습니다 (미터기 창, 전투 기록, 보내기 모두 멈춤). 표시 안 함: 창만 숨기고 전투 기록과 보내기는 계속합니다.",
            EnumCombo([(ThreeWay.끄기, "끄기"), (ThreeWay.표시안함, "표시 안 함"), (ThreeWay.켜기, "켜기")], m.Mode(), v => { m.SetMode(v); Again(); })));
        if (m.Mode() == ThreeWay.켜기)
        {
            var who = BoxKey;
            p.Children.Add(Row("", HideHere(who, ClassProfiles.WindowsOf(_s, who).Meter, v => ClassProfiles.EditWindows(_s, who).Meter = v)));
        }
        p.Children.Add(HelpRow("버프 적용률", "내 피해 중 공증 · 아덴 피증 · 초각스 피증이 켜져 있을 때 넣은 비율입니다(전분의 유효율과는 다른 값). 버프 줄을 읽어야 나옵니다. 표시 안 함: 미터기 창에는 없고 타임라인에만 나옵니다. 끄기: 둘 다 없습니다. 미터기를 끄거나 숨기면 고를 수 있는 값이 줄고, 다시 켜면 전에 고른 값으로 돌아옵니다.",
            BuffShareCombo(m, Again)));
        if (m.BuffShare() == ThreeWay.켜기)   // the bars show only then (OverlayRenderer.BuffBarsShown)
            p.Children.Add(Defaulted(SliderRow("막대 불투명도", MeterSettings.MinOpacity, 1, m.BarOpacity ?? m.Opacity, 0.05, v => $"{v * 100:0}%", v => m.BarOpacity = v, Changed,
                $"버프 적용률 막대 세 개의 불투명도입니다. 미터기 창의 불투명도와 따로 정합니다."),
                s => s.Meter.BarOpacity, (s, v) => s.Meter.BarOpacity = v));
        p.Children.Add(DefaultedCheck(
            Check("전투 기록", m.Record, v => m.Record = v, Changed, "전투를 기록해 설정 → 타임라인에 남깁니다. 개수는 그곳에서 정합니다."), s => s.Meter.Record, (s, v) => s.Meter.Record = v));
        p.Children.Add(DefaultedCheck(Check("내 피해량 보내기", m.Send, v => m.Send = v, () => { Changed(); FillSend(); },
            "켜면 1초마다 내 피해량(서포터는 조력 피해량)과 DPS를 파티원에게 보냅니다. 끄면 파티원의 수치도 못 받습니다. 보내는 사람만 남의 수치를 볼 수 있습니다. 파티 → 보내기·받기에도 같은 스위치가 있습니다."), s => s.Meter.Send, (s, v) => s.Meter.Send = v));
        if (m.Send) p.Children.Add(DefaultedCheck(Check("파티원 피해량 받기", m.Receive, v => m.Receive = v, Changed,
            "끄면 파티원의 수치를 미터기와 타임라인에 쓰지 않고, 내 수치는 계속 보냅니다. 받으려면 내 피해량 보내기도 켜져 있어야 합니다."), s => s.Meter.Receive, (s, v) => s.Meter.Receive = v));
        if (!m.On) return;

        p.Children.Add(SubHeading("보기"));
        p.Children.Add(Defaulted(Row("범위", EnumCombo([(MeterScope.나, "나"), (MeterScope.파티, "파티 (4)"), (MeterScope.공대, "공대 (8)"), (MeterScope.파티별, "1·2파티")], m.Scope,
            v => { m.Scope = v; Again(); })), s => s.Meter.Scope, (s, v) => s.Meter.Scope = v));
        p.Children.Add(Defaulted(Row("수치", EnumCombo([(MeterValue.지분, "딜지분 · 찬조 %"), (MeterValue.피해량, "피해량 (억)"), (MeterValue.DPS, "DPS (억)")], m.Value, v => m.Value = v)),
            s => s.Meter.Value, (s, v) => s.Meter.Value = v));
        p.Children.Add(Defaulted(Row("표시 방식", EnumCombo([(MeterLook.행, "행"), (MeterLook.세로막대, "세로 막대"), (MeterLook.선그래프, "선 그래프"), (MeterLook.한줄막대, "한 줄 막대"),
            (MeterLook.파티비교, "파티 비교 (공대 · 1·2파티)")], m.Look, v => { m.Look = v; Again(); })), s => s.Meter.Look, (s, v) => s.Meter.Look = v));
        p.Children.Add(Defaulted(HelpRow("그래프 범위", "전체: 전분이 기록을 시작한 때부터 그립니다. 최근 1분: 전분 시간으로 마지막 1분만 그리고 지난 기록은 밀려납니다. 그래프만 바뀌고 행의 숫자는 그대로입니다.",
            EnumCombo([(MeterRange.전체, "전체"), (MeterRange.최근1분, "최근 1분")], m.Range, v => m.Range = v)), s => s.Meter.Range, (s, v) => s.Meter.Range = v));
        p.Children.Add(Defaulted(Row("",
            Check("DPS 그래프", m.Graph, v => m.Graph = v, Changed, "행 아래에 시간에 따른 DPS를 그립니다. 혼자여도 내 선이 그려집니다."),
            Check("서포터 · 찬조", m.Support, v => m.Support = v, Changed),
            Check("기타", m.Other, v => m.Other = v, Changed, "보스가 받은 피해 중 보이는 딜러 몫이 아닌 나머지입니다.")),
            s => (s.Meter.Graph, s.Meter.Support, s.Meter.Other), (s, v) => (s.Meter.Graph, s.Meter.Support, s.Meter.Other) = v));
        if (m.ShownLook == MeterLook.파티비교)
            p.Children.Add(DefaultedCheck(Check("% 로 비교", m.CompareAsShare, v => m.CompareAsShare = v, Changed, "끄면 피해량으로 비교합니다."), s => s.Meter.CompareAsShare, (s, v) => s.Meter.CompareAsShare = v));

        MeterSkillRows(p, m, Again);

        p.Children.Add(SubHeading("색과 창"));
        // A null colour is the default (the fallback shown here): ↺ puts the null back.
        p.Children.Add(Defaulted(ColorRow(Label("내 막대"), m.MyColor ?? "#FFC83C", v => m.MyColor = v, swatch: false), s => s.Meter.MyColor, (s, v) => s.Meter.MyColor = v));
        p.Children.Add(Defaulted(ColorRow(Label("딜러"), m.DealerColor ?? "#5B8DEF", v => m.DealerColor = v, swatch: false), s => s.Meter.DealerColor, (s, v) => s.Meter.DealerColor = v));
        p.Children.Add(Defaulted(ColorRow(Label("서포터"), m.SupportColor ?? "#5BD18B", v => m.SupportColor = v, swatch: false), s => s.Meter.SupportColor, (s, v) => s.Meter.SupportColor = v));
        p.Children.Add(Defaulted(ColorRow(Label("기타"), m.OtherColor ?? "#6B7280", v => m.OtherColor = v, swatch: false), s => s.Meter.OtherColor, (s, v) => s.Meter.OtherColor = v));
        p.Children.Add(Defaulted(ColorRow(Label("배경"), m.Background, v => m.Background = v, swatch: false), s => s.Meter.Background, (s, v) => s.Meter.Background = v));
        p.Children.Add(Defaulted(SliderRow("불투명도", 0.1, 1, m.Opacity, 0.05, v => $"{v * 100:0}%", v => m.Opacity = v, Changed), s => s.Meter.Opacity, (s, v) => s.Meter.Opacity = v));
        p.Children.Add(Defaulted(SliderRow("크기", ClassProfiles.MinScale, ClassProfiles.MaxScale, m.Width, 0.05, v => $"{v * 100:0}%", v => (m.Width, m.Height) = (v, v), Changed),
            s => (s.Meter.Width, s.Meter.Height), (s, v) => (s.Meter.Width, s.Meter.Height) = v));

        p.Children.Add(SubHeading("읽기"));
        p.Children.Add(Defaulted(SliderRow("보스 체력 읽기 주기", MeterSettings.MinHpEvery, MeterSettings.MaxHpEvery, m.HpEvery, 0.1, v => $"{v:0.0}초",
            v => m.HpEvery = MeterSettings.ClampHpEvery(v), Changed,
            "보스 체력을 몇 초마다 읽을지 정합니다. 짧을수록 보스 체력 바가 사라지거나 보스가 처치된 순간을 빨리 알아채지만 CPU를 조금 더 씁니다. 전분은 그대로 1초마다 읽습니다. 대난투 중에는 0.2초보다 느려지지 않습니다. 기본 0.5초."),
            s => s.Meter.HpEvery, (s, v) => s.Meter.HpEvery = v));

        p.Children.Add(SubHeading("전분 위치", "전분 창이 화면 어디에 있는지입니다. 미터기가 켜져 있으면 전투 중에 알아서 찾아 게임 화면 크기마다 저장하고, 전분 창을 옮기면 다시 찾습니다. 위치 조정에서 끌어 옮기거나 직접 넣어도 됩니다. 전분 창을 닫으면 미터기에 '전분 없음'이 보입니다."));
        if (GameArea?.Invoke() is not { } key)
        {
            p.Children.Add(Note("스킬창을 먼저 맞춰 주세요. 전분 위치는 게임 화면 크기마다 저장됩니다."));
            return;
        }
        var r = m.Panels.TryGetValue(key, out var saved) ? saved : new PixelRect(0, 0, 0, 0);
        TextBox Box(int v) => new() { Text = v.ToString(), Width = 60, Margin = new(0, 0, 6, 0) };
        var (x, y, w, h) = (Box(r.X), Box(r.Y), Box(r.Width), Box(r.Height));
        var save = new Button { Content = "저장" };
        save.Click += (_, _) =>
        {
            if (int.TryParse(x.Text, out int a) && int.TryParse(y.Text, out int b) && int.TryParse(w.Text, out int c) && int.TryParse(h.Text, out int d) && c > 0 && d > 0)
                m.Panels[key] = new(a, b, c, d);
            else m.Panels.Remove(key);
            Changed();
            Again();
        };
        p.Children.Add(Row($"{key} 화면", Label("x"), x, Label("y"), y, Label("너비"), w, Label("높이"), h, save));
    }

    /// <summary>스킬 사용 횟수 for the 편집 중 pick: the skill by its key, its name on the meter (empty: the key), what it counts over.</summary>
    void MeterSkillRows(Panel p, MeterSettings m, Action again)
    {
        var who = BoxKey;
        var w = ClassProfiles.WindowsOf(_s, who);
        p.Children.Add(SubHeading("스킬 사용 횟수", "고른 스킬을 1분에 몇 번 썼는지 미터기 창에 보여 줍니다. 클래스 · 직업각인마다 따로 고릅니다(위의 편집 중). 타임라인은 이 스킬에 금색 테두리를 두르고 평균을 보여 줍니다. 내 것만 셉니다."));
        var skill = Combo();
        skill.MinWidth = 120;
        skill.ItemsSource = SkillChoices(SlotKeyName);
        skill.SelectedItem = skill.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (SlotId?)i.Tag == w.MeterSkill) ?? skill.Items[0];   // a hand edit can name a slot not offered
        skill.SelectionChanged += (_, _) =>
        {
            if (skill.SelectedItem is not ComboBoxItem item) return;
            ClassProfiles.EditWindows(_s, who).SetMeterSkill((SlotId?)item.Tag);
            Changed();
            again();
        };
        p.Children.Add(Row("스킬", skill));
        if (w.MeterSkill is null) return;
        var name = new TextBox { Text = w.MeterSkillName ?? "", Width = 140, MaxLength = MeterSkillNameMax, ToolTip = "비워 두면 키 이름으로 보입니다." };
        name.LostFocus += (_, _) =>
        {
            string? text = string.IsNullOrWhiteSpace(name.Text) ? null : name.Text.Trim();
            if (text == ClassProfiles.WindowsOf(_s, who).MeterSkillName) return;
            ClassProfiles.EditWindows(_s, who).MeterSkillName = text;
            Changed();
        };
        p.Children.Add(Row("표시 이름", name));
        p.Children.Add(DefaultedCheck(Check("그래프 범위 따라 세기", m.SkillFollowsRange, v => m.SkillFollowsRange = v, Changed, "켜면 그래프가 최근 1분일 때 마지막 1분 동안 쓴 횟수를 보여 줍니다. 끄면 항상 전투 전체 평균입니다."), s => s.Meter.SkillFollowsRange, (s, v) => s.Meter.SkillFollowsRange = v));
        if (!_s.Overlay.SkillRead) p.Children.Add(Note("스킬창 읽기가 꺼져 있어 횟수를 셀 수 없습니다. 미터기 창에는 '—'로 보입니다."));
    }

    const int MeterSkillNameMax = 12;

    /// <summary>없음 and each skill slot (아덴 slots aren't skills), named by <paramref name="name"/>.</summary>
    static List<ComboBoxItem> SkillChoices(Func<SlotId, string> name, IEnumerable<SlotId>? only = null) =>
        [new() { Content = "없음", Tag = null }, .. (only ?? SlotInfo.All).Where(s => FightSkills.KindOf(s) is not null).Select(s => new ComboBoxItem { Content = name(s), Tag = s })];

    /// <summary>파티 → 순서, host only: each member's party.</summary>
    void PartyAssign(StackPanel p)
    {
        if (!_partyHost || _partyMembers.Count < 2) return;
        p.Children.Add(SubHeading("1·2파티", "공대(8명)에서 파티원 창에는 내 파티만 보이고, DPS 미터기는 1·2파티를 나눠 보여 줍니다. 한 파티는 4명까지입니다."));
        foreach (var member in _partyMembers)
        {
            var c = Combo();
            c.MinWidth = 90;
            c.ItemsSource = new[] { 1, 2 }.Select(n => new ComboBoxItem { Content = $"{n}파티", Tag = n }).ToList();
            c.SelectedItem = c.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == member.Party);
            c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: int n } && n != member.Party && PartyMove?.Invoke(member.Id, n) == false) Dispatcher.BeginInvoke(FillOrder); };
            p.Children.Add(Row(member.Name, c));
        }
    }
}
