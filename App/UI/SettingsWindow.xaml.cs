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

    /// <summary>캐릭터·인식 → 스킬창 → "스킬창 다시 맞추기".</summary>
    public event Action? RefitRequested;
    /// <summary>캐릭터·인식 → 스킬창 → "디버그 창에서 위치 조정".</summary>
    public event Action? AdjustRequested;

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

    /// <summary>아덴 게이지 → 스킬별 예측 → 이 캐릭터의 배운 값 지우기.</summary>
    public event Action? GainsResetRequested;

    /// <summary>캐릭터·인식 → 스킬창 → "스킬 등록" / "등록된 스킬 지우기".</summary>
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

    /// <summary>Shows the last fit result in 캐릭터·인식 → 스킬창 ("맞춤 완료: …" or the error).</summary>
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
            (GaugeBox.Visibility, GaugeBox.Margin) = (Visibility.Visible, new());   // the editor's side margin; the card pads here
            _secGauge.Children.Add(GaugeBox);
        }
        IsVisibleChanged += (_, _) => { if (IsVisible) RefreshPick(reloadIcons: true); };   // its icons may have been read since
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
        string? active = ClassProfiles.ActiveKey(_s);
        if (active != _lastActive) (_classShown, _lastActive) = (null, active);   // a new character: edit its class
        RefreshPick();   // before the sections: they show what the pick means
        FillCharacter();
        FillFit();
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
        QuickRefresh();   // the buff and debuff windows as the settings now say (a raid picked in the tray)
    }

    /// <summary>Clears a section and starts it with its title and what it is for.</summary>
    static StackPanel Refill(StackPanel p, string title, string? lead = null)
    {
        p.Children.Clear();
        p.Children.Add(Heading(title, first: true, help: lead));   // what the section is for: in its ?
        return p;
    }

    // ── 캐릭터·인식 ───────────────────────────────────────────

    void FillCharacter()
    {
        var p = Refill(_secCharacter, "캐릭터", "트레이 메뉴에서 고른 캐릭터와 같습니다. 클래스마다 마지막으로 고르거나 읽은 직업각인을 기록해 두고, 스킬창을 다시 맞출 때 그 클래스면 직업각인을 읽지 않고 기록대로 고릅니다. 기록이 없을 때만 게임 화면에서 직업각인을 읽습니다.");
        p.Children.Add(BuildCharacterPanel(_s, _chars, () => { Changed(); Dispatcher.BeginInvoke(BuildAll); }));
        if (_chars.ActiveClass is { } cls)
            p.Children.Add(Label(_s.SubclassHistory.TryGetValue(cls.Name, out var sub) ? $"기록된 직업각인: {cls.Name} → {sub}" : $"기록된 직업각인: {cls.Name} → 없음", 0.8));
        var redetect = new Button { Content = "직업각인 다시 감지", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
        redetect.Click += (_, _) => SubclassRedetectRequested?.Invoke();
        redetect.IsEnabled = _chars.ActiveClass is not null && _chars.SameSkillsNote is null;
        p.Children.Add(Wrap(redetect, HelpMark("기록을 무시하고 초각스가 준비됐을 때 직업각인을 다시 읽어 기록을 고칩니다. 알아보지 못하면 기록은 그대로 둡니다.")));
        if (_chars.SameSkillsNote is { } same) p.Children.Add(Note(same));
        CoreRows(p);
        p.Children.Add(SubHeading("지원하는 직업각인", "직업각인 감지 '자동': 스킬창 다시 맞추기 때 초각스 아이콘으로 직업각인을 알아봅니다. 아덴 게이지와 아덴 스킬이 있는 직업각인은 그것도 읽습니다. " + "표에 없는 클래스도 쿨타임, 버프 막대, 파티 공유는 모두 됩니다. NEW는 지난번에 이 화면을 본 뒤 추가된 것입니다."));
        p.Children.Add(SupportTable());
        p.Children.Add(Note(CoresExplained));
    }

    static readonly Brush NewBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1F));
    static readonly Brush MineBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x3C, 0x9E, 0xE8));

    /// <summary>지원하는 직업각인: one row per 직업각인 the shipped data supports (Core.Data.ClassSupport), slots named by the user's
    /// keys, the active one highlighted; NEW on a 직업각인, or on a feature of one already seen, added since the user last looked.</summary>
    Grid SupportTable()
    {
        var rows = _chars.Support(SlotKeyName);
        var fresh = ClassSupport.New(rows, _s.SeenSupport);
        var grid = new Grid { Margin = new(0, 4, 0, 8) };
        // MinWidth: at the window's narrowest the 직업각인 감지 header ("직업각인" on its first line) still fits its column.
        foreach (var (w, min) in new[] { (1.1, 90.0), (0.6, 76.0), (1.9, 80.0), (1.1, 80.0), (1.8, 80.0) })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star), MinWidth = min });
        void Add(UIElement e, int row, int col, int span = 1)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            Grid.SetColumnSpan(e, span);
            grid.Children.Add(e);
        }
        TextBlock Cell(string text, bool isNew, bool head = false)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(4, 4, 8, 4), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal };
            if (isNew) t.Inlines.Add(new System.Windows.Documents.Run("NEW ") { Foreground = NewBrush, FontWeight = FontWeights.Bold });
            t.Inlines.Add(new System.Windows.Documents.Run(text));
            return t;
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = ["직업각인", "직업각인 감지", "아덴 게이지", "아덴 스킬", "코어 빌드"];
        for (int c = 0; c < heads.Length; c++) Add(Cell(heads[c], false, head: true), 0, c);
        var active = _chars.Active;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int r = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool mine = active is { } a && a.Class == row.Class && a.Subclass == row.Subclass;
            if (mine) Add(new Border { Background = MineBrush, CornerRadius = new(4) }, r, 0, heads.Length);
            bool rowNew = fresh.Contains(row.Key);
            bool New(string feature) => !rowNew && fresh.Contains($"{row.Key}/{feature}");
            Add(Cell($"{row.Class} · {row.Subclass}" + (mine ? " (지금)" : ""), rowNew), r, 0);
            Add(Cell(row.Detect ? "자동" : "직접 고름", row.Detect && New("감지")), r, 1);
            Add(Cell(row.Gauge is { } g ? $"{g.Read} 표시: {g.Shown}" : "–", row.Gauge is not null && New("게이지")), r, 2);
            Add(Cell(row.Skills ?? "–", row.Skills is not null && New("스킬")), r, 3);
            Add(Cell(row.Cores ?? "–", row.HasCoreData && New("코어")), r, 4);
        }
        return grid;
    }

    /// <summary>The 캐릭터 section was shown: what it lists counts as seen, so NEW is gone next time (it stays while on screen).</summary>
    void MarkSupportSeen()
    {
        var keys = ClassSupport.Keys(_chars.Support(slot => slot.ToString()));
        if (_s.SeenSupport is { } seen && keys.All(seen.Contains)) return;
        _s.SeenSupport = [.. (_s.SeenSupport ?? []).Union(keys)];
        Changed();
    }

    void FillFit()
    {
        var p = Refill(_secFit, "스킬창 맞추기", "스킬을 쓸 수 있는 곳에서 모든 스킬이 쿨 돈 상태로 가만히 서서 누르세요 (수련장에서는 쿨초). 해상도나 게임의 HUD 크기를 바꾼 뒤에도 다시 맞춥니다. 맞추면서 클래스도 다시 확인합니다. 직업각인은 기록이 있으면 기록대로 고릅니다.");
        if (_s.Fits.Count == 0) p.Children.Add(Label("아직 맞춘 적이 없습니다.", 0.7));
        foreach (var (size, fit) in _s.Fits)
            p.Children.Add(Row(size, Label($"HUD {fit.Hud:0.#}%" + (_s.Adjusts.TryGetValue(size, out var adj) ? $" · 수동 조정 HUD {adj.Hud:0.#}%" : ""))));
        foreach (var (size, adj) in _s.Adjusts.Where(a => !_s.Fits.ContainsKey(a.Key)))
            p.Children.Add(Row(size, Label($"수동 조정 HUD {adj.Hud:0.#}%")));

        // 스킬창 위치: 자동 / 직접 입력 / 수동 조정 (the last only once one was applied in the debug window).
        var mode = _s.FitMode;
        var modes = new WrapPanel();
        foreach (var (m, text) in new[] { (FitMode.자동, "자동"), (FitMode.직접입력, "직접 입력"), (FitMode.수동조정, "수동 조정") })
        {
            var r = Toggle(text, "fitMode", m == mode);
            r.IsEnabled = m != FitMode.수동조정 || _s.Adjusts.Count > 0 || mode == FitMode.수동조정;
            r.Checked += (_, _) =>
            {
                if (_s.FitMode == m) return;
                _s.SetFitMode(m);
                Changed();
                Dispatcher.BeginInvoke(FillFit);
            };
            modes.Children.Add(r);
        }
        p.Children.Add(Row(HelpLabel("스킬창 위치", mode switch
        {
            FitMode.직접입력 => "찾지 않고 게임 설정에 적힌 값을 그대로 써서, 스킬창 자리만 확인한 뒤 클래스와 직업각인을 읽습니다. 처음 값은 마지막으로 찾은 값입니다.",
            FitMode.수동조정 => "디버그 창에서 옮기고 적용한 위치를 씁니다. 게임 화면 크기마다 따로 저장하며, 적용하지 않은 크기에서는 자동으로 맞춘 값을 씁니다.",
            _ => "맞추기는 화면의 검은 띠와 HP 바로 해상도와 HUD 크기를 찾습니다." + (_s.Adjusts.Count == 0 ? " 수동 조정은 디버그 창에서 위치를 적용한 뒤 고를 수 있습니다." : ""),
        }), modes));
        p.Children.Add(HelpRow("게임의 버프 크기 (%)", "게임 설정의 버프 크기와 같게 맞춰 주세요. 스킬창 다시 맞추기는 버프 크기를 찾지 않습니다. 다르면 버프·디버프를 엉뚱한 곳에서 읽습니다. 120%에서 가장 잘 읽습니다.", BuffSizeCombo()));
        if (mode == FitMode.직접입력 && _s.ManualHud is { } hud)
        {
            var hudSize = Combo();
            hudSize.ItemsSource = Settings.HudSizes;
            hudSize.SelectedItem = Settings.NearestHud(hud);
            hudSize.SelectionChanged += (_, _) => { if (hudSize.SelectedItem is int v && v != _s.ManualHud) { _s.ManualHud = v; Changed(); } };
            p.Children.Add(Row("게임의 HUD 크기 (%)", hudSize));
            ScreenRows(p);
            var auto = new Button { Content = "자동으로 맞추기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 0) };
            auto.Click += (_, _) => { (_s.ManualHud, _s.ManualScreen) = (null, null); FillFit(); Changed(); RefitRequested?.Invoke(); };
            p.Children.Add(Wrap(auto, HelpMark("직접 입력한 값으로 스킬창을 찾지 못하면 직접 입력을 끄고 해상도와 HUD 크기를 찾아 맞춥니다.")));
        }
        var b = new Button { Content = "스킬창 다시 맞추기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 12, 0, 0) };
        b.Click += (_, _) => RefitRequested?.Invoke();
        Themes.Primary(b);   // 스킬창's main action; the others fix a fit that went wrong
        var check = HelpMark(ClassCheckNote());   // the class is checked by this button, so what it checks is said here
        check.ToolTipOpening += (_, _) => ((TextBlock)check.ToolTip).Text = ClassCheckNote();   // the hotkey may have changed in 앱 단축키
        p.Children.Add(Wrap(b, check));
        if (_fitResult is { } result) p.Children.Add(new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        // The main action first; fixing a fit that went wrong comes after it, under its own heading.
        p.Children.Add(SubHeading("인식이 어긋날 때"));
        var adjustButton = new Button { Content = "디버그 창에서 수동 조정", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 4) };
        adjustButton.Click += (_, _) => AdjustRequested?.Invoke();
        p.Children.Add(Wrap(adjustButton, HelpMark("인식이 어긋나면 디버그 창의 실시간 화면에서 스킬창과 버프·디버프·보스 줄을 끌어 맞추고 적용합니다. 적용하기 전에는 창이 열려 있는 동안만 쓰입니다.")));
        p.Children.Add(SubHeading("스킬 등록", "마을처럼 스킬을 못 쓰는 곳에서는 스킬창이 회색이 되어 직업각인을 읽기 어렵습니다. 스킬을 쓸 수 있는 곳에서 일반 스킬이 모두 준비됐을 때 등록해 두면, 회색 스킬창에서도 일반 스킬 8개 중 5개 이상이 맞는 직업각인을 고릅니다. 스킬 위치는 상관없습니다. 지금 고른 직업각인으로 저장되며, 다른 스킬 구성은 추가로 등록할 수 있습니다. 초각스 아이콘으로 읽지 못할 때 쓰입니다. 등록하지 않은 직업각인은 스킬로 고르지 않고, 두 직업각인이 같이 쓰는 스킬은 빼고 나머지로 비교합니다."));
        var submit = new Button { Content = "스킬 등록", Margin = new(0, 0, 8, 0) };
        submit.Click += (_, _) => SkillsSubmitRequested?.Invoke();
        submit.IsEnabled = _chars.SameSkillsNote is null;
        var clear = new Button { Content = "등록된 스킬 지우기" };
        clear.Click += (_, _) => SkillsClearRequested?.Invoke();
        p.Children.Add(new WrapPanel { Children = { submit, clear } });
        if (_chars.SameSkillsNote is not null) p.Children.Add(Note($"{_chars.ActiveClass!.Name}: 두 직업각인의 스킬이 같아 스킬로 고를 수 없으므로 등록하지 않습니다."));
        if (_skillsResult is { } skills) p.Children.Add(new TextBlock { Text = skills, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        p.Children.Add(ResetButton("스킬창 맞춤 지우기", "저장된 스킬창 맞춤을 모두 지웁니다. 다음 실행 때 다시 맞춰야 합니다.", () => SettingsResets.Fits(_s)));
    }

    /// <summary>A hotkey as shown, or that there is none.</summary>
    internal static string KeyOrNone(string key) => key == "" ? "단축키 없음" : KeyNames.Display(key);

    /// <summary>클래스 확인, which 스킬창 다시 맞추기 does: what it compares, or why it can't for this class.</summary>
    string ClassCheckNote() => _chars.ClassCheckable
        ? $"클래스 확인: 스킬창 다시 맞추기 때 스킬창 가운데나 초각스 아이콘이 고른 클래스와 다르면 아덴 게이지가 빨갛게 깜빡이고 게이지 예측을 멈춥니다. 캐릭터 감지는 스킬창 다시 맞추기({KeyOrNone(_s.Hotkeys.DetectCharacter)})를 누를 때만 합니다. 로딩이나 컷신 뒤에는 저절로 바뀌지 않습니다. 가만히 있을 때 누르면 가장 정확합니다."
        : ClassProfiles.ActiveClass(_s) is null
            ? "클래스 확인: 캐릭터를 고르면 그 클래스가 맞는지 게임 화면과 비교합니다."
            : "클래스 확인: 이 클래스는 비교할 그림(Profiles/aden, Profiles/subclass)이 없어 클래스를 확인하지 못합니다. 고른 클래스가 맞는지 직접 확인하세요.";

    // ── 내 오버레이 → 전체, 버프/디버프 ───────────────────────────────────────────

    void FillDisplay()
    {
        var o = _s.Overlay;
        var p = Refill(_secDisplay, "오버레이 표시");
        p.Children.Add(Check("오버레이 표시", o.Visible, v => o.Visible = v, Changed, "끄면 내 오버레이와 파티원 창을 모두 숨깁니다. 트레이 메뉴나 '오버레이 표시/숨기기' 단축키로도 바꿀 수 있습니다. 이 설정 창이 열려 있는 동안에도 켜져 있어야 보입니다. 끄면 미리보기도 숨겨지니, 미리보기를 보려면 켜세요 (레이아웃 편집기가 열려 있을 때는 빼고)."));
        UIElement? opacityRow = null;
        p.Children.Add(Check("배경 판 표시", o.ShowPlate,
            v => { o.ShowPlate = v; opacityRow!.Visibility = Shown(v); }, Changed, "내 오버레이와 파티원 창 뒤의 판입니다. 분리된 버프 창에는 판이 없습니다."));
        p.Children.Add(opacityRow = SliderRow("배경 불투명도", 0.3, 1, o.Opacity, 0.05, v => $"{v * 100:0}%", v => o.Opacity = v, Changed));
        opacityRow.Visibility = Shown(o.ShowPlate);
        CooldownLookRows(p);
        p.Children.Add(Check("화면 캡처에서 숨기기", o.ExcludeFromCapture, v => o.ExcludeFromCapture = v, Changed, "스크린샷, OBS 방송, 디스코드 화면 공유에 오버레이가 찍히지 않습니다. 내 화면에는 그대로 보입니다."));
        p.Children.Add(ColorRow(Label("초각기 표시 색"), o.AwakeningChangedColor, v => o.AwakeningChangedColor = v, swatch: false, warnDark: true));

        p.Children.Add(Heading("금색 테두리", help: "게임에서 스킬 칸에 금색 테두리가 뜨면 오버레이의 그 칸에도 테두리를 그립니다. 두께는 칸 안쪽으로 늘어나고, 0이면 그리지 않습니다. 금색과 비슷한 칸 색은 스킬 칸의 색 항목에서 알려 줍니다."));
        p.Children.Add(ColorRow(Label("테두리 색"), o.GoldColor, v => { o.GoldColor = v; Dispatcher.BeginInvoke(FillClassSections); }, swatch: false));   // their warnings
        p.Children.Add(SliderRow("테두리 두께", 0, 8, o.GoldThickness, 0.5, v => v == 0 ? "끔" : $"{v:0.#} px", v => o.GoldThickness = v, Changed));
        p.Children.Add(ResetButton("오버레이 표시 되돌리기", "이 화면의 표시, 불투명도, 시간 숫자, 쿨타임 표시, 초각기 표시, 금색 테두리를 되돌립니다. 위치, 크기, 판 색은 남습니다.",
            () => SettingsResets.Display(_s)));
        FillBuffs();
    }

    /// <summary>How every box shows its cooldown: 칸 불투명도, 남은 시간 숫자 (and its size), 쿨타임 표시 (and 채움 방향).
    /// The same rows in 내 오버레이 → 전체 and in the editor's 창 전체.</summary>
    void CooldownLookRows(Panel p)
    {
        var o = _s.Overlay;
        p.Children.Add(SliderRow("칸 불투명도", 0.2, 1, o.BoxOpacity, 0.05, v => $"{v * 100:0}%", v => o.BoxOpacity = v, Changed));
        double maxFont = Math.Max(OverlayRenderer.MinTimerFont, OverlayRenderer.MaxTimerFont(o.Width, o.Height));   // the box size is the editor's
        UIElement? fontRow = null, fromRow = null;
        p.Children.Add(Check("남은 시간 숫자 표시", o.ShowTimer, v => { o.ShowTimer = v; fontRow!.Visibility = Shown(v); }, Changed));
        p.Children.Add(fontRow = SliderRow("   숫자 크기", OverlayRenderer.MinTimerFont, maxFont, Math.Min(o.TimerFont, maxFont),
            OverlayRenderer.TimerFontStep, v => $"{v:0.#}", v => o.TimerFont = v, Changed));
        fontRow.Visibility = Shown(o.ShowTimer);
        p.Children.Add(Row(HelpLabel("쿨타임 표시", "빗금은 준비될 때까지 남아서, 거의 다 찬 칸을 준비된 칸으로 착각하지 않게 합니다. 충전 스킬은 쓸 수 있는 횟수가 남아 있으면 원래 색에 흰 선으로, 다 쓰면 회색으로 표시됩니다."), EnumCombo([(CooldownStyle.Sweep, "회전"), (CooldownStyle.Fill, "채움 (어두운 색 → 밝은 색)"),
            (CooldownStyle.FillHatched, "채움 (원래 색 + 빗금)")], o.CooldownStyle,
            v => { o.CooldownStyle = v; fromRow!.Visibility = Shown(v != CooldownStyle.Sweep); })));
        p.Children.Add(fromRow = Row("   채움 방향", EnumCombo([(FillFrom.Bottom, "아래 → 위"), (FillFrom.Top, "위 → 아래"), (FillFrom.Left, "왼쪽 → 오른쪽"), (FillFrom.Right, "오른쪽 → 왼쪽")],
            o.FillFrom, v => o.FillFrom = v)));
        fromRow.Visibility = Shown(o.CooldownStyle != CooldownStyle.Sweep);
    }

    /// <summary>버프/디버프: the buff bars, then under a line the raid debuffs, each part with its own reset.</summary>
    void FillBuffs()
    {
        var b = _s.Buffs;
        var p = Refill(_secBuffs, "버프", "내 버프 줄에서 공증, 아덴 피증, 초각스 피증, 뎀증을 찾아 남은 시간을 막대로 보여 줍니다. 파티원에게는 보내지 않습니다. 모두 끄면 버프 줄을 읽지 않습니다.");
        p.Children.Add(Row("게임의 버프 크기 (%)", BuffSizeCombo()));
        p.Children.Add(HelpRow("막대 위치", "분리된 창: 버프 막대가 오버레이 판에서 빠져 자기 창에 그려지고, 편집 모드에서 그 창을 끌어 옮기거나 오른쪽 아래 모서리로 크기를 바꿉니다. " + "세로: 막대를 세워 옆으로 나란히 둡니다 (판과 함께면 판 왼쪽 바깥). 끄면 눕혀 위아래로 쌓습니다 (판과 함께면 칸 아래에 두 개씩).", Check("분리된 창", b.Detached, v => b.Detached = v, Changed), Check("세로 (시간 숫자 없음)", b.Side, v => b.Side = v, Changed)));
        p.Children.Add(Row("줄어듦", Check("남은 시간만큼 줄어듦", b.Drain, v => b.Drain = v, Changed, "끄면 버프가 있는 동안 막대가 가득 찬 채로 있습니다.")));
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
        p.Children.Add(Heading("디버프", first: true, help: "레이드를 고르면 MP 바 위의 디버프 줄(버프 줄의 반대편)과 보스 체력바 아래의 빨강·파랑 숫자를 읽어 디버프 창에 보여 줍니다: " + "내 색, 교체 때 데려갈 인원(빨강과 파랑이 반반이 되도록), 번개(남은 초, 깜빡임). 번개 칸에는 결투자도 보입니다(4에서 0까지 카운트다운 뒤 아이콘, 처음 보인 뒤 30초 동안 유지). 편집 모드에서 창을 옮기고 크기를 바꿉니다. " + "보스 숫자는 아직 0, 1, 4, 6, 8만 읽고, 둘 다 확실히 읽히지 않으면 데려갈 인원 칸에 ?를 띄웁니다."));
        // The box sizes only while a raid is picked: with 없음 there is no debuff window to size.
        var sizes = new CardStack { Visibility = Shown(_s.Debuffs.Raid != Raid.없음) };
        p.Children.Add(Row("레이드", EnumCombo([.. Enum.GetValues<Raid>().Select(r => (r, Tray.RaidName(r)))], _s.Debuffs.Raid,
            v => { _s.Debuffs.Raid = v; sizes.Visibility = Shown(v != Raid.없음); })));
        sizes.Children.Add(SubHeading("칸 크기", "칸마다 너비와 높이를 바꿉니다 (100% = 창 크기에 맞춘 칸). 창은 칸에 맞춰 커집니다. " + "레이아웃 편집기에서 칸을 눌러 바꾸거나, 칸 옮기기를 켜고 칸의 오른쪽 아래 모서리를 끌어도 됩니다."));
        for (int i = 0; i < DebuffSettings.BoxCount; i++)
            foreach (var row in DebuffSizeSliders(i, DebuffNames[i] + " ")) sizes.Children.Add(row);
        sizes.Children.Add(ResetButton("디버프 칸 크기 되돌리기", "디버프 칸 크기를 되돌립니다. 고른 레이드와 창의 위치와 크기는 남습니다.", () => SettingsResets.Debuffs(_s)));
        p.Children.Add(sizes);
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
        return [SliderRow(indent + "길이", BarSizes.MinLength, BarSizes.MaxLength, i.Length, 0.05, v => $"{v * 100:0}%", v => b.Edit(k).Length = v, Changed, "길이와 두께는 모든 배치에 적용됩니다. 칸을 옮긴 배치에서는 옮겨 둔 막대 크기를 100%로 봅니다. 레이아웃 편집기의 칸 옵션에서도 바꿀 수 있습니다."),
            SliderRow(indent + "두께", BarSizes.MinThickness, BarSizes.MaxThickness, i.Thickness, 0.25, v => $"{v * 100:0}%", v => b.Edit(k).Thickness = v, Changed)];
    }

    // ── 키·단축키 ───────────────────────────────────────────

    void FillKeys()
    {
        var p = Refill(_secKeys, "게임 키");
        p.Children.Add(BuildKeyPanel(_s, () => { Changed(); Dispatcher.BeginInvoke(() => { FillClassSections(); FillChips(); FillBox(); FillHotkeys(); FillCharacter(); }); }));
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
        p.Children.Add(Row("스킬창 다시 맞추기 (캐릭터 감지 포함)", Key(nameof(h.DetectCharacter), () => h.DetectCharacter, v => h.DetectCharacter = v)));
        // A key that may be left unset, with 지우기 (off while there is nothing to clear).
        FormRow Clearable(string label, string action, Func<string> get, Action<string> set)
        {
            var clear = new Button { Content = "지우기", IsEnabled = get() != "" };
            clear.Click += (_, _) => { set(""); FillHotkeys(); Changed(); };
            return Row(label, Key(action, get, v => { set(v); clear.IsEnabled = v != ""; }), clear);
        }
        p.Children.Add(Clearable("직업각인 다시 감지", nameof(h.DetectSubclass), () => h.DetectSubclass, v => h.DetectSubclass = v));
        p.Children.Add(Clearable("아이콘 다시 읽기", nameof(h.RereadIcons), () => h.RereadIcons, v => h.RereadIcons = v));
        p.Children.Add(HelpRow("테스트 모드 (맨 앞 창)", "로스트아크 대신 맨 앞 창(게임 영상 등)을 읽어 오버레이를 시험합니다. 다시 누르면 끕니다. 트레이 → 테스트 모드에서 창을 고를 수도 있습니다. 테스트 모드에서 맞춘 스킬창은 저장하지 않습니다.", Key(nameof(h.TestMode), () => h.TestMode, v => h.TestMode = v)));
        // 오버레이 프로필 1–5 (위치, 크기, 판 색; set up in the 레이아웃 편집기): unset by default, so each can be cleared again.
        (Func<string>, Action<string>)[] profiles = [(() => h.Profile1, v => h.Profile1 = v), (() => h.Profile2, v => h.Profile2 = v),
            (() => h.Profile3, v => h.Profile3 = v), (() => h.Profile4, v => h.Profile4 = v), (() => h.Profile5, v => h.Profile5 = v)];
        for (int i = 0; i < profiles.Length; i++)
            p.Children.Add(Clearable($"오버레이 프로필 {i + 1}", $"Profile{i + 1}", profiles[i].Item1, profiles[i].Item2));
        p.Children.Add(clash);
        Mark();
        p.Children.Add(ResetButton("단축키 되돌리기", "단축키를 기본값으로 되돌립니다. 오버레이 프로필 단축키는 지워집니다.", () => SettingsResets.Hotkeys(_s)));
    }

    /// <summary>Inline two-step confirm (no modal): the first click relabels the button, a second click within
    /// 5 s resets, otherwise it reverts. The label names what it resets; the tooltip says what stays.</summary>
    Button ResetButton(string idle, string consequence, Action reset)
    {
        var b = new Button { Content = idle, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = consequence };
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
        var box = new TextBox { Text = value, Width = 96, VerticalContentAlignment = VerticalAlignment.Center };
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
                if (d) warn.Text = $"{Josa.With(string.Join(", ", groups.First(g => g.Contains(id)).Where(o => o != id)))} 겹칩니다";
                warn.Visibility = d ? Visibility.Visible : Visibility.Collapsed;
                if (d) { field.SetResourceReference(Control.BorderBrushProperty, CriticalBrushKey); field.BorderThickness = new(2); }
                else { field.ClearValue(Control.BorderBrushProperty); field.ClearValue(Control.BorderThicknessProperty); }
            }
        }

        foreach (var id in SlotInfo.All)
        {
            var field = KeyField(() => s.Keys.GetValueOrDefault(id, ""), key => { s.Keys[id] = key; changed(); Refresh(); });
            var warn = new TextBlock { Text = "다른 칸과 겹칩니다", VerticalAlignment = VerticalAlignment.Center };
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
    static string SlotWithKey(Settings s, SlotId id) => $"{id} ({KeyLabel(s, id) ?? "?"})";

    /// <summary>The user's key for <paramref name="id"/> as shown, or null when none is set.</summary>
    static string? KeyLabel(Settings s, SlotId id) => s.Keys.TryGetValue(id, out var k) && k != "" ? KeyNames.Display(k) : null;

    /// <summary>The user's key for <paramref name="slot"/>, or the slot's own name when none is set (the table's and effect lines' wording).</summary>
    string SlotKeyName(SlotId slot) => KeyLabel(_s, slot) ?? slot.ToString();

    internal static StackPanel Stack(params UIElement[] children)
    {
        var p = new StackPanel();
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    internal static TextBlock Heading(string text, bool first = false, string? help = null) =>
        Break(Helped(new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new(0, first ? 0 : 20, 0, 8) }, text, help));

    /// <summary>A heading stands between setting cards, not in one.</summary>
    static TextBlock Break(TextBlock t)
    {
        CardStack.SetBreak(t, true);
        return t;
    }

    /// <summary>A heading inside a part of a section (a buff, 칸 크기), a step under <see cref="Heading"/>.</summary>
    static TextBlock SubHeading(string text, string? help = null) =>
        Break(Helped(new() { FontWeight = FontWeights.SemiBold, Opacity = 0.85, Margin = new(0, 12, 0, 6) }, text, help));

    // ── 도움말 (?): the form shows the names; what each does is in the ? beside it ──────────────────

    /// <summary>A small ? whose tooltip explains the setting beside it, on hover or keyboard focus (Tab reaches it).</summary>
    internal static FrameworkElement HelpMark(string help)
    {
        var glyph = new TextBlock { Text = "?", FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Border
        {
            Width = 16, Height = 16, CornerRadius = new(8), BorderThickness = new(1), Margin = new(5, 0, 0, 0), Opacity = 0.75,
            Background = Brushes.Transparent, Child = glyph, Focusable = true, Cursor = Cursors.Help, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 },
        };
        mark.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(TextBlock.Foreground)) { Source = glyph });
        KeyboardNavigation.SetIsTabStop(mark, true);
        ToolTipService.SetInitialShowDelay(mark, 100);
        ToolTipService.SetShowDuration(mark, int.MaxValue);
        ToolTipService.SetShowsToolTipOnKeyboardFocus(mark, true);
        ToolTipService.SetShowOnDisabled(mark, true);   // rows greyed while a class follows 기본 still explain themselves
        // Inside a check box's label: clicking the ? doesn't tick the box.
        mark.PreviewMouseLeftButtonDown += (_, e) => { mark.Focus(); e.Handled = true; };
        System.Windows.Automation.AutomationProperties.SetName(mark, "도움말");
        System.Windows.Automation.AutomationProperties.SetHelpText(mark, help);
        return mark;
    }

    /// <summary><paramref name="t"/> showing <paramref name="text"/>, then the ? when there is <paramref name="help"/>; it
    /// follows the last word when the text wraps.</summary>
    static TextBlock Helped(TextBlock t, string text, string? help)
    {
        if (help is null) { t.Text = text; return t; }
        t.Inlines.Add(new System.Windows.Documents.Run(text));
        t.Inlines.Add(new System.Windows.Documents.InlineUIContainer(HelpMark(help)) { BaselineAlignment = BaselineAlignment.Center });
        System.Windows.Automation.AutomationProperties.SetName(t, text);   // read as the name, without the ?
        return t;
    }

    /// <summary>A row label with its ?.</summary>
    static TextBlock HelpLabel(string text, string? help) =>
        Helped(new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, text, help);

    internal static TextBlock Note(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new(0, 0, 0, 10) };
        CardStack.SetBreak(note, true);   // a status line, not a setting: it stands between the cards
        return note;
    }

    static TextBlock Label(string text, double opacity = 1) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Opacity = opacity, TextWrapping = TextWrapping.Wrap };

    /// <summary>A row label with its ? saying what the row does.</summary>
    static TextBlock Described(string label, string description) => HelpLabel(label, description);

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
        p.Children.Add(Check("21:9 화면 비율 사용", screen.Wide, v => _s.ManualScreen = _s.ManualScreen! with { Wide = v }, () => { FillFit(); Changed(); }, "게임 설정의 21:9 화면 비율입니다. 더 좁은 화면에서는 위아래에 검은 띠가 생기고, 21:9보다 넓은 화면에서는 화면을 그대로 씁니다."));
    }

    /// <summary>게임의 버프 크기, shown in 버프/디버프 and in 스킬창 맞추기 (every mode); either one changes the same setting.</summary>
    ComboBox BuffSizeCombo()
    {
        var size = Combo();
        size.ItemsSource = Settings.BuffSizes.Append(_s.Buffs.Size).Distinct().Order().ToList();   // a size set with 위치 조정's ± shows too
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

    static FormRow HelpRow(string label, string help, params UIElement[] controls) => Row(HelpLabel(label, help), controls);

    static FormRow Row(UIElement label, params UIElement[] controls) => new(label, Wrap(controls));

    static CheckBox Check(string text, bool value, Action<bool> set, Action changed, string? help = null)
    {
        // A TextBlock, not the string: a long label wraps under itself instead of running off the card.
        var c = new CheckBox { Content = Helped(new() { TextWrapping = TextWrapping.Wrap }, text, help), IsChecked = value, Margin = new(0, 2, 16, 2) };
        if (help is not null) System.Windows.Automation.AutomationProperties.SetName(c, text);
        c.Click += (_, _) => { set(c.IsChecked == true); changed(); };
        return c;
    }

    static UIElement SliderRow(string label, double min, double max, double value, double step,
        Func<double, string> format, Action<double> set, Action changed, string? help = null)
    {
        var text = new TextBlock { Text = format(value), Width = 44, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, Width = 180, MinWidth = 120, TickFrequency = step,
            IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) => { set(e.NewValue); text.Text = format(e.NewValue); changed(); };
        return Row(HelpLabel(label, help), slider, text);
    }
}
