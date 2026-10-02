using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>파티: 연결, 파티원 모양 (the class picked's 파티원 part and how members are labelled), 순서, 보내기·받기.</summary>
public partial class SettingsWindow
{
    int PeerSlots => _s.Party.ShowOthersBySlot.Count;   // party windows (1, 2, 3)

    void FillParty()
    {
        FillConnect();
        FillMembers();
        FillOrder();
        FillSend();
    }

    void FillConnect()
    {
        var party = _s.Party;
        var p = Refill(_secConnect, "세션", "호스트 1명과 최대 3명이 참가할 수 있고, 토큰으로 연결합니다. 호스트는 공유기에서 이 UDP 포트를 열어 두어야 합니다.");
        bool inSession = _partyStatus is not null;
        bool connecting = _partyStatus is "연결 중…" or "승인 기다리는 중…" or "다시 연결 중…";
        p.Children.Add(Row("상태", Label(_partyStatus ?? "연결 안 됨")));
        var port = new TextBox { Text = party.HostPort.ToString(), Width = 90, IsEnabled = !inSession };
        var portWarn = new TextBlock { Text = "포트는 1–65535 사이의 숫자여야 합니다", Visibility = Visibility.Collapsed };
        portWarn.SetResourceReference(TextBlock.ForegroundProperty, CautionBrushKey);
        port.TextChanged += (_, _) =>
        {
            bool ok = int.TryParse(port.Text, out var v) && v is > 0 and < 65536;
            portWarn.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            if (ok) { party.HostPort = v; Changed(); }
        };
        var host = new Button { Content = connecting ? "연결 중…" : "호스트 시작", IsEnabled = !inSession };
        host.Click += (_, _) => HostRequested?.Invoke();
        p.Children.Add(Row("호스트 포트", Stack(Wrap(port, host), portWarn)));
        // The pasted token outlives the rebuilds a status change makes, so a failed join can be retried as is.
        var token = new PasswordBox { Width = 240, IsEnabled = !inSession, Password = _joinToken };
        var join = new Button { Content = connecting ? "연결 중…" : "참가", IsEnabled = !inSession && _joinToken.Trim().Length > 0 };
        token.PasswordChanged += (_, _) =>
        {
            _joinToken = token.Password;
            join.IsEnabled = !inSession && _joinToken.Trim().Length > 0;
        };
        join.Click += (_, _) => { if (_joinToken.Trim().Length > 0) JoinRequested?.Invoke(_joinToken.Trim()); };
        p.Children.Add(Row("참가 토큰", token, join));
        if (inSession)
        {
            var leave = new Button { Content = "나가기" };
            leave.Click += (_, _) => LeaveRequested?.Invoke();
            var copy = new Button { Content = "토큰 복사", IsEnabled = _hasToken };
            copy.Click += (_, _) => CopyTokenRequested?.Invoke();
            p.Children.Add(Row("", copy, leave));
        }
        if (_partyNotice is { } notice) p.Children.Add(new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 8) });

        AdmissionRows(p);

        p.Children.Add(Heading("내 정보"));
        var name = new TextBox { Text = PlayerName.Clip(party.DisplayName), Width = 180 };
        // Saved cut to the limit as typed; the box itself is cut only on leaving it, since rewriting its text during
        // Korean IME composition breaks the syllable being composed.
        name.TextChanged += (_, _) => { party.DisplayName = PlayerName.Clip(name.Text); Changed(); };
        name.LostKeyboardFocus += (_, _) => name.Text = party.DisplayName;
        p.Children.Add(Row(Described("표시 이름", "한글 7자, 영문 14자까지. 파티원 화면에서 클래스 아이콘 아래에 보입니다."), name));
        p.Children.Add(ResetButton("파티 설정 되돌리기", "파티의 모든 설정(표시, 순서, 역할 색, 보내기·받기, 포트)을 되돌립니다. 표시 이름과 파티원 창의 위치는 남습니다.",
            () => SettingsResets.Party(_s)));
    }

    /// <summary>참가 요청 승인, 신뢰한 기기 (삭제), and while hosting, 차단한 기기 (다시 허용).</summary>
    void AdmissionRows(Panel p)
    {
        var party = _s.Party;
        p.Children.Add(Heading("참가 요청"));
        p.Children.Add(Check("참가 요청 승인", party.ApproveJoins, v => party.ApproveJoins = v, Changed));
        p.Children.Add(Note("켜 두면 호스트일 때 처음 보는 기기가 참가하려 하면 화면 오른쪽 아래에 허용·거절 창이 뜹니다. 허용한 기기는 아래 신뢰한 기기에 남아 다음부터는 묻지 않습니다. 끄면 토큰이 있는 사람은 누구나 참가합니다."));
        if (party.TrustedDevices.Count == 0) p.Children.Add(Row("신뢰한 기기", Label("없음", 0.7)));
        foreach (var trusted in party.TrustedDevices.ToList())
        {
            var remove = new Button { Content = "삭제", ToolTip = "이 기기가 다음에 참가할 때 다시 묻습니다." };
            remove.Click += (_, _) =>
            {
                party.TrustedDevices.RemoveAll(t => t.Device == trusted.Device);
                Changed();
                Dispatcher.BeginInvoke(FillConnect);
            };
            p.Children.Add(Row("신뢰한 기기", Label($"{trusted.Name} ({trusted.Device[..8]})"), remove));
        }
        if (!_isHost) return;
        foreach (var (device, name) in _partyBlocked)
        {
            var allow = new Button { Content = "다시 허용", ToolTip = "이 세션에 다시 참가할 수 있게 합니다." };
            allow.Click += (_, _) => UnblockRequested?.Invoke(device);
            p.Children.Add(Row("내보낸 기기", Label($"{name} ({device[..8]})"), allow));
        }
    }

    /// <summary>파티원 모양: the class picked's 파티원 boxes, then what every member shows beside them.</summary>
    void FillMembers()
    {
        var party = _s.Party;
        var p = Refill(_secMembers, $"파티원 ({ClassName(ShownClass)})");
        MembersPart(p, ClassProfiles.Of(_s, ShownClass), () => ClassProfiles.Edit(_s, ShownClass));

        p.Children.Add(Heading("모든 파티원"));
        p.Children.Add(Row("칸 왼쪽", EnumCombo([(PeerLabel.IconAndName, "클래스 아이콘 + 이름"), (PeerLabel.Name, "이름"), (PeerLabel.Icon, "클래스 아이콘")],
            party.Label, v => party.Label = v)));
        p.Children.Add(Note("클래스를 고르지 않은 파티원은 아이콘 자리에 '?'가 표시됩니다."));
        p.Children.Add(SliderRow("아이콘 크기", 0.5, 2, party.IconScale, 0.1, v => $"{v * 100:0}%", v => party.IconScale = v, Changed));
        p.Children.Add(SliderRow("이름 길이", PlayerName.MinUnits, PlayerName.MaxUnits, party.NameLength, 1,
            v => $"{v:0}", v => party.NameLength = (int)v, Changed));
        p.Children.Add(Note("파티원 이름을 내 화면에서 이 길이까지만 보여 줍니다. 영문은 1, 한글은 2로 셉니다 (14 = 한글 7자, 6 = 한글 3자)."));

        p.Children.Add(Heading("기본 역할 색"));
        p.Children.Add(Note("파티원 색을 '기본'으로 둔 클래스는 일반스킬 칸을 역할 색으로 그립니다. 클래스마다 정하지 않아도 딜러와 서포터를 한눈에 구분할 수 있습니다. 다른 그룹은 기본 색입니다."));
        foreach (var role in Enum.GetValues<Role>())
            p.Children.Add(ColorRow(Label(role.ToString()), party.RoleColor.GetValueOrDefault(role, "#808080"),
                v => party.RoleColor[role] = v, swatch: true, warnGold: true));
    }

    void FillOrder()
    {
        var party = _s.Party;
        var p = Refill(_secOrder, "파티원 순서", "번호는 파티원 창 왼쪽에 보입니다. 게임에서 내가 몇 번인지 고르면 파티원은 나머지 번호를 씁니다. 바꾼 순서는 그 이름에 고정되어, 다시 참가해도 같은 자리에 옵니다.");
        int mine = party.MyNumber;
        p.Children.Add(Row("내 번호", NumberCombo([1, 2, 3, 4], mine, v =>
        {
            party.MyNumber = v;
            if (party.SupportNumber == v) party.SupportNumber = PartySlots.Numbers(v)[0];
            Dispatcher.BeginInvoke(FillOrder);
        })));
        for (int i = 0; i < PeerSlots; i++)
        {
            string? who = i < _partyOrder.Length ? _partyOrder[i] : null;
            var cells = new List<UIElement> { Label(who ?? "(비어 있음)", 0.7) };
            if (_isHost && who is not null)
            {
                var kick = new Button { Content = "내보내기", ToolTip = "이 파티원을 내보냅니다. 이 세션에는 다시 참가할 수 없고(연결 → 다시 허용으로 풀 수 있음), 신뢰한 기기에서도 빠집니다." };
                kick.Click += (_, _) => KickRequested?.Invoke(who);
                cells.Add(kick);
            }
            p.Children.Add(Row($"{PartySlots.Label(i, mine)}번", [.. cells]));
        }
        var swaps = new List<UIElement>();
        for (int i = 0; i + 1 < PeerSlots; i++)
        {
            int a = i;
            var swap = new Button { Content = $"{PartySlots.Label(a, mine)} ↔ {PartySlots.Label(a + 1, mine)}" };
            swap.Click += (_, _) =>
            {
                // Pins everyone where they are now, then swaps the two, so nobody else moves.
                var now = Enumerable.Range(0, PeerSlots).Select(k => k < _partyOrder.Length ? _partyOrder[k] : null).ToList();
                party.SlotNames = PartySlots.Swap(now, a, a + 1);
                Changed();
            };
            swaps.Add(swap);
        }
        var unpin = new Button { Content = "참가 순서로", ToolTip = "고정한 순서를 지우고 참가한 순서대로 둡니다." };
        unpin.Click += (_, _) => { party.SlotNames = []; Changed(); };
        swaps.Add(unpin);
        p.Children.Add(Row("바꾸기", [.. swaps]));
        bool iAmSupport = _chars.Role == Role.서포터;
        var supportNumber = NumberCombo(PartySlots.Numbers(mine), party.SupportNumber, v => party.SupportNumber = v);
        var fixSupport = Check("서포터 고정", party.SupportFixed, v => { party.SupportFixed = v; supportNumber.Visibility = Shown(v); }, Changed);
        supportNumber.Visibility = Shown(party.SupportFixed);
        fixSupport.IsEnabled = supportNumber.IsEnabled = !iAmSupport;
        p.Children.Add(Row("서포터", fixSupport, supportNumber));
        p.Children.Add(Note(iAmSupport ? "내가 서포터라서 서포터 고정은 쓰지 않습니다." : "켜면 첫 서포터가 고른 번호에 옵니다."));
        p.Children.Add(Note("파티원마다 창이 따로 있습니다. 편집 모드에서 각 창을 끌어 옮기고, 오른쪽 아래 모서리를 끌어 크기를 바꿉니다."));
    }

    void FillSend()
    {
        var party = _s.Party;
        var p = Refill(_secSend, "파티원에게 보내기", "설정은 바꾸는 즉시 저장되지만, 파티원에게는 참가할 때 한 번 가고 그 뒤에는 업데이트를 누를 때만 갑니다.");
        var update = new Button { Content = "업데이트", IsEnabled = InParty,
            ToolTip = "지금 쓰는 색, 칸 크기, 칸 배치를 파티원에게 다시 보냅니다." };
        update.Click += (_, _) => LookUpdateRequested?.Invoke();
        p.Children.Add(Row(Described("내 모양 보내기", InParty ? "색, 칸 크기, 칸 배치" : "파티에 있을 때 쓸 수 있습니다"), update));
        var send = new List<UIElement>();
        foreach (var g in Enum.GetValues<SlotGroup>())
            send.Add(Check(g.ToString(), party.Send.GetValueOrDefault(g, true), v => party.Send[g] = v, Changed));
        send.Add(Check("아덴 게이지", party.SendGauge, v => party.SendGauge = v, Changed));
        p.Children.Add(Row(Described("보낼 그룹", "끈 그룹은 파티원에게 보내지 않습니다"), [.. send]));

        p.Children.Add(Heading("파티원에게서 받기"));
        p.Children.Add(Note("색은 파티 → 파티원 모양에서 '파티원 설정 받기'를 고른 클래스에 씁니다. 크기와 배치는 아래를 켤 때만 쓰고, 내가 정한 파티원 칸 크기 안에 비율을 지켜 맞추므로 파티원 창이 커지지 않습니다."));
        p.Children.Add(Check("파티원 크기 받기", party.ReceiveSize, v => party.ReceiveSize = v, Changed));
        p.Children.Add(Check("파티원 배치 받기", party.ReceiveLayout, v => party.ReceiveLayout = v, Changed));
    }

    /// <summary>A combo box over party numbers (1–4).</summary>
    ComboBox NumberCombo(IEnumerable<int> numbers, int value, Action<int> set)
    {
        var c = Combo();
        c.MinWidth = 70;
        c.ItemsSource = numbers.Select(n => new ComboBoxItem { Content = $"{n}번", Tag = n }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == value) ?? c.Items[0];
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: int v }) { set(v); Changed(); } };
        return c;
    }
}
