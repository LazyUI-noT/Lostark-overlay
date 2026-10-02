using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Forms = System.Windows.Forms;

namespace GaugeOverlay.App.UI;

/// <summary>설정: four areas of sections (<see cref="BuildShell"/>). Every change edits the <see cref="Settings"/> object at
/// once and raises <see cref="SettingsChanged"/> (no Save button; saving is debounced by the caller). Closing only hides it.</summary>
public partial class SettingsWindow : Window
{
    readonly Settings _s;
    readonly CharacterService _chars;
    readonly bool _inEditor;   // the 레이아웃 편집기's hidden instance: only BoxTab and GaugeBox are shown, by the editor
    string? _lastActive;

    /// <summary>Raised after every edit (and after a reset).</summary>
    public event Action? SettingsChanged;

    /// <summary>캐릭터·인식 → 스킬바 → "스킬바 다시 맞추기".</summary>
    public event Action? RefitRequested;

    /// <summary>내 오버레이 → 배치 → "편집 모드".</summary>
    public event Action? EditModeRequested;

    /// <summary>레이아웃 편집기 열기.</summary>
    public event Action? LayoutEditorRequested;

    /// <summary>파티 → "호스트 시작" / "참가" (with the pasted token) / "나가기" / "토큰 복사".</summary>
    public event Action? HostRequested, LeaveRequested, CopyTokenRequested;
    public event Action<string>? JoinRequested;

    /// <summary>파티 → 보내기·받기 → "업데이트": send your colours, box size and layout to the party again.</summary>
    public event Action? LookUpdateRequested;

    /// <summary>파티 → "내보내기" (host only), with the member's name.</summary>
    public event Action<string>? KickRequested;

    /// <summary>배운 값 → 이 캐릭터의 배운 값 지우기 (스킬별 예측).</summary>
    public event Action? GainsResetRequested;

    /// <summary>캐릭터·인식 → 스킬바 → "스킬 등록" / "등록된 스킬 지우기".</summary>
    public event Action? SkillsSubmitRequested, SkillsClearRequested;

    /// <summary>캐릭터·인식 → 캐릭터 → "직업각인 다시 감지".</summary>
    public event Action? SubclassRedetectRequested;

    /// <summary>내 오버레이 → 스킬 칸 → "아이콘 다시 읽기".</summary>
    public event Action? IconRereadRequested;

    string? _partyStatus, _partyNotice, _fitResult, _skillsResult;
    string _joinToken = "";   // 참가 토큰 as typed; kept in memory only, never saved
    bool _hasToken, _isHost;
    string?[] _partyOrder = [];                      // who is in each party slot now

    /// <summary>Who is in each party slot (null = nobody), for 파티원 순서. Rebuilds it only when it changed.</summary>
    public void SetPartyOrder(IReadOnlyList<string?> names)
    {
        if (names.SequenceEqual(_partyOrder)) return;
        _partyOrder = [.. names];
        FillOrder();
    }

    bool InParty => _partyStatus is not null and not "연결 중…";

    /// <summary>Shows the party session state (null status = none) and, when given, the latest one-off message
    /// (host/join failure or success) so it's visible here even if the tray balloon was suppressed. "토큰 복사"
    /// is enabled while there is a token.</summary>
    public void SetPartyState(string? status, string? token, string? notice = null, bool isHost = false)
    {
        (_partyStatus, _hasToken, _isHost) = (status, token is not null, isHost && status is not null);
        if (notice is not null) _partyNotice = notice;
        if (InParty) _joinToken = "";   // kept only to retry a failed join: it holds the session secret, so not on screen after
        FillParty();
    }

    /// <summary>Host: devices kicked this session (차단한 기기), each with 다시 허용.</summary>
    public void SetPartyBlocked(IReadOnlyList<(string Device, string Name)> blocked)
    {
        _partyBlocked = blocked;
        FillConnect();
    }
    IReadOnlyList<(string Device, string Name)> _partyBlocked = [];

    /// <summary>다시 허용 was pressed for this device.</summary>
    public event Action<string>? UnblockRequested;

    /// <summary>Shows the last fit result in 캐릭터·인식 → 스킬바 ("맞춤 완료: …" or the error).</summary>
    public void SetFitResult(string text)
    {
        _fitResult = text;
        FillFit();
    }

    public void SetSkillsResult(string text)
    {
        _skillsResult = text;
        FillFit();
    }

    /// <param name="quickTab">The window as 설정, with its preview; off for the 레이아웃 편집기, which hosts
    /// <see cref="BoxTab"/> and <see cref="GaugeBox"/> itself.</param>
    public SettingsWindow(Settings settings, CharacterService characters, bool quickTab = true)
    {
        _s = settings;
        _chars = characters;
        _inEditor = !quickTab;
        InitializeComponent();
        BuildShell();
        if (quickTab)
        {
            BuildQuick();
            _secSkills.Children.Add(_chips);
            _secSkills.Children.Add(BoxTab);
            _secSkills.Children.Add(_skillsClass);
            GaugeBox.Visibility = Visibility.Visible;
            _secGauge.Children.Add(GaugeBox);
        }
        BuildAll();
        ShowArea();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    void Changed()
    {
        QuickRefresh();
        SettingsChanged?.Invoke();
    }

    /// <summary>Rebuilds every section from the settings object (after it was edited elsewhere, e.g. first run or the tray).</summary>
    public void Reload() => BuildAll();

    void BuildAll()
    {
        string? active = ClassProfiles.ActiveClass(_s);
        if (active != _lastActive) (_classShown, _lastActive) = (null, active);   // a new character: edit its class
        FillCharacter();
        FillFit();
        FillLearned();
        FillCheck();
        FillDisplay();
        FillGauge();
        FillConnect();
        FillOrder();
        FillSend();   // 파티원 모양 comes with the class sections
        FillKeys();
        FillHotkeys();
        FillClassSections();
        FillChips();
        FillBox();
        QuickSample();
        QuickRefresh();   // the buff and debuff windows as the settings now say (a raid picked in the tray)
    }

    /// <summary>Clears a section and starts it with its title and what it is for.</summary>
    static StackPanel Refill(StackPanel p, string title, string? lead = null)
    {
        p.Children.Clear();
        p.Children.Add(Heading(title, first: true));
        if (lead is not null) p.Children.Add(Note(lead));
        return p;
    }

    // ── 캐릭터·인식 ───────────────────────────────────────────

    void FillCharacter()
    {
        var p = Refill(_secCharacter, "캐릭터");
        p.Children.Add(BuildCharacterPanel(_s, _chars, () => { Changed(); Dispatcher.BeginInvoke(BuildAll); }));
        p.Children.Add(Note("트레이 메뉴에서 고른 캐릭터와 같습니다. 클래스마다 마지막으로 고르거나 읽은 직업각인을 기록해 두고, 스킬바를 다시 맞출 때 그 클래스면 직업각인을 읽지 않고 기록대로 고릅니다. 기록이 없을 때만 게임 화면에서 직업각인을 읽습니다."));
        if (_chars.ActiveClass is { } cls)
            p.Children.Add(Label(_s.SubclassHistory.TryGetValue(cls.Name, out var sub) ? $"기록된 직업각인: {cls.Name} → {sub}" : $"기록된 직업각인: {cls.Name} → 없음", 0.8));
        var redetect = new Button { Content = "직업각인 다시 감지", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        redetect.Click += (_, _) => SubclassRedetectRequested?.Invoke();
        redetect.IsEnabled = _chars.ActiveClass is not null && _chars.SameSkillsNote is null;
        p.Children.Add(redetect);
        p.Children.Add(Note(_chars.SameSkillsNote ?? "기록을 무시하고 초각스가 준비됐을 때 직업각인을 다시 읽어 기록을 고칩니다. 알아보지 못하면 기록은 그대로 둡니다."));
    }

    void FillFit()
    {
        var p = Refill(_secFit, "스킬바 맞추기", "수련장에서 재사용 대기시간을 초기화한 뒤 누르세요. 해상도나 게임의 HUD 크기를 바꾼 뒤에도 다시 맞춥니다. 맞추면서 클래스도 다시 확인합니다. 직업각인은 기록이 있으면 기록대로 고릅니다.");
        if (_s.Fits.Count == 0) p.Children.Add(Label("아직 맞춘 적이 없습니다.", 0.7));
        foreach (var (size, fit) in _s.Fits)
            p.Children.Add(Row(size, Label($"HUD {fit.Hud:0.#}%")));
        p.Children.Add(Check("직접 입력 (HUD · 버프 · 해상도 · 21:9)", _s.ManualHud is not null,
            v => { if (v) _s.StartManual(); else (_s.ManualHud, _s.ManualScreen) = (null, null); }, () => { FillFit(); Changed(); }));
        p.Children.Add(Note("맞추기는 화면의 검은 띠와 HP 바로 해상도와 HUD 크기를 찾습니다. 켜면 찾지 않고 게임 설정에 적힌 값을 그대로 써서, 스킬바 자리만 확인한 뒤 클래스와 직업각인을 읽습니다. 처음 값은 마지막으로 찾은 값입니다."));
        if (_s.ManualHud is { } hud)
        {
            p.Children.Add(SliderRow("HUD 크기 (%)", 70, 130, hud, 1, v => $"{v:0}%", v => _s.ManualHud = v, Changed));
            p.Children.Add(Row("게임의 버프 크기 (%)", BuffSizeCombo()));
            ScreenRows(p);
            var auto = new Button { Content = "자동으로 맞추기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
            auto.Click += (_, _) => { (_s.ManualHud, _s.ManualScreen) = (null, null); FillFit(); Changed(); RefitRequested?.Invoke(); };
            p.Children.Add(auto);
            p.Children.Add(Note("직접 입력한 값으로 스킬바를 찾지 못하면 직접 입력을 끄고 해상도와 HUD 크기를 찾아 맞춥니다."));
        }
        var b = new Button { Content = "스킬바 다시 맞추기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        b.Click += (_, _) => RefitRequested?.Invoke();
        p.Children.Add(b);
        if (_fitResult is { } result) p.Children.Add(new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        p.Children.Add(SubHeading("스킬 등록"));
        p.Children.Add(Note("마을처럼 스킬을 못 쓰는 곳에서는 스킬바가 회색이 되어 직업각인을 읽기 어렵습니다. 스킬을 쓸 수 있는 곳에서 일반 스킬이 모두 준비됐을 때 등록해 두면, 회색 스킬바에서도 일반 스킬 8개 중 5개 이상이 맞는 직업각인을 고릅니다. 스킬 위치는 상관없습니다. 지금 고른 직업각인으로 저장되며, 다른 스킬 구성은 추가로 등록할 수 있습니다. 초각스 아이콘으로 읽지 못할 때 쓰입니다. 등록하지 않은 직업각인은 스킬로 고르지 않고, 두 직업각인이 같이 쓰는 스킬은 빼고 나머지로 비교합니다."));
        var submit = new Button { Content = "스킬 등록", Margin = new(0, 0, 8, 0) };
        submit.Click += (_, _) => SkillsSubmitRequested?.Invoke();
        submit.IsEnabled = _chars.SameSkillsNote is null;
        var clear = new Button { Content = "등록된 스킬 지우기" };
        clear.Click += (_, _) => SkillsClearRequested?.Invoke();
        p.Children.Add(new WrapPanel { Children = { submit, clear } });
        if (_chars.SameSkillsNote is not null) p.Children.Add(Note($"{_chars.ActiveClass!.Name}: 두 직업각인의 스킬이 같아 스킬로 고를 수 없으므로 등록하지 않습니다."));
        if (_skillsResult is { } skills) p.Children.Add(new TextBlock { Text = skills, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        p.Children.Add(ResetButton("스킬바 맞춤 지우기", "저장된 스킬바 맞춤을 모두 지웁니다. 다음 실행 때 다시 맞춰야 합니다.", () => SettingsResets.Fits(_s)));
    }

    /// <summary>What the app learned for the character: for 스킬별 예측, its gauge gains.</summary>
    void FillLearned()
    {
        var p = Refill(_secLearned, "배운 게이지 값");
        p.Children.Add(Note("아덴 게이지의 '스킬별 예측' 읽기에서 스킬마다 게이지가 얼마나 차는지 배운 값입니다. 캐릭터(클래스·직업각인)마다 저장되어 앱을 다시 켜도 이어집니다. 슬롯의 스킬을 바꿨으면 지우세요. 지우지 않아도 30번쯤 쓰면 새 값에 맞춰집니다."));
        var reset = new Button { Content = "이 캐릭터의 배운 값 지우기", HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => GainsResetRequested?.Invoke();
        p.Children.Add(reset);
    }

    void FillCheck()
    {
        var p = Refill(_secCheck, "클래스 확인");
        p.Children.Add(Note(_chars.ClassCheckable
            ? "스킬바 다시 맞추기 때 스킬바 가운데나 초각스 아이콘이 고른 클래스와 다르면 아덴 게이지가 빨갛게 깜빡이고 게이지 예측을 멈춥니다. 캐릭터 감지는 스킬바 다시 맞추기(Ctrl+Shift+D)를 누를 때만 합니다. 로딩이나 컷신 뒤에는 저절로 바뀌지 않습니다. 가만히 있을 때 누르면 가장 정확합니다."
            : ClassProfiles.ActiveClass(_s) is null
                ? "캐릭터를 고르면 그 클래스가 맞는지 게임 화면과 비교합니다."
                : "이 클래스는 비교할 그림(Profiles/aden, Profiles/subclass)이 없어 클래스를 확인하지 못합니다. 고른 클래스가 맞는지 직접 확인하세요."));
    }

    // ── 내 오버레이 → 전체, 버프/디버프 ───────────────────────────────────────────

    void FillDisplay()
    {
        var o = _s.Overlay;
        var p = Refill(_secDisplay, "오버레이 표시");
        p.Children.Add(Check("오버레이 표시", o.Visible, v => o.Visible = v, Changed));
        p.Children.Add(Note("끄면 내 오버레이와 파티원 창을 모두 숨깁니다. 트레이 메뉴나 '오버레이 표시/숨기기' 단축키로도 바꿀 수 있습니다. 이 설정 창이 열려 있는 동안에는 미리보기로 계속 보입니다 (레이아웃 편집기가 열려 있을 때는 빼고)."));
        UIElement? opacityRow = null;
        p.Children.Add(Check("배경 판 표시 (내 오버레이와 파티원 창; 따로 둔 버프 창에는 판이 없습니다)", o.ShowPlate,
            v => { o.ShowPlate = v; opacityRow!.Visibility = Shown(v); }, Changed));
        p.Children.Add(opacityRow = SliderRow("배경 불투명도", 0.3, 1, o.Opacity, 0.05, v => $"{v * 100:0}%", v => o.Opacity = v, Changed));
        opacityRow.Visibility = Shown(o.ShowPlate);
        p.Children.Add(SliderRow("칸 불투명도", 0.2, 1, o.BoxOpacity, 0.05, v => $"{v * 100:0}%", v => o.BoxOpacity = v, Changed));
        double maxFont = Math.Max(OverlayRenderer.MinTimerFont, OverlayRenderer.MaxTimerFont(o.Width, o.Height));   // the box size is the editor's
        UIElement? fontRow = null, fromRow = null;
        p.Children.Add(Check("남은 시간 숫자 표시", o.ShowTimer, v => { o.ShowTimer = v; fontRow!.Visibility = Shown(v); }, Changed));
        p.Children.Add(fontRow = SliderRow("숫자 크기", OverlayRenderer.MinTimerFont, maxFont, Math.Min(o.TimerFont, maxFont),
            OverlayRenderer.TimerFontStep, v => $"{v:0.#}", v => o.TimerFont = v, Changed));
        fontRow.Visibility = Shown(o.ShowTimer);
        p.Children.Add(ColorRow(Label("경과 시간 색 (준비 후 경과 시간 표시, 스킬 칸에서 켭니다)"), o.ReadyCountColor, v => o.ReadyCountColor = v, swatch: false));
        p.Children.Add(Row("쿨타임 표시", EnumCombo([(CooldownStyle.Sweep, "회전"), (CooldownStyle.Fill, "채움 (어두운 색 → 밝은 색)"),
            (CooldownStyle.FillHatched, "채움 (원래 색 + 빗금)")], o.CooldownStyle,
            v => { o.CooldownStyle = v; fromRow!.Visibility = Shown(v != CooldownStyle.Sweep); })));
        p.Children.Add(fromRow = Row("채움 방향", EnumCombo([(FillFrom.Bottom, "아래 → 위"), (FillFrom.Top, "위 → 아래"), (FillFrom.Left, "왼쪽 → 오른쪽"), (FillFrom.Right, "오른쪽 → 왼쪽")],
            o.FillFrom, v => o.FillFrom = v)));
        fromRow.Visibility = Shown(o.CooldownStyle != CooldownStyle.Sweep);
        p.Children.Add(Note("빗금은 준비될 때까지 남아서, 거의 다 찬 칸을 준비된 칸으로 착각하지 않게 합니다. 충전 스킬은 쓸 수 있는 횟수가 남아 있으면 원래 색에 흰 선으로, 다 쓰면 회색으로 표시됩니다."));
        p.Children.Add(Check("화면 캡처에서 오버레이 숨기기 (OBS·디스코드 화면 공유에서도 숨겨집니다)", o.ExcludeFromCapture, v => o.ExcludeFromCapture = v, Changed));
        p.Children.Add(ColorRow(Label("각성기 변화 색"), o.AwakeningChangedColor, v => o.AwakeningChangedColor = v, swatch: false, warnDark: true));

        p.Children.Add(Heading("금색 테두리"));
        p.Children.Add(Note("게임에서 스킬 칸에 금색 테두리가 뜨면 오버레이의 그 칸에도 테두리를 그립니다. 두께는 칸 안쪽으로 늘어나고, 0이면 그리지 않습니다. 금색과 비슷한 칸 색은 스킬 칸의 색 항목에서 알려 줍니다."));
        p.Children.Add(ColorRow(Label("테두리 색"), o.GoldColor, v => { o.GoldColor = v; Dispatcher.BeginInvoke(FillClassSections); }, swatch: false));   // their warnings
        p.Children.Add(SliderRow("테두리 두께", 0, 8, o.GoldThickness, 0.5, v => v == 0 ? "끔" : $"{v:0.#} px", v => o.GoldThickness = v, Changed));
        p.Children.Add(ResetButton("오버레이 표시 되돌리기", "이 화면의 표시, 불투명도, 시간 숫자, 쿨타임 표시, 각성기 변화, 금색 테두리를 되돌립니다. 위치, 크기, 판 색은 남습니다.",
            () => SettingsResets.Display(_s)));
        FillBuffs();
    }

    /// <summary>버프/디버프: the buff bars, then under a line the raid debuffs, each part with its own reset.</summary>
    void FillBuffs()
    {
        var b = _s.Buffs;
        var p = Refill(_secBuffs, "버프", "내 버프 줄에서 공증, 아덴 피증, 초각스 피증, 뎀증을 찾아 남은 시간을 막대로 보여 줍니다. 파티원에게는 보내지 않습니다. 모두 끄면 버프 줄을 읽지 않습니다.");
        p.Children.Add(Row("게임의 버프 크기 (%)", BuffSizeCombo()));
        p.Children.Add(Row("막대 위치", Check("분리된 창", b.Detached, v => b.Detached = v, Changed), Check("세로 (시간 숫자 없음)", b.Side, v => b.Side = v, Changed)));
        p.Children.Add(Note("분리된 창: 버프 막대가 오버레이 판에서 빠져 자기 창에 그려지고, 편집 모드에서 그 창을 끌어 옮기거나 오른쪽 아래 모서리로 크기를 바꿉니다. " +
            "세로: 막대를 세워 옆으로 나란히 둡니다 (판과 함께면 판 왼쪽 바깥). 끄면 눕혀 위아래로 쌓습니다 (판과 함께면 칸 아래에 두 개씩)."));
        p.Children.Add(Row("줄어듦", Check("남은 시간만큼 줄어듦 (끄면 버프가 있는 동안 가득 찬 막대)", b.Drain, v => b.Drain = v, Changed)));
        p.Children.Add(Note("길이와 두께는 모든 배치에 적용됩니다. 칸을 옮긴 배치에서는 옮겨 둔 막대 크기를 100%로 봅니다. 레이아웃 편집기의 칸 옵션에서도 바꿀 수 있습니다."));
        foreach (var k in Enum.GetValues<BuffKind>())
        {
            var i = b.Of(k);
            string name = k switch { BuffKind.아덴피증 => "아덴 피증", BuffKind.초각스피증 => "초각스 피증", _ => k.ToString() };
            var label = Check("시간 표시", i.ShowLabel, v => b.Edit(k).ShowLabel = v, Changed);
            var flash = Check("마지막 1초 깜빡임", b.EndFlash(k), v => b.Edit(k).EndFlash = v, Changed);
            flash.ToolTip = "남은 시간이 1초 아래로 내려가면 막대가 줄어드는 방향으로 빛이 지나갑니다.";
            var more = Stack([ColorRow(Label("   색"), i.Color, v => b.Edit(k).Color = v, swatch: false), .. BuffSizeSliders(k, "   ")]);
            void Show(bool on) => label.Visibility = flash.Visibility = more.Visibility = Shown(on);
            Show(i.Visible);
            p.Children.Add(SubHeading(name));
            p.Children.Add(Row("", Check("표시", i.Visible, v => { b.Edit(k).Visible = v; Show(v); }, Changed), label, flash));
            p.Children.Add(more);
        }
        p.Children.Add(ResetButton("버프 되돌리기", "버프 막대 설정을 되돌립니다. 분리된 창의 위치와 크기는 남습니다.", () => SettingsResets.Buffs(_s)));

        p.Children.Add(Divider());
        p.Children.Add(Heading("디버프", first: true));
        p.Children.Add(Note("레이드를 고르면 MP 바 위의 디버프 줄(버프 줄의 반대편)과 보스 체력바 아래의 빨강·파랑 숫자를 읽어 디버프 창에 보여 줍니다: " +
            "내 색, 교체 때 데려갈 인원(빨강과 파랑이 반반이 되도록), 번개(남은 초, 깜빡임). 편집 모드에서 창을 옮기고 크기를 바꿉니다. " +
            "보스 숫자는 아직 0, 1, 4, 6, 8만 읽고, 하나만 읽히면 다른 하나는 8에서 뺀 값으로 봅니다."));
        p.Children.Add(Row("레이드", EnumCombo([.. Enum.GetValues<Raid>().Select(r => (r, Tray.RaidName(r)))], _s.Debuffs.Raid, v => _s.Debuffs.Raid = v)));
        p.Children.Add(SubHeading("칸 크기"));
        p.Children.Add(Note("칸마다 너비와 높이를 바꿉니다 (100% = 창 크기에 맞춘 칸). 창은 칸에 맞춰 커집니다. " +
            "레이아웃 편집기에서 칸을 눌러 바꾸거나, 칸 옮기기를 켜고 칸의 오른쪽 아래 모서리를 끌어도 됩니다."));
        for (int i = 0; i < DebuffSettings.BoxCount; i++)
            foreach (var row in DebuffSizeSliders(i, DebuffNames[i] + " ")) p.Children.Add(row);
        p.Children.Add(ResetButton("디버프 칸 크기 되돌리기", "디버프 칸 크기를 되돌립니다. 고른 레이드와 창의 위치와 크기는 남습니다.", () => SettingsResets.Debuffs(_s)));
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

    /// <summary>A buff bar's 길이 (along its drain: an upright bar's height) and 두께, 100% = its spot in the layout; in 버프/디버프
    /// and in the 레이아웃 편집기's 칸 옵션.</summary>
    UIElement[] BuffSizeSliders(BuffKind k, string indent = "")
    {
        var b = _s.Buffs;
        var i = b.Of(k);
        return [SliderRow(indent + "길이", BarSizes.MinLength, BarSizes.MaxLength, i.Length, 0.05, v => $"{v * 100:0}%", v => b.Edit(k).Length = v, Changed),
            SliderRow(indent + "두께", BarSizes.MinThickness, BarSizes.MaxThickness, i.Thickness, 0.25, v => $"{v * 100:0}%", v => b.Edit(k).Thickness = v, Changed)];
    }

    // ── 키·단축키 ───────────────────────────────────────────

    void FillKeys()
    {
        var p = Refill(_secKeys, "게임 키");
        p.Children.Add(BuildKeyPanel(_s, () => { Changed(); Dispatcher.BeginInvoke(() => { FillClassSections(); FillChips(); FillBox(); FillHotkeys(); }); }));
        p.Children.Add(ResetButton("키를 게임 기본값으로", "스킬 키를 게임 기본값으로 되돌립니다.", () => SettingsResets.Keys(_s)));
    }

    /// <summary>The app's hotkeys, those clashing marked red.</summary>
    void FillHotkeys()
    {
        var p = Refill(_secHotkeys, "앱 단축키", "칸을 누른 다음 원하는 조합을 누르세요. 게임 밖에서도 동작합니다.");
        var h = _s.Hotkeys;
        var keys = new Dictionary<string, Button>();
        var clash = Note("빨간 칸은 다른 단축키나 스킬 키와 겹칩니다. 겹치면 하나만 되거나 둘 다 됩니다.");
        clash.SetResourceReference(TextBlock.ForegroundProperty, CriticalBrushKey);
        void Mark()
        {
            var clashes = h.Clashes(_s.Keys.Values);
            clash.Visibility = clashes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (action, field) in keys)
                if (clashes.Contains(action)) { field.SetResourceReference(Control.BorderBrushProperty, CriticalBrushKey); field.BorderThickness = new(2); }
                else { field.ClearValue(Control.BorderBrushProperty); field.ClearValue(Control.BorderThicknessProperty); }
        }
        Button Key(string action, Func<string> get, Action<string> set) => keys[action] = KeyField(get, v => { set(v); Changed(); Mark(); });
        p.Children.Add(Row("편집 모드", Key(nameof(h.EditMode), () => h.EditMode, v => h.EditMode = v)));
        p.Children.Add(Row("오버레이 표시/숨기기", Key(nameof(h.ToggleOverlay), () => h.ToggleOverlay, v => h.ToggleOverlay = v)));
        p.Children.Add(Row("스킬바 다시 맞추기 (캐릭터 감지 포함)", Key(nameof(h.DetectCharacter), () => h.DetectCharacter, v => h.DetectCharacter = v)));
        var clearRedetect = new Button { Content = "지우기" };
        clearRedetect.Click += (_, _) => { h.DetectSubclass = ""; FillHotkeys(); Changed(); };
        p.Children.Add(Row("직업각인 다시 감지", Key(nameof(h.DetectSubclass), () => h.DetectSubclass, v => h.DetectSubclass = v), clearRedetect));
        var clearIcons = new Button { Content = "지우기" };
        clearIcons.Click += (_, _) => { h.RereadIcons = ""; FillHotkeys(); Changed(); };
        p.Children.Add(Row("아이콘 다시 읽기", Key(nameof(h.RereadIcons), () => h.RereadIcons, v => h.RereadIcons = v), clearIcons));
        p.Children.Add(Row("테스트 모드 (맨 앞 창)", Key(nameof(h.TestMode), () => h.TestMode, v => h.TestMode = v)));
        // 오버레이 프로필 1–5 (위치, 크기, 판 색; set up in the 레이아웃 편집기): unset by default, so each can be cleared again.
        (Func<string>, Action<string>)[] profiles = [(() => h.Profile1, v => h.Profile1 = v), (() => h.Profile2, v => h.Profile2 = v),
            (() => h.Profile3, v => h.Profile3 = v), (() => h.Profile4, v => h.Profile4 = v), (() => h.Profile5, v => h.Profile5 = v)];
        for (int i = 0; i < profiles.Length; i++)
        {
            var (get, set) = profiles[i];
            var clear = new Button { Content = "지우기" };
            clear.Click += (_, _) => { set(""); FillHotkeys(); Changed(); };
            p.Children.Add(Row($"오버레이 프로필 {i + 1}", Key($"Profile{i + 1}", get, set), clear));
        }
        p.Children.Add(clash);
        Mark();
        p.Children.Add(ResetButton("단축키 되돌리기", "단축키를 기본값으로 되돌립니다. 오버레이 프로필 단축키는 지워집니다.", () => SettingsResets.Hotkeys(_s)));
    }

    /// <summary>Inline two-step confirm (no modal): the first click relabels the button, a second click within
    /// 5 s resets, otherwise it reverts. The label names what it resets; the tooltip says what stays.</summary>
    Button ResetButton(string idle, string consequence, Action reset)
    {
        var b = new Button { Content = idle, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 16, 0, 0), ToolTip = consequence };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); b.Content = idle; };
        b.Click += (_, _) =>
        {
            if (!timer.IsEnabled) { b.Content = "정말 되돌리기"; timer.Start(); return; }
            timer.Stop();
            b.Content = idle;
            reset();
            BuildAll();
            Changed();
        };
        return b;
    }

    const string HexPattern = "^#[0-9A-Fa-f]{6}$";
    const string Default = "기본 (클래스 없음)";

    static Color? ParseHex(string s) => Regex.IsMatch(s, HexPattern) ? (Color)ColorConverter.ConvertFromString(s) : null;

    /// <summary>A colour field with preview. <paramref name="swatch"/> shows a ready and a cooldown box; those rows
    /// (and <paramref name="warnDark"/> ones) warn when the colour is too dark to show the ready/cooldown step, and
    /// <paramref name="warnGold"/> rows when it is hard to tell from the gold frame (금색 테두리).</summary>
    UIElement ColorRow(UIElement label, string value, Action<string> set, bool swatch, bool warnDark = false, bool warnGold = false)
    {
        var preview = new Border { Width = swatch ? 64 : 22, Height = swatch ? 34 : 22, CornerRadius = new(swatch ? 0 : 2) };
        var warn = new TextBlock { Margin = new(0, 2, 0, 2), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        warn.SetResourceReference(TextBlock.ForegroundProperty, CautionBrushKey);
        void Show(Color c)
        {
            if (swatch) preview.Child = new System.Windows.Controls.Image { Source = OverlayRenderer.Swatch(c), Width = 64, Height = 34 };
            else preview.Background = new SolidColorBrush(c);
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            var problems = new List<string>();
            if ((swatch || warnDark) && OverlayRenderer.Lightness(c) < MinLightness) problems.Add("어두운 색은 준비/대기 구분이 약해집니다");
            if (warnGold && ParseHex(_s.Overlay.GoldColor) is not null && Core.Vision.ColorDistance.CloseTo(_s.Overlay.GoldColor, hex))
                problems.Add("금테와 비슷함: 금색 테두리가 잘 보이지 않을 수 있습니다");
            warn.Text = string.Join(" · ", problems);
            warn.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        var box = new TextBox { Text = value, Width = 116, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) =>
        {
            if (ParseHex(box.Text) is not { } c)
            {
                box.SetResourceReference(Control.BorderBrushProperty, CriticalBrushKey);
                box.BorderThickness = new(2);
                return;
            }
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
            set(box.Text.ToUpperInvariant());
            Show(c);
            Changed();
        };
        var pick = new Button { Content = "색 선택…" };
        pick.Click += (_, _) =>
        {
            var c = ParseHex(box.Text) ?? ParseHex(value) ?? Colors.Gray;
            using var d = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) };
            if (d.ShowDialog() == Forms.DialogResult.OK) box.Text = $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}";
        };
        Show(ParseHex(value) ?? Colors.Gray);
        return Row(label, Stack(Wrap(preview, box, pick), warn));
    }

    // ── Shared builders (also used by FirstRunWindow) ────────────────────────────────

    /// <summary>A key field: click it, then press the key or combination (Esc cancels). A modifier alone shows
    /// "Ctrl+…" and is assigned only when released without another key, so Ctrl+Q can be entered.
    /// Shows friendly names ("1", "`") but stores the <see cref="KeyNames"/> format.</summary>
    internal static Button KeyField(Func<string> get, Action<string> set)
    {
        var f = new Button { MinWidth = 140 };
        bool capturing = false, modifierDown = false;
        void Show() { capturing = modifierDown = false; f.Content = get() == "" ? "(없음)" : KeyNames.Display(get()); }
        void Assign(string key) { set(key); Show(); }
        static Key Real(KeyEventArgs e) => e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        static string Held() => KeyNames.Combo(Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), "");

        f.Click += (_, _) => { capturing = true; modifierDown = false; f.Content = "키를 누르세요…"; f.Focus(); };
        f.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            var name = KeyNames.Of(Real(e));
            if (name == nameof(Key.Escape) && Keyboard.Modifiers == ModifierKeys.None) Show();
            else if (KeyNames.IsModifier(name)) { modifierDown = true; f.Content = Held() + "…"; }
            else Assign(Held() + name);
        };
        f.PreviewKeyUp += (_, e) =>
        {
            if (!capturing || !modifierDown) return;
            e.Handled = true;
            if (KeyNames.Of(Real(e)) is var name && KeyNames.IsModifier(name)) Assign(name);
        };
        f.LostKeyboardFocus += (_, _) => { if (capturing) Show(); };
        Show();
        return f;
    }

    /// <summary>키 설정 rows (see <see cref="KeyField"/>). Keys used by more than one slot are highlighted.</summary>
    internal static UIElement BuildKeyPanel(Settings s, Action changed)
    {
        var p = new StackPanel();
        p.Children.Add(Note("칸을 누른 다음 게임에서 쓰는 키를 누르세요. Shift, Ctrl도 됩니다. Esc를 누르면 취소됩니다."));
        var fields = new Dictionary<SlotId, (Button Field, TextBlock Warn)>();

        void Refresh()
        {
            var groups = s.Keys.GroupBy(k => k.Value, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Select(k => k.Key).ToList()).ToList();
            var dup = groups.SelectMany(g => g).ToHashSet();
            foreach (var (id, (field, warn)) in fields)
            {
                bool d = dup.Contains(id);
                if (d) warn.Text = $"{string.Join(", ", groups.First(g => g.Contains(id)).Where(o => o != id))}와(과) 겹칩니다";
                warn.Visibility = d ? Visibility.Visible : Visibility.Collapsed;
                if (d) { field.SetResourceReference(Control.BorderBrushProperty, CriticalBrushKey); field.BorderThickness = new(2); }
                else { field.ClearValue(Control.BorderBrushProperty); field.ClearValue(Control.BorderThicknessProperty); }
            }
        }

        foreach (var id in SlotInfo.All)
        {
            var field = KeyField(() => s.Keys.GetValueOrDefault(id, ""), key => { s.Keys[id] = key; changed(); Refresh(); });
            var warn = new TextBlock { Text = "다른 슬롯과 겹칩니다", VerticalAlignment = VerticalAlignment.Center };
            warn.SetResourceReference(TextBlock.ForegroundProperty, CriticalBrushKey);
            fields[id] = (field, warn);
            p.Children.Add(Row(SlotLabel(id), field, warn));
        }
        Refresh();
        return p;
    }

    /// <summary>캐릭터: 직업군 → 클래스 → 직업각인. Picking the 직업각인 makes that "클래스 · 직업각인" character active, as the tray does;
    /// 직업군 <see cref="Default"/> is 기본, no class.</summary>
    internal static UIElement BuildCharacterPanel(Settings s, CharacterService chars, Action changed)
    {
        var classes = chars.ClassTree.ToDictionary(f => f.Family, f => f.Classes.ToDictionary(c => c.Class, c => c.Subclasses));
        var family = Combo();
        var cls = Combo();
        var sub = Combo();
        var p = new StackPanel();
        var hint = Note("직업군 → 클래스 → 직업각인 순서로 고르세요. 직업각인까지 골라야 이 캐릭터로 저장됩니다.");
        p.Children.Add(hint);
        p.Children.Add(Row("직업군", family));
        p.Children.Add(Row("클래스", cls));
        p.Children.Add(Row("직업각인", sub));

        // The choice in progress; nothing is stored until a 직업각인 is picked, which goes through CharacterService.Pick as
        // the tray does: that character becomes active (added the first time) and the others, with their emblems, stay.
        var cur = s.ActiveCharacter is { } k && s.Characters.TryGetValue(k, out var found) ? found : null;
        string? pickedFamily = cur?.ClassFamily, pickedClass = cur?.Class;
        bool sync = false;

        void Set(ComboBox cb, IEnumerable<object> items, Func<object, bool> selected)
        {
            sync = true;
            cb.ItemsSource = items.ToList();
            cb.SelectedItem = cb.Items.Cast<object>().FirstOrDefault(selected);
            sync = false;
        }
        IReadOnlyList<string> Subs() => pickedFamily is { } f && classes.TryGetValue(f, out var fc) && pickedClass is { } c
            && fc.TryGetValue(c, out var subs) ? subs : [];
        void FillClasses() => Set(cls, pickedFamily is { } f && classes.TryGetValue(f, out var fc) ? fc.Keys : [], o => (string)o == pickedClass);
        void FillSubs()
        {
            bool same = cur is not null && cur.ClassFamily == pickedFamily && cur.Class == pickedClass;
            Set(sub, Subs(), o => same && (string)o == cur!.Subclass);
            hint.Visibility = sub.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
        }

        family.SelectionChanged += (_, _) =>
        {
            if (sync || family.SelectedItem is not string f) return;
            (pickedFamily, pickedClass) = (f, null);
            FillClasses(); FillSubs();
            if (f != Default) return;
            chars.PickDefault();   // no class: 기본 at once, nothing further to pick
            cur = null;
            hint.Visibility = Visibility.Collapsed;
            changed();
        };
        cls.SelectionChanged += (_, _) =>
        {
            if (sync || cls.SelectedItem is not string v) return;
            pickedClass = v;
            FillSubs();
        };
        sub.SelectionChanged += (_, _) =>
        {
            if (sync || sub.SelectedItem is not string v || pickedFamily is null || pickedClass is null) return;
            chars.Pick(pickedFamily, pickedClass, v);
            cur = s.Characters[s.ActiveCharacter!];
            hint.Visibility = Visibility.Collapsed;
            changed();
        };

        Set(family, [Default, .. classes.Keys], o => (string)o == (pickedFamily ?? Default));
        FillClasses(); FillSubs();
        if (cur is null) hint.Visibility = Visibility.Collapsed;
        return p;
    }

    // ── Small control helpers ────────────────────────────────────────────────────────

    const string CriticalBrushKey = "SystemFillColorCriticalBrush", CautionBrushKey = "SystemFillColorCautionBrush";
    const double MinLightness = 60;   // L*; darker group colours make ready vs cooldown hard to tell apart

    static string SlotLabel(SlotId id) => id switch
    {
        SlotId.아덴1 => "아덴1 (엠블럼 왼쪽)",
        SlotId.아덴2 => "아덴2 (엠블럼 오른쪽)",
        _ => id.ToString(),
    };

    /// <summary>"일반3 (E)": the slot with the user's key.</summary>
    static string SlotWithKey(Settings s, SlotId id) => $"{id} ({KeyNames.Display(s.Keys.GetValueOrDefault(id, "?"))})";

    internal static StackPanel Stack(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    internal static TextBlock Heading(string text, bool first = false) =>
        new() { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, first ? 0 : 24, 0, 8) };

    /// <summary>A heading inside a part of a section (a buff, 칸 크기), a step under <see cref="Heading"/>.</summary>
    static TextBlock SubHeading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new(0, 16, 0, 6) };

    internal static TextBlock Note(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new(0, 0, 0, 10) };

    static TextBlock Label(string text, double opacity = 1) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Opacity = opacity, TextWrapping = TextWrapping.Wrap };

    /// <summary>A row label with a small line under it saying what the row does.</summary>
    static StackPanel Described(string label, string description) =>
        Stack(Label(label), new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11.5 });

    static ComboBox Combo() => new() { MinWidth = 200 };

    /// <summary>The game's 해상도 list (게임 설정 → 화면); anything else is 커스텀 화면크기.</summary>
    static readonly (int W, int H)[] Resolutions = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 1600), (3840, 2160), (5120, 2160)];
    const string Custom = "커스텀 화면크기";
    bool _customScreen;   // 커스텀 화면크기 picked, even while its size is one of the list's

    /// <summary>직접 입력's 해상도 (a list, 커스텀 화면크기 with its own width and height) and 21:9.</summary>
    void ScreenRows(StackPanel p)
    {
        var screen = _s.ManualScreen ??= new(1920, 1080, false);   // StartManual sets it; an older file may not have
        bool custom = _customScreen || !Resolutions.Contains((screen.Width, screen.Height));
        var c = Combo();
        c.ItemsSource = Resolutions.Select(r => $"{r.W}×{r.H}").Append(Custom).ToList();
        c.SelectedItem = custom ? Custom : $"{screen.Width}×{screen.Height}";
        c.SelectionChanged += (_, _) =>
        {
            _customScreen = c.SelectedIndex == Resolutions.Length;
            if (!_customScreen && c.SelectedIndex >= 0)
                _s.ManualScreen = _s.ManualScreen! with { Width = Resolutions[c.SelectedIndex].W, Height = Resolutions[c.SelectedIndex].H };
            Changed();
            Dispatcher.BeginInvoke(FillFit);
        };
        p.Children.Add(Row("해상도", c));
        if (custom)
        {
            // Checked when the box loses focus: a half-typed number isn't a size.
            TextBox Field(Func<ManualScreen, int> get, Func<ManualScreen, int, ManualScreen> with)
            {
                string Shown() => get(_s.ManualScreen!).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var box = new TextBox { Text = Shown(), Width = 70, Margin = new(0, 0, 8, 0) };
                box.LostKeyboardFocus += (_, _) =>
                {
                    if (int.TryParse(box.Text, out int v) && v is >= 640 and <= 16384) { _s.ManualScreen = with(_s.ManualScreen!, v); Changed(); }
                    else box.Text = Shown();
                };
                return box;
            }
            p.Children.Add(Row("화면 크기 (가로 × 세로)", Field(s => s.Width, (s, v) => s with { Width = v }), Field(s => s.Height, (s, v) => s with { Height = v })));
        }
        p.Children.Add(Check("21:9 화면 비율 사용", screen.Wide, v => _s.ManualScreen = _s.ManualScreen! with { Wide = v }, () => { FillFit(); Changed(); }));
        p.Children.Add(Note("게임 설정의 21:9 화면 비율입니다. 더 좁은 화면에서는 위아래에 검은 띠가 생기고, 21:9보다 넓은 화면에서는 화면을 그대로 씁니다."));
    }

    /// <summary>게임의 버프 크기, shown in 버프/디버프 and with 직접 입력; either one changes the same setting.</summary>
    ComboBox BuffSizeCombo()
    {
        var size = Combo();
        size.ItemsSource = new[] { 80, 90, 100, 110, 120 };
        size.SelectedItem = _s.Buffs.Size;
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is int v && v != _s.Buffs.Size) { _s.Buffs.Size = v; Changed(); Dispatcher.BeginInvoke(() => { FillBuffs(); FillFit(); }); } };
        return size;
    }

    /// <summary>A row that only applies to some choices is hidden, not greyed, for the others.</summary>
    static Visibility Shown(bool applies) => applies ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A combo box over <paramref name="items"/> showing <paramref name="value"/>; a pick calls <paramref name="set"/>, then saves.</summary>
    ComboBox EnumCombo<T>((T Value, string Text)[] items, T value, Action<T> set) where T : struct, Enum
    {
        var c = Combo();
        c.ItemsSource = items.Select(i => new ComboBoxItem { Content = i.Text, Tag = i.Value }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => value.Equals(i.Tag)) ?? c.Items[0];
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: T v }) { set(v); Changed(); } };
        return c;
    }

    /// <summary>Controls side by side, wrapping onto the next line when the form is narrow.</summary>
    static WrapPanel Wrap(params UIElement[] controls)
    {
        var w = new WrapPanel();
        foreach (var c in controls)
        {
            if (c is FrameworkElement f && f.Margin == default) f.Margin = new(0, 2, 8, 2);
            w.Children.Add(c);
        }
        return w;
    }

    static FormRow Row(string label, params UIElement[] controls) => Row(Label(label), controls);

    static FormRow Row(UIElement label, params UIElement[] controls) => new(label, Wrap(controls));

    static CheckBox Check(string text, bool value, Action<bool> set, Action changed)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new(0, 2, 16, 2) };
        c.Click += (_, _) => { set(c.IsChecked == true); changed(); };
        return c;
    }

    static UIElement SliderRow(string label, double min, double max, double value, double step,
        Func<double, string> format, Action<double> set, Action changed)
    {
        var text = new TextBlock { Text = format(value), Width = 56, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, Width = 240, MinWidth = 120, TickFrequency = step,
            IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) => { set(e.NewValue); text.Text = format(e.NewValue); changed(); };
        return Row(label, slider, text);
    }
}
