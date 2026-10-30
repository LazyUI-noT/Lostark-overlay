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

    /// <summary>The host puts a member in 1파티 or 2파티 (id, party); false: refused (that party is full).</summary>
    public Func<ulong, int, bool>? PartyMove { get; set; }

    /// <summary>The session's members and whether I host (파티 → 순서's 1·2파티 picks).</summary>
    public void SetPartyMembers(IReadOnlyList<PartyMember> members, bool host)
    {
        if (host == _partyHost && members.SequenceEqual(_partyMembers)) return;
        (_partyMembers, _partyHost) = ([.. members], host);
        FillOrder();
    }

    void FillMeter()
    {
        var m = _s.Meter;
        void Again() => Dispatcher.BeginInvoke(FillMeter);
        var p = Refill(_secMeter, AreaMeter, "보스가 받은 피해 중 각자의 몫(딜러 딜지분, 서포터 찬조)을 전분(전투분석기)이 기록을 시작한 때부터 보여 줍니다. " +
            "파티원 몫은 나와 그 파티원이 모두 '내 피해량 보내기'를 켰을 때만 보입니다. 모두 같은 보스 수치로 나누므로 누구에게나 같은 숫자가 보입니다.");
        p.Children.Add(Row("", Check("켜기", m.On, v => m.On = v, () => { Changed(); Again(); }, "끄면 아무것도 읽지 않습니다 (미터기 창, 전투 기록, 보내기 모두).")));
        p.Children.Add(Row("",
            Check("미터기 창 보이기", m.Show, v => m.Show = v, Changed, "끄면 창만 숨기고, 전투 기록과 보내기는 계속합니다."),
            Check("전투 기록", m.Record, v => m.Record = v, Changed, "전투를 기록해 설정 → 전투 기록에 남깁니다. 개수는 그곳에서 정합니다.")));
        p.Children.Add(Row("", Check("내 피해량 보내기", m.Send, v => m.Send = v, () => { Changed(); FillSend(); },
            "켜면 1초마다 내 피해량(서포터는 조력 피해량)과 DPS를 파티원에게 보냅니다. 끄면 다른 파티원의 수치도 받지 않습니다(보내는 사람만 남의 수치를 봅니다). 파티 → 보내기·받기에도 같은 스위치가 있습니다.")));
        if (!m.On) return;

        p.Children.Add(SubHeading("보기"));
        p.Children.Add(Row("범위", EnumCombo([(MeterScope.나, "나"), (MeterScope.파티, "파티 (4)"), (MeterScope.공대, "공대 (8)"), (MeterScope.파티별, "1·2파티")], m.Scope,
            v => { m.Scope = v; Again(); })));
        p.Children.Add(Row("수치", EnumCombo([(MeterValue.지분, "딜지분 · 찬조 %"), (MeterValue.피해량, "피해량 (억)"), (MeterValue.DPS, "DPS (억)")], m.Value, v => m.Value = v)));
        p.Children.Add(Row("표시 방식", EnumCombo([(MeterLook.행, "행"), (MeterLook.세로막대, "세로 막대"), (MeterLook.선그래프, "선 그래프"), (MeterLook.한줄막대, "한 줄 막대"),
            (MeterLook.파티비교, "파티 비교 (공대 · 1·2파티)")], m.Look, v => { m.Look = v; Again(); })));
        p.Children.Add(Row("",
            Check("DPS 그래프", m.Graph, v => m.Graph = v, Changed, "행 아래에 시간에 따른 DPS를 그립니다. 혼자여도 내 선이 그려집니다."),
            Check("버프 적용률", m.BuffBars, v => m.BuffBars = v, Changed, "내 피해 중 공증 · 아덴 피증 · 초각스 피증이 켜져 있을 때 넣은 비율입니다. 설정 → 버프가 켜져 있어야 합니다."),
            Check("서포터 · 찬조", m.Support, v => m.Support = v, Changed),
            Check("기타", m.Other, v => m.Other = v, Changed, "보스가 받은 피해 중 보이는 딜러 몫이 아닌 나머지입니다.")));
        if (m.ShownLook == MeterLook.파티비교)
            p.Children.Add(Row("", Check("% 로 비교", m.CompareAsShare, v => m.CompareAsShare = v, Changed, "끄면 피해량으로 비교합니다.")));

        p.Children.Add(SubHeading("색과 창"));
        p.Children.Add(ColorRow(Label("내 막대"), m.MyColor ?? "#FFC83C", v => m.MyColor = v, swatch: false));
        p.Children.Add(ColorRow(Label("딜러"), m.DealerColor ?? "#5B8DEF", v => m.DealerColor = v, swatch: false));
        p.Children.Add(ColorRow(Label("서포터"), m.SupportColor ?? "#5BD18B", v => m.SupportColor = v, swatch: false));
        p.Children.Add(ColorRow(Label("기타"), m.OtherColor ?? "#6B7280", v => m.OtherColor = v, swatch: false));
        p.Children.Add(ColorRow(Label("배경"), m.Background, v => m.Background = v, swatch: false));
        p.Children.Add(SliderRow("불투명도", 0.1, 1, m.Opacity, 0.05, v => $"{v * 100:0}%", v => m.Opacity = v, Changed));
        p.Children.Add(SliderRow("크기", ClassProfiles.MinScale, ClassProfiles.MaxScale, m.Width, 0.05, v => $"{v * 100:0}%", v => (m.Width, m.Height) = (v, v), Changed));

        p.Children.Add(SubHeading("전분 위치", "전분 창의 화면 위치입니다. 미터기가 켜져 있으면 전투 중에 전분 창을 스스로 찾아 게임 화면 크기마다 저장하고, 옮기면 다시 찾습니다. 직접 넣을 수도 있습니다. 전분 창을 닫으면 미터기에 '전분 없음'이 보입니다."));
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
