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

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정: four areas of sections (<see cref="BuildShell"/>). Every change edits the <see cref="Settings"/> object at
/// once and raises <see cref="SettingsChanged"/> (no Save button; saving is debounced by the caller). Closing only hides it.</summary>
public partial class SettingsWindow : Window
{
    readonly Settings _s;
    readonly CharacterService _chars;
    string? _lastActive;

    /// <summary>Raised after every edit (and after a reset).</summary>
    public event Action? SettingsChanged;

    /// <summary>캐릭터·인식 → 스킬창 → "스킬창 다시 맞추기".</summary>
    public event Action? RefitRequested;

    /// <summary>인게임 설정 → "시작하기 다시 보기".</summary>
    public event Action? FirstRunRequested;
    /// <summary>인게임 설정 → 스킬창 → "위치 조정".</summary>
    public event Action? AdjustRequested;

    /// <summary>내 오버레이 → 배치 → "편집 모드".</summary>
    public event Action? EditModeRequested;

    /// <summary>파티 → "호스트 시작" / "참가" (with the pasted token) / "나가기" / "토큰 복사".</summary>
    public event Action? HostRequested, LeaveRequested, CopyTokenRequested;
    public event Action<string>? JoinRequested;

    /// <summary>파티 → 보내기·받기 → "업데이트": send your colours, box size and layout to the party again.</summary>
    public event Action? LookUpdateRequested;

    /// <summary>파티 → "내보내기" (host only), with the member's name.</summary>
    public event Action<string>? KickRequested;

    /// <summary>아덴 게이지 → 스킬별 예측 → 이 캐릭터의 배운 값 지우기.</summary>
    public event Action? GainsResetRequested;

    /// <summary>인게임 설정 → "스킬 등록" / "등록된 스킬 지우기", for 편집할 직업각인; null from a fight record: the played one.</summary>
    public event Action<(ClassInfo Class, string Sub)?>? SkillsSubmitRequested, SkillsClearRequested;

    /// <summary>캐릭터·인식 → 캐릭터 → "직업각인 다시 읽기".</summary>
    public event Action? SubclassRedetectRequested;
    /// <summary>캐릭터 → "직업각인 등록": the next ready 초각스 becomes the active 직업각인's icon.</summary>
    public event Action? SubclassRegisterRequested;

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

    /// <summary>Shows the last fit result in 인게임 설정 → 스킬창 ("맞춤 완료: …" or the error), with 되돌리기 beside a 테스트 모드
    /// fit request's fine-tune (<paramref name="canRevert"/>).</summary>
    public void SetFitResult(string text, bool canRevert = false)
    {
        (_fitResult, _fitCanRevert) = (text, canRevert);
        FillFit();
    }

    bool _fitCanRevert, _gameRunning = true;

    /// <summary>The game (테스트 모드: the picked window) is running: 위치 조정 can open.</summary>
    public void SetGameRunning(bool running)
    {
        if (running == _gameRunning) return;
        _gameRunning = running;
        FillFit();
    }

    /// <summary>테스트 모드: 되돌리기 beside the fit result.</summary>
    public event Action? RevertFitRequested;

    public void SetSkillsResult(string text)
    {
        _skillsResult = text;
        FillFit();
        SkillsRegistered(text);
    }

    public SettingsWindow(Settings settings, CharacterService characters)
    {
        _s = settings;
        UiMotion.Follow(settings);
        RaidBadgeView.Colors = () => _s.RaidCapsules;
        _chars = characters;
        InitializeComponent();
        UiMotion.PressButtons(this);
        var work = SystemParameters.WorkArea;   // the XAML size can exceed a small or scaled screen (CenterScreen then puts the title bar off it)
        (MinWidth, MinHeight) = (Math.Min(MinWidth, work.Width), Math.Min(MinHeight, work.Height));
        (Width, Height) = (Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        BuildShell();
        BuildUndo();
        (GaugeBox.Visibility, GaugeBox.Margin) = (Visibility.Visible, new());   // the inspector pads it
        BuildCanvasPage();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            // Opened: 편집 중 starts on the character's 직업각인 again (user, 2026-11-13), whatever was picked last time.
            bool reset = _classShown is not null && _classShown != ClassProfiles.ActiveKey(_s);
            if (reset) _classShown = null;
            // Rows still bound to 편집 중's old copy would edit it after RefreshPick drops it: rebuild them.
            if (reset || _copy is not null) BuildAll();
            RefreshPick(reloadIcons: true);   // its icons may have been read since
        };
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
        KeepCopy();
        _canvas?.Relayout();
        RefreshPlacement();
        PreviewChanged();
        RefreshRowMarks();
        RecordChange();
        SettingsChanged?.Invoke();
    }

    /// <summary>Rebuilds every section from the settings object (after it was edited elsewhere, e.g. first run or the tray).</summary>
    public void Reload() => BuildAll();

    void BuildAll()
    {
        StartCollecting();
        try { FillSections(); }
        finally { StopCollecting(); }
        ShowProfileButtons();
        ShowCaptureHidden();   // undo, test mode, a tray pick
        RefreshBadges();
        ShowCanvasShot();   // the profile may be another one
        FillAutoBox();
        _canvas?.Relayout();   // the windows as the settings now say (a raid picked in the tray, another profile)
        RefreshInspector();
        ResetBaseline();   // an edit from elsewhere (tray, first run, undo) is where the next step starts
        RefreshRailOrbs();
    }

    void FillSections()
    {
        _canvas?.ReleaseMouseCapture();   // a drag must not carry on into another profile's spots
        string? active = ClassProfiles.ActiveKey(_s);
        if (active != _lastActive) (_classShown, _lastActive) = (null, active);   // a new character: edit its class
        RefreshPick();   // before the sections: they show what the pick means
        FillCharacter();
        FillFit();
        FillDetect();
        FillDisplay();
        FillApart();
        Collect(GaugeBox);
        FillGauge();
        FillConnect();
        FillOrder();
        FillSend();   // 파티원 모양 comes with the class sections
        FillKeys();
        FillHotkeys();
        FillPerformance();
        FillApi();
        FillWarnings();
        FillRaid();
        FillRaidCount();
        FillMeter();
        FillShow();
        Collect(null);
        FillRecords();   // a list of files, not settings: nothing for search   // 편집 중's line is not a setting row
        FillClassSections();
        Collect(null);   // the box options are one box's
        FillBox();
    }

    /// <summary>Clears a section and starts it with its title and what it is for.</summary>
    StackPanel Refill(StackPanel p, string title, string? lead = null)
    {
        Collect(p);   // its rows go into the search registry (while BuildAll collects)
        ForgetRowMarks(p);
        p.Children.Clear();
        // A section named as its area has the page's title: the ? goes there, not on a second copy of the word.
        if (FeatureMap.Rail.Any(g => g.Areas.Contains(title))) { if (lead is null) _areaHelp.Remove(title); else _areaHelp[title] = lead; }
        else p.Children.Add(Heading(title, first: true, help: lead));   // what the section is for: in its ?
        return p;
    }

    // ── 캐릭터·인식 ───────────────────────────────────────────

    void FillCharacter()
    {
        var p = Refill(_secCharacter, AreaCharacter, "트레이 메뉴에서 고른 캐릭터와 같습니다. 클래스마다 마지막으로 고르거나 읽은 직업각인을 기억합니다. 스킬창을 다시 맞출 때는 그 값을 쓰고, 없을 때만 게임 화면에서 읽습니다.");
        p.Children.Add(BuildCharacterPanel(_s, _chars, () => { Changed(); Dispatcher.BeginInvoke(BuildAll); }));
        if (_chars.ActiveClass is { } cls)
            p.Children.Add(Label(_s.SubclassHistory.TryGetValue(cls.Name, out var sub) ? $"기억한 직업각인: {sub}" : "기억한 직업각인: 없음", 0.8));
        var redetect = new Button { Content = "직업각인 다시 읽기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Gap, 0, 0) };
        redetect.Click += (_, _) => SubclassRedetectRequested?.Invoke();
        redetect.IsEnabled = _chars.ActiveClass is not null && _chars.SameSkillsNote is null;
        p.Children.Add(Wrap(redetect, HelpMark("기억한 값을 무시하고, 초각스가 준비됐을 때 직업각인을 다시 읽어 고칩니다. 인식하지 못하면 그대로 둡니다.")));
        var register = new Button { Content = "직업각인 등록", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Gap, 0, 0) };
        register.Click += (_, _) => SubclassRegisterRequested?.Invoke();
        register.IsEnabled = _chars.Active is { Subclass.Length: > 0 } && _chars.ActiveClass is not null;
        p.Children.Add(Wrap(register, HelpMark("지금 고른 직업각인으로 플레이하면서 누르세요. 초각스가 준비됐을 때 그 아이콘을 이 직업각인의 것으로 저장합니다. 클래스의 직업각인을 모두 등록하면 직업각인 다시 읽기와 인식 방식 '수동'·'자동'을 쓸 수 있습니다.")));
        var detectMode = Combo();
        foreach (var m in Enum.GetValues<ClassDetectMode>()) detectMode.Items.Add(m.ToString());
        detectMode.SelectedIndex = (int)_s.ClassDetect;
        detectMode.SelectionChanged += (_, _) => { _s.ClassDetect = (ClassDetectMode)detectMode.SelectedIndex; Changed(); };
        p.Children.Add(HelpRow("인식 방식", "끄기: 직접 고른 클래스와 직업각인을 그대로 씁니다. 수동: 스킬창을 다시 맞출 때 준비된 초각스 아이콘으로 인식합니다. 자동: 수동에 더해, 전투 밖에서 스킬창이 다시 보일 때마다 확인해 캐릭터 변경을 따라갑니다. 확실하지 않으면 그대로 둡니다. 마을처럼 스킬창이 회색이면 스킬을 쓸 수 있는 곳에 가서 바뀝니다.", detectMode));
        if (_chars.SameSkillsNote is { } same) p.Children.Add(Note(same));
        CoreRows(p);
        p.Children.Add(SubHeading("지원하는 직업각인", "인식 방식 '수동'·'자동': 초각스 아이콘으로 직업각인을 인식합니다. 아덴 게이지와 아덴스킬이 있는 직업각인은 그것도 읽습니다. " + "표에 없는 클래스도 쿨타임, 버프 막대, 파티 공유는 모두 됩니다. NEW는 지난번에 이 화면을 본 뒤 추가된 것입니다."));
        p.Children.Add(SupportTable());
        p.Children.Add(Note(CoresExplained));
    }

    /// <summary>NEW in orange (<see cref="NewBrushKey"/>, set by <see cref="PaintRail"/>).</summary>
    internal static readonly Color NewColor = Color.FromRgb(0xFF, 0x8A, 0x1F);
    static readonly Brush MineBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x3C, 0x9E, 0xE8));

    /// <summary>지원하는 직업각인: one row per 직업각인 the shipped data supports (Core.Data.ClassSupport), slots named by the user's
    /// keys, the active one highlighted; NEW on a 직업각인, or on a feature of one already seen, added since the user last looked.</summary>
    Grid SupportTable()
    {
        var rows = _chars.Support(SlotKeyName);
        var fresh = ClassSupport.New(rows, _s.SeenSupport);
        var grid = new Grid { Margin = new(0, Tokens.Space.Tight, 0, Tokens.Space.Gap) };
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
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(Tokens.Space.Tight, Tokens.Space.Tight, Tokens.Space.Gap, Tokens.Space.Tight), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal };
            if (isNew) { var mark = new System.Windows.Documents.Run("NEW ") { FontWeight = FontWeights.Bold }; mark.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, NewBrushKey); t.Inlines.Add(mark); }
            t.Inlines.Add(new System.Windows.Documents.Run(text));
            return t;
        }
        // WPF wraps Korean between any two letters ("고독한기 / 사"); one block per word breaks only at spaces.
        WrapPanel Words(string text, bool isNew, bool head = false)
        {
            var p = new WrapPanel { Margin = new(Tokens.Space.Tight, Tokens.Space.Tight, Tokens.Space.Gap, Tokens.Space.Tight) };
            if (isNew) { var mark = new TextBlock { Text = "NEW", FontWeight = FontWeights.Bold, Margin = new(0, 0, Tokens.Space.Tight, 0) }; mark.SetResourceReference(TextBlock.ForegroundProperty, NewBrushKey); p.Children.Add(mark); }
            foreach (var word in text.Split(' '))
                p.Children.Add(new TextBlock { Text = word, Margin = new(0, 0, Tokens.Space.Tight, 0), FontWeight = head ? FontWeights.SemiBold : FontWeights.Normal });
            return p;
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] heads = ["직업각인", "직업각인 인식", "아덴 게이지", "아덴스킬", "코어 빌드"];
        for (int c = 0; c < heads.Length; c++) Add(Words(heads[c], false, head: true), 0, c);
        var active = _chars.Active;
        // By 직업군, with rows that have anything new on top until the 캐릭터 tab has been seen (user, 2026-10-13).
        int r = 0;
        foreach (var (heading, group) in ClassSupport.Grouped(rows, fresh))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = Cell(heading, false, head: true);
            title.Margin = new(Tokens.Space.Tight, Tokens.Space.Block, Tokens.Space.Gap, Tokens.Space.Hair);
            if (heading == ClassSupport.NewHeading) title.SetResourceReference(TextBlock.ForegroundProperty, NewBrushKey);
            Add(title, ++r, 0, heads.Length);
            foreach (var row in group) AddRow(row, ++r);
        }
        return grid;

        void AddRow(SupportRow row, int r)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool mine = active is { } a && a.Class == row.Class && a.Subclass == row.Subclass;
            if (mine) Add(new Border { Background = MineBrush, CornerRadius = new(Tokens.Radius.Control) }, r, 0, heads.Length);
            bool rowNew = fresh.Contains(row.Key);
            bool New(string feature) => !rowNew && fresh.Contains($"{row.Key}/{feature}");
            Add(Words($"{row.Class} · {row.Subclass}" + (mine ? " (지금)" : ""), rowNew), r, 0);
            Add(Cell(row.Detect ? "자동" : "직접 고름", row.Detect && New("감지")), r, 1);
            Add(Cell(row.Gauge is { } g ? $"{g.Read} 표시: {g.Shown}" : "–", row.Gauge is not null && New("게이지")), r, 2);
            Add(Cell(row.Skills ?? "–", row.Skills is not null && New("스킬")), r, 3);
            Add(Cell(row.Cores ?? "–", row.HasCoreData && New("코어")), r, 4);
        }
    }

    /// <summary>The 캐릭터 section was shown: what it lists counts as seen, so NEW is gone next time (it stays while on screen).</summary>
    void MarkSupportSeen()
    {
        var keys = ClassSupport.Keys(_chars.Support(slot => slot.ToString()));
        if (_s.SeenSupport is { } seen && keys.All(seen.Contains)) return;
        _s.SeenSupport = [.. (_s.SeenSupport ?? []).Union(keys)];
        Changed();
    }

    /// <summary>인게임 설정 (reading-place.plan.md A): 화면 맞추기, 게임 설정 값, 표시할 화면, 인식이 어긋날 때, 스킬 등록.</summary>
    void FillFit()
    {
        FillScreenBox();   // the canvas box shows the same values
        var p = Refill(_secFit, SecFit, "게임 화면에 스킬창을 맞추고, 게임 설정 값과 오버레이를 띄울 모니터를 앱에 알려 줍니다.");
        FitRows(p);
        GameSizeRows(p);
        ScreenDisplayRows(p);
        AdjustRows(p);
        SkillRows(p);
    }

    /// <summary>화면 맞추기: the fits there are, 스킬창 위치 and 직접 입력's values, 자동으로 맞추기 (not under 자동), then 스킬창 (다시)
    /// 맞추기 and its result.</summary>
    void FitRows(StackPanel p)
    {
        p.Children.Add(SubHeading("화면 맞추기", "스킬을 쓸 수 있는 곳에서 모든 스킬 쿨타임이 끝난 채로 가만히 서서 스킬창 다시 맞추기를 누르세요 (수련장에서는 쿨초). 해상도나 HUD 크기를 바꾼 뒤에도 다시 맞추세요. 맞추면서 클래스도 확인하고, 직업각인은 기억한 값이 있으면 그 값을 씁니다."));
        // ponytail: read when the page fills (open, game found/lost, a fit); a test fit found later shows on the next refill
        bool test = GameWindow.TestMode;
        foreach (var row in FitList.Rows(_s, GameArea?.Invoke(), test, test ? FitHud?.Invoke() : null))
        {
            var name = Label(row.Label, row.InUse ? 1 : 0.6);
            if (row.InUse) name.FontWeight = FontWeights.SemiBold;
            p.Children.Add(Row(name, Value(row.Value + (row.InUse ? "  · 사용 중" : ""))));
        }
        // 스킬창 위치: 자동 / 직접 입력 / 수동 조정 (the last only once one was applied in 수동 위치 조정).
        var mode = _s.FitMode;
        p.Children.Add(Row(HelpLabel("스킬창 위치", FitModeNote(mode) + (mode == FitMode.자동 && _s.Adjusts.Count == 0 ? " 수동 조정은 수동 위치 조정에서 위치를 적용한 뒤 고를 수 있습니다." : "")), FitModeToggles("fitMode", FillFit)));
        if (mode == FitMode.직접입력 && _s.ManualHud is not null)
        {
            p.Children.Add(Row("HUD 크기 (%)", HudControl()));
            ScreenRows(p);
        }
        if (mode != FitMode.자동)   // under 자동 it would do nothing: a greyed second fit button read as a choice (ui-audit P2)
        {
            var auto = new Button { Content = "자동으로 맞추기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Block, 0, 0) };
            auto.Click += (_, _) => { _s.SetFitMode(FitMode.자동); FillFit(); Changed(); RefitRequested?.Invoke(); };
            p.Children.Add(Wrap(auto, HelpMark("해상도, 화면 비율, HUD 크기만 자동으로 찾아 맞춥니다. 직접 입력과 수동 조정의 값은 쓰지 않습니다.")));
        }
        var refit = new Button { Content = FitList.FitButton(_s, GameArea?.Invoke(), test), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Gap, 0, 0) };
        refit.Click += (_, _) => RefitRequested?.Invoke();
        Themes.Primary(refit);   // the section's main action; the others fix a fit that went wrong
        var check = HelpMark(ClassCheckNote());   // the class is checked by this button, so what it checks is said here
        check.ToolTipOpening += (_, _) => ((TextBlock)check.ToolTip).Text = ClassCheckNote();   // the hotkey may have changed in 앱 단축키
        var again = new Button { Content = "시작하기 다시 보기", Margin = new(Tokens.Space.Gap, Tokens.Space.Gap, 0, 0) };
        again.Click += (_, _) => FirstRunRequested?.Invoke();
        p.Children.Add(Wrap(refit, check, again));
        if (_fitResult is not { } result) return;
        p.Children.Add(new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Gap, 0, 0) });
        if (!_fitCanRevert) return;
        var revert = new Button { Content = "되돌리기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Tight, 0, 0), ToolTip = "자동 맞춤 전의 위치로 돌아갑니다." };
        revert.Click += (_, _) => { _fitCanRevert = false; RevertFitRequested?.Invoke(); FillFit(); };
        p.Children.Add(revert);
    }

    /// <summary>게임 설정 값: the in-game sizes the reads follow, all in one place (user, 2026-11-12).</summary>
    void GameSizeRows(StackPanel p)
    {
        p.Children.Add(SubHeading("게임 설정 값", "게임 설정 → 화면과 같은 값으로 맞추세요. 다르면 스킬창, 버프, 단심을 엉뚱한 곳에서 읽습니다."));
        p.Children.Add(HelpRow("버프 크기 (%)", "게임 설정의 버프 크기와 같게 맞추세요. 다르면 버프·디버프를 엉뚱한 곳에서 읽습니다. 스킬창 다시 맞추기로는 찾지 못하는 값입니다. 120%에서 가장 잘 읽힙니다.", BuffSizeCombo()));
        p.Children.Add(HelpRow("버프 표시", BuffShownNote, BuffShownCombo()));
        p.Children.Add(HelpRow("데미지 폰트 크기 (%)", "게임 설정의 데미지 폰트 크기와 같게 맞추세요. 단심이 캐릭터 위의 '심판' 글자를 읽을 때 씁니다.", FontSizeCombo()));
        p.Children.Add(HelpRow("타겟 UI 버프 크기 (보스) (%)", "게임 설정의 타겟 UI 버프 크기와 같게 맞추세요. 보스 체력바 아래 아이콘(암수 · 빛암, 모르둠 문양)과 빨강·파랑 숫자를 이 크기로 읽습니다. 내 버프·디버프 크기(게임의 버프 크기)와는 별개입니다.", BossSizeCombo()));
        p.Children.Add(HelpRow("타겟 UI 버프 표시", BossShownNote, BossShownCombo()));
        p.Children.Add(Defaulted(SliderRow("타겟 UI 버프 읽을 칸 수", 1, Core.Vision.Geometry.MaxIconsAny, _s.Debuffs.BossCount, 1, v => BossCountText((int)v), v => _s.Debuffs.BossCount = (int)Math.Round(v), Changed, BossCountNote),
            s => s.Debuffs.BossCount, (s, v) => s.Debuffs.BossCount = v));
    }

    /// <summary>인식이 어긋날 때: 수동 위치 조정, the layer over the game with every box.</summary>
    void AdjustRows(StackPanel p)
    {
        p.Children.Add(SubHeading("인식이 어긋날 때"));
        var adjust = WhyTip(new Button { Content = "수동 위치 조정", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Tight, 0, Tokens.Space.Tight), IsEnabled = _gameRunning },
            "게임(테스트 모드: 고른 창)이 실행 중일 때 쓸 수 있습니다.");
        adjust.Click += (_, _) => AdjustRequested?.Invoke();
        p.Children.Add(Wrap(adjust, HelpMark("게임 화면 위에 읽는 상자를 모두 띄웁니다. 스킬창과 버프·디버프·보스 줄을 끌거나 화살표 키로 맞춘 뒤 적용하세요. 잘 읽히면 상자가 초록색이 됩니다. 적용하기 전의 위치는 이 화면이 열려 있는 동안만 쓰입니다. 한 칸만 맞출 때는 화면 인식 설정에서 그 칸을 고르세요.")));
    }

    const double SavedSkillIcon = 28;   // the saved skills' icons beside their 직업각인

    /// <summary>스킬 등록 for 편집할 직업각인 (reading-place.plan.md A): what each 직업각인 of its class has saved, 스킬 등록 and
    /// 등록된 스킬 지우기 (Danger, asks first). Off for 기본, with why.</summary>
    void SkillRows(StackPanel p)
    {
        p.Children.Add(SubHeading("스킬 등록", "편집할 직업각인으로 플레이하면서 일반 스킬이 모두 준비됐을 때 누르세요. 그 직업각인의 스킬로 저장됩니다.\n초각스 아이콘으로 직업각인을 못 읽을 때(마을처럼 스킬창이 회색일 때) 등록한 스킬로 고릅니다. 일반 스킬 8개 중 5개 이상이 맞는 직업각인을 고르고, 스킬 위치는 상관없습니다. 두 직업각인이 같이 쓰는 스킬은 빼고 비교합니다. 등록하지 않은 직업각인은 스킬로 고르지 않습니다.\n다른 스킬 구성도 더 등록할 수 있습니다."));
        var into = ShownSubclass;
        var saved = into is { } t ? _chars.SavedSkills(t.Class) : null;   // ponytail: read from disk at each refill; a pool is a few small files
        if (into is { } shown && saved is not null)
            foreach (var (sub, skills) in saved)
                p.Children.Add(Row(SkillOwner(shown.Class.Name, sub, sub == shown.Sub), SavedSkillsView(skills)));
        string? blocked = into is null ? "기본에는 스킬을 등록하지 않습니다. 위의 편집할 직업각인에서 직업각인을 고르세요." : CharacterService.SkillsBlockedFor(into);
        var submit = WhyTip(new Button { Content = "스킬 등록", Margin = new(0, 0, Tokens.Space.Gap, 0), IsEnabled = blocked is null }, blocked ?? "");
        submit.Click += (_, _) => SkillsSubmitRequested?.Invoke(into);
        int count = into is { } c && saved!.TryGetValue(c.Sub, out var mine) ? mine.Count : 0;
        var clear = UiBuild.Danger(WhyTip(new Button { Content = "등록된 스킬 지우기", IsEnabled = count > 0 }, "이 직업각인에 저장된 스킬이 없습니다."));
        clear.Click += (_, _) =>
        {
            if (into is not { } target) return;
            // read again: the tray may have registered skills since the page was filled
            int now = _chars.SavedSkills(target.Class).TryGetValue(target.Sub, out var current) ? current.Count : 0;
            if (now == 0) { FillFit(); return; }
            if (MessageBox.Show(this, $"{target.Class.Name} · {target.Sub}에 저장된 스킬 {now}개를 지울까요? 다시 쓰려면 스킬을 새로 등록해야 합니다.", SecFit, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            SkillsClearRequested?.Invoke(target);
        };
        p.Children.Add(new WrapPanel { Children = { submit, clear }, Margin = new(0, Tokens.Space.Gap, 0, 0) });
        if (into is { Class.SameSkills: true } same) p.Children.Add(Note($"{same.Class.Name}: 두 직업각인의 스킬이 같아 스킬로 구분할 수 없으므로 등록하지 않습니다."));
        if (_skillsResult is { } result) p.Children.Add(new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Gap, 0, 0) });
    }

    /// <summary>"클래스 · 직업각인", the one 스킬 등록 saves to in bold.</summary>
    TextBlock SkillOwner(string cls, string sub, bool target)
    {
        var name = Label($"{cls} · {sub}" + (target ? " (편집할 직업각인)" : ""), target ? 1 : 0.6);
        if (target) name.FontWeight = FontWeights.SemiBold;
        return name;
    }

    /// <summary>"N개 저장됨" with the saved icons, or "저장된 스킬 없음".</summary>
    FrameworkElement SavedSkillsView(IReadOnlyList<Core.Domain.Image> skills)
    {
        if (skills.Count == 0) return Value("저장된 스킬 없음");
        var w = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        w.Children.Add(new TextBlock { Text = $"{skills.Count}개 저장됨", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, Tokens.Space.Gap, 0) });
        foreach (var icon in skills)
            w.Children.Add(new System.Windows.Controls.Image { Source = OverlayRenderer.ToBitmap(icon), Width = SavedSkillIcon, Height = SavedSkillIcon, Margin = new(0, 0, Tokens.Space.Hair, 0) });
        return w;
    }

    /// <summary>설정 → 시스템 성능 (user, 2026-11-11): the two rates, moved here from 스킬창 맞추기.</summary>
    void FillPerformance()
    {
        var p = Refill(_secPerformance, AreaPerformance);
        p.Children.Add(SliderRow("초당 읽기 횟수", ReadSteps.Min, ReadSteps.Max, _s.ReadsPerSecond, 1, v => $"{v:0}회", v => _s.ReadsPerSecond = (int)v, Changed,
            "화면을 1초에 몇 번 읽을지 정합니다. 높을수록 정확하지만 CPU를 더 씁니다. 모니터 주사율(60·144 Hz)과 상관없이 이 횟수를 넘지 않습니다. 프레임이 떨어지면 낮춰 보세요. 기본 20."));
        p.Children.Add(SliderRow("초당 그리기 횟수", DrawSteps.Min, DrawSteps.Max, _s.DrawsPerSecond, 1, v => $"{v:0}회", v => _s.DrawsPerSecond = (int)v, Changed,
            "줄어드는 막대, 반짝임, 깜빡임처럼 움직이는 것이 있을 때 오버레이를 1초에 몇 번 다시 그릴지 정합니다. 높을수록 부드럽지만 CPU를 조금 더 씁니다. 움직이는 것이 없으면 다시 그리지 않습니다. 모니터 주사율에 맞춰 가까운 횟수로 그립니다(60 Hz에서 45는 30처럼). 기본 30."));
    }

    /// <summary>A hotkey as shown, or that there is none.</summary>
    internal static string KeyOrNone(string key) => key == "" ? "단축키 없음" : KeyNames.Display(key);

    /// <summary>클래스 확인, which 스킬창 다시 맞추기 does: what it compares, or why it can't for this class.</summary>
    string ClassCheckNote() => _chars.ClassCheckable
        ? $"클래스 확인: 스킬창 다시 맞추기({KeyOrNone(_s.Hotkeys.DetectCharacter)})를 누를 때 합니다. 인식 방식이 '자동'이면 전투 밖에서 스킬창이 다시 보일 때도 합니다. 스킬창 가운데나 초각스 아이콘이 고른 클래스와 다르면 아덴 게이지가 빨갛게 깜빡이고 게이지 예측을 멈춥니다. 가만히 서서 누르면 가장 정확합니다."
        : ClassProfiles.ActiveClass(_s) is null
            ? "클래스 확인: 캐릭터를 고르면 그 클래스가 맞는지 게임 화면과 비교합니다."
            : "클래스 확인: 이 클래스는 비교할 그림(Profiles/aden, Profiles/subclass)과 초각스가 없어 클래스를 확인하지 못합니다. 고른 클래스가 맞는지 직접 확인하세요.";

    // ── 내 오버레이 → 전체, 버프/디버프 ───────────────────────────────────────────

    void FillDisplay()
    {
        var o = _s.Overlay;
        var p = Refill(_secDisplay, "배경판");
        UIElement? opacityRow = null;   // 오버레이 표시 and 스킬창이 안 보이면 숨기기: 설정 → 기능 Map (FillShow)
        var plate = Target.Overlay;   // 판 is 편집 중's look (방송·캡처에서 숨김 is in the top bar, BuildTopBar)
        p.Children.Add(Check("배경판", plate.ShowPlate,
            v => { plate.ShowPlate = v; opacityRow!.Visibility = Shown(v); }, Changed, "내 오버레이와 파티원 창 뒤에 깔리는 배경판입니다. 분리된 버프 창에는 배경판이 없습니다."));
        p.Children.Add(opacityRow = SliderRow("배경판 불투명도", 0.3, 1, plate.Opacity, 0.05, v => $"{v * 100:0}%", v => plate.Opacity = v, Changed));
        opacityRow.Visibility = Shown(plate.ShowPlate);

        // 칸 모양: how every box shows its cooldown, 초각스, 금테 (the skill inspector)
        p = Refill(_secSkillLook, "칸 모양");
        p.Children.Add(CardBadge(LookGroup.스킬칸));
        var box = Target.Overlay;   // 편집 중's look; 스킬창 읽기 is global (o)
        p.Children.Add(DefaultedCheck(Check("스킬창 읽기", o.SkillRead, v => o.SkillRead = v, Changed,
            "끄면 스킬 쿨을 읽지 않습니다. 쿨타임, 타임라인의 스킬 아이콘, 파티로 보내기가 모두 멈춥니다. 켜 둔 채 스킬 칸만 숨기면 타임라인에 쓰려고 계속 읽습니다."),
            s => s.Overlay.SkillRead, (s, v) => s.Overlay.SkillRead = v));
        CooldownLookRows(p);
        p.Children.Add(Defaulted(ColorRow(Label("초각스 색"), box.AwakeningChangedColor, v => box.AwakeningChangedColor = v, swatch: false, warnDark: true),
            s => s.Overlay.AwakeningChangedColor, (s, v) => s.Overlay.AwakeningChangedColor = v, Target));
        p.Children.Add(Heading("금테 (게임 금색 테두리)", help: "게임에서 스킬 칸에 금색 테두리가 뜨면 오버레이의 그 칸에도 그립니다. 두께는 칸 안쪽으로 늘어나고, 0이면 그리지 않습니다. 칸 색이 금색과 비슷하면 스킬 칸의 색 항목에서 알려 줍니다."));
        p.Children.Add(Defaulted(ColorRow(Label("금테 색"), box.GoldColor, v => { box.GoldColor = v; Dispatcher.BeginInvoke(FillClassSections); }, swatch: false),   // their warnings
            s => s.Overlay.GoldColor, (s, v) => s.Overlay.GoldColor = v, Target));
        p.Children.Add(Defaulted(SliderRow("금테 두께", 0, 8, box.GoldThickness, 0.5, v => v == 0 ? "끔" : $"{v:0.#} px", v => box.GoldThickness = v, Changed),
            s => s.Overlay.GoldThickness, (s, v) => s.Overlay.GoldThickness = v, Target));
        FillBuffs();
        FillJudgment();
        FillAmsu();
    }

    /// <summary>What every class's boxes share: 칸 불투명도 and 쿨 숫자 크기. The same rows in 내 오버레이 → 칸 모양 and in
    /// the editor's 창 전체; the class's own 쿨 진행 표시 and 남은 쿨 (초) are <see cref="ClassCooldownRows"/>.</summary>
    void CooldownLookRows(Panel p)
    {
        var o = _s.Overlay;
        var box = Target.Overlay;   // 편집 중's look; 쿨 숫자 크기 is global (o)
        p.Children.Add(Defaulted(SliderRow("불투명도", 0.2, 1, box.BoxOpacity, 0.05, v => $"{v * 100:0}%", v => box.BoxOpacity = v, Changed),
            s => s.Overlay.BoxOpacity, (s, v) => s.Overlay.BoxOpacity = v, Target));
        double maxFont = Math.Max(OverlayRenderer.MinTimerFont, OverlayRenderer.MaxTimerFont(o.Width, o.Height));   // the box size is the editor's
        p.Children.Add(Defaulted(SliderRow("쿨 숫자 크기", OverlayRenderer.MinTimerFont, maxFont, Math.Min(o.TimerFont, maxFont),
            OverlayRenderer.TimerFontStep, v => $"{v:0.#}", v => o.TimerFont = v, Changed, "모든 클래스의 남은 쿨 숫자 크기입니다. 칸에 들어가는 크기보다 커지지 않습니다."),
            s => s.Overlay.TimerFont, (s, v) => s.Overlay.TimerFont = v));
    }

    /// <summary>The class's 쿨 진행 표시 (style, and beside it the direction of a fill) and 남은 쿨 (초) (display-split.plan.md
    /// step 5): on its 스킬 칸 page and in the editor's 창 전체. Party boxes of the class draw with these too.</summary>
    void ClassCooldownRows(Panel p, MineProfile read, Func<MineProfile> edit)
    {
        void Again() => Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); });   // both places show these
        var from = EnumCombo([(FillFrom.Bottom, "아래 → 위"), (FillFrom.Top, "위 → 아래"), (FillFrom.Left, "왼쪽 → 오른쪽"), (FillFrom.Right, "오른쪽 → 왼쪽")],
            read.FillFrom, v => { edit().FillFrom = v; Again(); });
        System.Windows.Automation.AutomationProperties.SetName(from, "진행 방향");
        from.Visibility = Shown(read.CooldownStyle != CooldownStyle.Sweep);
        p.Children.Add(Row(HelpLabel("쿨 진행 표시", "빗금이 준비될 때까지 남아 있어, 거의 다 찬 칸과 준비된 칸이 헷갈리지 않습니다. 충전 스킬은 쓸 수 있는 횟수가 남아 있으면 원래 색에 흰 선으로, 다 쓰면 회색으로 보입니다. 이 클래스의 파티원 칸도 이렇게 보입니다."),
            EnumCombo([(CooldownStyle.Sweep, "회전"), (CooldownStyle.Fill, "채움 (어두운 색 → 밝은 색)"), (CooldownStyle.FillHatched, "채움 (원래 색 + 빗금)")],
                read.CooldownStyle, v => { edit().CooldownStyle = v; Again(); }), from));
        p.Children.Add(Check("남은 쿨 (초)", read.ShowTimer, v => edit().ShowTimer = v, () => { Changed(); Again(); }));
    }

    /// <summary>버프/디버프: the buff bars, then under a line the raid debuffs, each part with its own reset.</summary>
    void FillBuffs()
    {
        var b = _s.Buffs;
        var p = Refill(_secBuffs, "버프", "내 버프 줄에서 공증, 아덴 피증, 초각스 피증, 초각성기 뎀증을 찾아 남은 시간을 막대로 보여 줍니다. 낙인은 보스 체력바 아래 디버프 줄에서 찾고, 보이는 동안 막대를 가득 채웁니다. 파티원에게는 보내지 않습니다. 모두 끄면 버프 줄을 읽지 않습니다.");
        p.Children.Add(CardBadge(LookGroup.버프막대));
        _buffHeads.Clear();
        _debuffHeads.Clear();
        p.Children.Add(Defaulted(HelpRow("모든 버프", "버프 막대 모두에 적용됩니다. 막대마다는 아래에서 켜기 / 끄기만 고릅니다.", BuffAutoCheck()),
            s => s.Buffs.RaidOnly, (s, v) => s.Buffs.RaidOnly = v));
        p.Children.Add(Note("버프 크기는 인게임 설정 → 게임 설정 값에서 맞춥니다."));
        p.Children.Add(Defaulted(SliderRow("버프 읽을 칸 수", Core.Vision.Geometry.MinBuffBoxCount, Core.Vision.Geometry.MaxIconsAny, b.Count, 1, v => $"{v:0}칸",
            v => b.Count = (int)Math.Round(v), Changed,
            "버프 줄 오른쪽 끝에서부터 몇 칸을 읽을지 정합니다. 이보다 왼쪽에 뜬 버프는 못 읽습니다. 버프가 많이 뜨면 늘리세요. 칸이 늘수록 CPU를 조금 더 씁니다. " +
            "게임은 한 줄에 버프 표시만큼만 그리고 나머지는 '+숫자' 아이콘으로 접습니다 (버프 크기 120%에서 최대 12칸, 100%에서 14칸, 80%에서 17칸이며, 칸 수는 게임 설정의 버프 표시입니다). 그보다 많이 읽어도 더 찾지 못합니다."),
            s => s.Buffs.Count, (s, v) => s.Buffs.Count = v));
        p.Children.Add(ApartRow());
        p.Children.Add(DirectionRow(DockGroup.Buffs, "세로는 막대를 세워 옆으로 나란히 두고, 남은 초는 표시하지 않습니다. 가로는 눕혀서 위아래로 쌓습니다.",
            s => s.Buffs.Side, (s, v) => s.Buffs.Side = v));
        p.Children.Add(Defaulted(Row("움직임", Check("유지시간 비례 감소", b.Drain, v => b.Drain = v, Changed, "끄면 버프가 있는 동안 막대가 가득 찬 채로 있습니다.")), s => s.Buffs.Drain, (s, v) => s.Buffs.Drain = v));
        p.Children.Add(Defaulted(Row("비었을 때", Check("끊긴 버프 막대 숨김", b.HideWhenEmpty, v => b.HideWhenEmpty = v, Changed,
            "버프가 없는 동안 그 막대를 숨깁니다. 다른 막대는 움직이지 않습니다. 편집 모드와 미리보기에서는 옮길 수 있게 흐리게 보입니다.")), s => s.Buffs.HideWhenEmpty, (s, v) => s.Buffs.HideWhenEmpty = v));
        foreach (var k in BuffOrder.Shown)
        {
            var i = b.Of(k);
            bool timed = k != BuffKind.낙인;   // 낙인 is read by its icon alone: no time to show or flash
            string name = k switch { BuffKind.아덴피증 => "아덴 피증", BuffKind.초각스피증 => "초각스 피증", BuffKind.뎀증 => "초각성기 뎀증", _ => k.ToString() };
            var label = Check("남은 초", i.ShowLabel, v => b.Edit(k).ShowLabel = v, Changed);
            var flash = Check("끊기기 1초 전 깜빡임", b.EndFlash(k), v => b.Edit(k).EndFlash = v, Changed);
            flash.ToolTip = "남은 시간이 1초 아래로 내려가면 막대가 줄어드는 방향으로 빛이 지나갑니다.";
            var more = Stack([Defaulted(ColorRow(Label("색"), i.Color, v => b.Edit(k).Color = v, swatch: false), s => s.Buffs.Of(k).Color, (s, v) => s.Buffs.Edit(k).Color = v),
                .. BuffSizeSliders(k)]);
            void Show(bool on) => (label.Visibility, flash.Visibility, more.Visibility) = (Shown(on && timed), Shown(on && timed), Shown(on));
            Show(i.Visible);
            p.Children.Add(_buffHeads[k] = SubHeading(name));
            // One ↺ for 표시 · 남은 초 · 깜빡임; 이 캐릭터에서 숨김 is the character's, not a default to put back.
            p.Children.Add(Defaulted(Row("표시", BuffShowCombo(k, Show), BuffHideHere(k), label, flash),
                s => (s.Buffs.Of(k).Visible, s.Buffs.Of(k).ShowLabel, s.Buffs.EndFlash(k)),
                (s, v) => { var e = s.Buffs.Edit(k); bool f; (e.Visible, e.ShowLabel, f) = v; e.EndFlash = f == new BuffSettings().EndFlash(k) ? null : f; }));
            p.Children.Add(more);
        }

        p.Children.Add(Divider());
        p.Children.Add(Heading("디버프", first: true, help: "디버프 창에 내 색, 교체 때 데려갈 인원(빨강과 파랑이 반반이 되도록), 번개(남은 초, 깜빡임)를 보여 줍니다. " + "레이드의 디버프 창 자동을 켜거나 트레이에서 디버프 창을 켜면 MP 바 위 디버프 줄(버프 줄 반대편)과 보스 체력바 아래 빨강·파랑 숫자를 읽습니다. 번개 칸에는 결투자도 보입니다(4에서 0까지 카운트다운 뒤 아이콘, 처음 보인 뒤 30초 유지). 편집 모드에서 창을 옮기고 크기를 바꿉니다. " + "보스 숫자는 아직 0, 1, 4, 6, 8만 읽습니다. 둘 다 확실히 읽히지 않으면 데려갈 인원 칸에 ?를 띄웁니다."));
        // The box sizes only while a gate's 디버프 창 is on (레이드): otherwise there is no debuff window to size.
        var sizes = new CardStack { Visibility = Shown(_s.Raids.DebuffShown(_s.Debuffs)) };
        var toRaid = new Button { Content = "레이드 열기", Margin = new(0, Tokens.Space.Hair, 0, Tokens.Space.Hair) };
        toRaid.Click += (_, _) => Go(AreaRaid);
        string debuffState = _s.Debuffs.Raid != Raid.없음 ? "켜짐 (트레이에서 켬: 레이드 안에서 늘)" : _s.Raids.DebuffsAuto ? "자동 (보스를 알아본 레이드에서)" : "꺼짐 (레이드에서 자동을 켭니다)";
        p.Children.Add(Row("디버프 창", Value(debuffState), toRaid));
        p.Children.Add(Note("타겟 UI 버프 크기(보스)는 인게임 설정 → 게임 설정 값에서 맞춥니다."));
        sizes.Children.Add(Defaulted(Row("비었을 때", Check("디버프 없을 때 숨김", _s.Debuffs.HideWhenEmpty, v => _s.Debuffs.HideWhenEmpty = v, Changed,
            "디버프가 없는 칸(색을 아직 모를 때, 교체·번개·결투자가 없을 때)을 숨깁니다. 다른 칸은 움직이지 않습니다. 편집 모드와 미리보기에서는 옮길 수 있게 흐리게 보입니다.")), s => s.Debuffs.HideWhenEmpty, (s, v) => s.Debuffs.HideWhenEmpty = v));
        sizes.Children.Add(SubHeading("칸 크기", "칸마다 너비와 높이를 바꿉니다 (100% = 창 크기에 맞춘 칸). 창은 칸에 맞춰 커집니다. " + "화면에서 칸의 오른쪽 아래 모서리를 끌어도 됩니다."));
        for (int i = 0; i < DebuffSettings.BoxCount; i++)
            foreach (var row in DebuffSizeSliders(i, DebuffNames[i] + " ")) { sizes.Children.Add(row); _debuffHeads.TryAdd(i, row); }
        p.Children.Add(sizes);
    }

    /// <summary>A buff bar's 두께, 100% = its spot in the layout (its 길이 is set by dragging its box on 내 오버레이); in 버프/디버프
    /// and in the 레이아웃 편집기's 칸 옵션.</summary>
    UIElement[] BuffSizeSliders(BuffKind k, string indent = "")
    {
        var b = _s.Buffs;
        var i = b.Of(k);
        // Of (not Edit) in the getters: it reads the shared defaults without adding an entry to them.
        return [Defaulted(SliderRow(indent + "두께", BarSizes.MinThickness, BarSizes.MaxThickness, i.Thickness, 0.25, v => $"{v * 100:0}%", v => b.Edit(k).Thickness = v, Changed, "두께는 모든 프로필에 적용됩니다. 막대 길이는 내 오버레이 화면에서 막대 끝(가로는 오른쪽, 세로는 위)을 끌어 바꿉니다. 자유 배치에서는 칸의 모서리를 끕니다."),
                s => s.Buffs.Of(k).Thickness, (s, v) => s.Buffs.Edit(k).Thickness = v)];
    }

    // ── 키·단축키 ───────────────────────────────────────────

    void FillKeys()
    {
        var p = Refill(_secKeys, "게임 키");
        p.Children.Add(BuildKeyPanel(_s, () => { Changed(); Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); FillHotkeys(); FillCharacter(); }); }));
        p.Children.Add(ResetButton("키를 게임 기본값으로", "스킬 키를 게임 기본값으로 되돌립니다.", () => SettingsResets.Keys(_s)));
    }

    /// <summary>The app's hotkeys, those clashing marked red.</summary>
    void FillHotkeys()
    {
        var p = Refill(_secHotkeys, "앱 단축키", "칸을 누른 다음 원하는 조합을 누르세요. 게임 밖에서도 동작합니다.");
        var h = _s.Hotkeys;
        var keys = new Dictionary<string, Button>();
        var clash = Note("빨간 칸은 다른 단축키나 스킬 키와 겹칩니다. 겹치면 하나만 되거나 둘 다 될 수 있습니다.");
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
        p.Children.Add(Row("오버레이 표시 켜기/끄기", Key(nameof(h.ToggleOverlay), () => h.ToggleOverlay, v => h.ToggleOverlay = v)));
        p.Children.Add(Row("스킬창 다시 맞추기 (직업 확인 포함)", Key(nameof(h.DetectCharacter), () => h.DetectCharacter, v => h.DetectCharacter = v)));
        // A key that may be left unset, with 지우기 (off while there is nothing to clear).
        FormRow Clearable(string label, string action, Func<string> get, Action<string> set)
        {
            var clear = WhyTip(new Button { Content = "지우기", IsEnabled = get() != "" }, "지울 키가 없습니다. (없음)이면 이미 비어 있습니다.");
            clear.Click += (_, _) => { set(""); FillHotkeys(); Changed(); };
            return Row(label, Key(action, get, v => { set(v); clear.IsEnabled = v != ""; }), clear);
        }
        p.Children.Add(Clearable("직업각인 다시 읽기", nameof(h.DetectSubclass), () => h.DetectSubclass, v => h.DetectSubclass = v));
        p.Children.Add(Clearable("아이콘 다시 읽기", nameof(h.RereadIcons), () => h.RereadIcons, v => h.RereadIcons = v));
        p.Children.Add(Clearable("즉시 화면 캡처", nameof(h.CaptureNow), () => h.CaptureNow, v => h.CaptureNow = v));
        p.Children.Add(HelpRow("테스트 모드 (맨 앞 창)", "로스트아크 대신 맨 앞 창(게임 영상 등)을 읽어 오버레이를 테스트합니다. 다시 누르면 꺼집니다. 트레이 → 테스트 모드에서 창을 고를 수도 있습니다. 테스트 모드에서 맞춘 스킬창은 저장되지 않습니다.", Key(nameof(h.TestMode), () => h.TestMode, v => h.TestMode = v)));
        // 오버레이 프로필 1–5 (위치, 크기, 판 색; set up in the 레이아웃 편집기): unset by default, so each can be cleared again.
        (Func<string>, Action<string>)[] profiles = [(() => h.Profile1, v => h.Profile1 = v), (() => h.Profile2, v => h.Profile2 = v),
            (() => h.Profile3, v => h.Profile3 = v), (() => h.Profile4, v => h.Profile4 = v), (() => h.Profile5, v => h.Profile5 = v)];
        for (int i = 0; i < profiles.Length; i++)
            p.Children.Add(Clearable($"프로필 {i + 1}", $"Profile{i + 1}", profiles[i].Item1, profiles[i].Item2));
        p.Children.Add(clash);
        Mark();
        p.Children.Add(ResetButton("기본값으로", "단축키를 기본값으로 되돌립니다. 프로필 단축키는 지워집니다.", () => SettingsResets.Hotkeys(_s)));
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
            if (!timer.IsEnabled) { b.Content = "정말 " + idle; timer.Start(); return; }
            timer.Stop();
            b.Content = idle;
            RecordStep();   // BuildAll moves the baseline past the reset: the state before it must be a step now
            reset();
            KeepCopy();   // a reset of 편집 중's copy, before BuildAll takes a new one
            BuildAll();
            Changed();
        };
        return b;
    }

    /// <summary>A colour field with preview. <paramref name="swatch"/> shows a ready and a cooldown box; those rows
    /// (and <paramref name="warnDark"/> ones) warn when the colour is too dark to show the ready/cooldown step, and
    /// <paramref name="warnGold"/> rows when it is hard to tell from the gold frame (금색 테두리).</summary>
    UIElement ColorRow(UIElement label, string value, Action<string> set, bool swatch, bool warnDark = false, bool warnGold = false)
    {
        var preview = new Border { Width = swatch ? 64 : 22, Height = swatch ? 34 : 22, CornerRadius = new(swatch ? 0 : 2) };
        var warn = new TextBlock { Margin = new(0, Tokens.Space.Hair, 0, Tokens.Space.Hair), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        warn.SetResourceReference(TextBlock.ForegroundProperty, CautionBrushKey);
        void Show(Color c)
        {
            if (swatch) preview.Child = new System.Windows.Controls.Image { Source = OverlayRenderer.Swatch(c), Width = 64, Height = 34 };
            else preview.Background = new SolidColorBrush(c);
            var hex = UiColor.ToHex(c);
            var problems = new List<string>();
            if ((swatch || warnDark) && OverlayRenderer.Lightness(c) < MinLightness) problems.Add("어두운 색은 준비/대기 구분이 약해집니다");
            if (warnGold && UiColor.TryParse(Target.Overlay.GoldColor) is not null && Core.Vision.ColorDistance.CloseTo(Target.Overlay.GoldColor, hex))
                problems.Add("금테와 비슷함: 금색 테두리가 잘 보이지 않을 수 있습니다");
            warn.Text = string.Join(" · ", problems);
            warn.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        var box = new TextBox { Text = value, Width = 96, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) =>
        {
            if (UiColor.TryParse(box.Text) is not { } c)
            {
                box.SetResourceReference(Control.BorderBrushProperty, CriticalBrushKey);
                box.BorderThickness = new(2);
                return;
            }
            box.ClearValue(Control.BorderBrushProperty);
            box.ClearValue(Control.BorderThicknessProperty);
            set(UiColor.ToHex(c));
            Show(c);
            Changed();
        };
        var pick = new Button { Content = "색 선택…" };
        pick.Click += (_, _) =>
        {
            if (UiColor.Pick(UiColor.TryParse(box.Text) is not null ? box.Text : value) is { } picked) box.Text = picked;
        };
        Show(UiColor.Parse(value, Colors.Gray));
        return Row(label, Stack(Wrap(preview, box, pick), warn));
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

    /// <summary>캐릭터 (user, 2026-11-19): one "클래스 · 직업각인" list with 직업 검색, built like 편집할 직업각인's (★ first, 기본 (no
    /// class) at the top) but in the tray's 직업군 order. Picking an entry makes that character active, as the tray does (<see cref="CharacterService.Pick"/>).</summary>
    internal static UIElement BuildCharacterPanel(Settings s, CharacterService chars, Action changed)
    {
        // key → (직업군, class, 직업각인), in the tray's 직업군 order
        var whose = chars.ClassTree.SelectMany(f => f.Classes.SelectMany(c => c.Subclasses.Select(sub => (Key: Settings.CharacterKey(c.Class, sub), f.Family, c.Class, Sub: sub))))
            .ToDictionary(x => x.Key);
        string current = s.ActiveCharacter is { } k && whose.ContainsKey(k) ? k : ClassProfiles.Template;
        var entries = ClassPicker.FavoritesFirst([ClassProfiles.Template, .. whose.Keys], s.FavoriteClasses);
        var pick = PickCombo(entries, current, key =>
        {
            if (whose.TryGetValue(key, out var w)) chars.Pick(w.Family, w.Class, w.Sub);
            else chars.PickDefault();   // 기본: no class
            changed();
        }, s.FavoriteClasses);
        pick.MinWidth = 220;
        System.Windows.Automation.AutomationProperties.SetName(pick, "직업각인");
        return Row("직업각인", pick, PickSearch(pick, "직업각인"));
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

    /// <summary>스킬창 위치's choices (자동 / 직접 입력 / 수동 조정) as toggles in radio group <paramref name="group"/>; a pick saves, then <paramref name="refill"/>s the page.</summary>
    WrapPanel FitModeToggles(string group, Action refill)
    {
        var mode = _s.FitMode;
        var modes = new WrapPanel();
        foreach (var (m, text) in new[] { (FitMode.자동, "자동"), (FitMode.직접입력, "직접 입력"), (FitMode.수동조정, "수동 조정") })
        {
            var r = Toggle(text, group, m == mode);
            r.IsEnabled = m != FitMode.수동조정 || _s.Adjusts.Count > 0 || mode == FitMode.수동조정;
            if (m == FitMode.수동조정) WhyTip(r, "수동 위치 조정으로 맞춘 값이 있어야 고를 수 있습니다.");
            r.Checked += (_, _) =>
            {
                if (_s.FitMode == m) return;
                _s.SetFitMode(m);
                Changed();
                Dispatcher.BeginInvoke(refill);
            };
            modes.Children.Add(r);
        }
        return modes;
    }

    static string FitModeNote(FitMode mode) => mode switch
    {
        FitMode.직접입력 => "찾지 않고 게임 설정에 적힌 값을 그대로 씁니다. 스킬창 자리만 확인한 뒤 클래스와 직업각인을 읽습니다. 처음 값은 마지막으로 찾은 값입니다.",
        FitMode.수동조정 => "수동 위치 조정에서 옮기고 적용한 위치를 씁니다. 게임 화면 크기마다 따로 저장되고, 적용한 적 없는 크기에서는 자동으로 맞춘 값을 씁니다.",
        _ => "맞추기는 화면의 검은 띠와 HP 바로 해상도와 HUD 크기를 찾습니다.",
    };

    /// <summary>The game's 해상도 list (게임 설정 → 화면); anything else is 커스텀 화면크기.</summary>
    static readonly (int W, int H)[] Resolutions = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1080), (2560, 1440), (3440, 1440), (3840, 1600), (3840, 2160), (5120, 2160)];
    const string Custom = "커스텀 화면크기";
    bool _customScreen;   // 커스텀 화면크기 picked, even while its size is one of the list's

    /// <summary>직접 입력's 해상도 (a list, 커스텀 화면크기 with its own width and height) and 21:9.</summary>
    /// <param name="compact">The 표시 map's narrow box: fields stack and a change refills with <paramref name="again"/> (default 스킬창 맞추기's page).</param>
    void ScreenRows(StackPanel p, Action? again = null, bool compact = false)
    {
        again ??= () => Dispatcher.BeginInvoke(FillFit);
        UIElement Line(string name, params UIElement[] controls) => compact ? Field(name, controls.Length == 1 ? controls[0] : new StackPanel { Orientation = Orientation.Horizontal, Children = { controls[0], controls[1] } }) : Row(name, controls);
        var screen = _s.ManualScreen ??= new(1920, 1080, false);   // StartManual sets it; an older file may not have
        bool custom = _customScreen || !Resolutions.Contains((screen.Width, screen.Height));
        var c = Combo();
        if (compact) c.MinWidth = 120;
        c.ItemsSource = Resolutions.Select(r => $"{r.W}×{r.H}").Append(Custom).ToList();
        c.SelectedItem = custom ? Custom : $"{screen.Width}×{screen.Height}";
        c.SelectionChanged += (_, _) =>
        {
            _customScreen = c.SelectedIndex == Resolutions.Length;
            if (!_customScreen && c.SelectedIndex >= 0)
                _s.ManualScreen = _s.ManualScreen! with { Width = Resolutions[c.SelectedIndex].W, Height = Resolutions[c.SelectedIndex].H };
            Changed();
            again();
        };
        p.Children.Add(Line("해상도", c));
        if (custom)
        {
            // Checked when the box loses focus: a half-typed number isn't a size.
            TextBox NumberBox(Func<ManualScreen, int> get, Func<ManualScreen, int, ManualScreen> with)
            {
                string Shown() => get(_s.ManualScreen!).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var box = new TextBox { Text = Shown(), Width = 70, Margin = new(0, 0, Tokens.Space.Gap, 0) };
                box.LostKeyboardFocus += (_, _) =>
                {
                    if (int.TryParse(box.Text, out int v) && v is >= 640 and <= 16384) { _s.ManualScreen = with(_s.ManualScreen!, v); Changed(); }
                    else box.Text = Shown();
                };
                return box;
            }
            p.Children.Add(Line("화면 크기 (가로 × 세로)", NumberBox(s => s.Width, (s, v) => s with { Width = v }), NumberBox(s => s.Height, (s, v) => s with { Height = v })));
        }
        p.Children.Add(Check("21:9", screen.Wide, v => _s.ManualScreen = _s.ManualScreen! with { Wide = v }, () => { Changed(); again(); }, "게임 설정의 21:9 화면 비율입니다. 화면이 21:9보다 좁으면 위아래에 검은 띠가 생기고, 더 넓으면 화면을 그대로 씁니다."));
    }

    /// <summary>게임의 버프 크기, shown in 버프/디버프, 스킬창 맞추기 and the 표시 map; any one changes the same setting.</summary>
    FrameworkElement BuffSizeCombo() => SizeControl(GameSize.Buff, () => _s.Buffs.Size, v => _s.Buffs.Size = v, () => { FillBuffs(); FillFit(); });

    /// <summary>게임의 타겟 UI 버프 크기 (보스; named 보스 디버프 크기 before), in 버프/디버프 → 디버프: the boss row's own size, apart from 버프 크기.
    /// Its other copies (디버프, 암흑수류탄, the canvas box) refill to show the new size.</summary>
    FrameworkElement BossSizeCombo() => SizeControl(GameSize.Boss, () => _s.Debuffs.BossIconSize, v => _s.Debuffs.BossIconSize = v, () => { FillBuffs(); FillAmsu(); FillFit(); });

    /// <summary>The HUD size 직접 입력 keeps (<see cref="Settings.ManualHud"/>, set).</summary>
    FrameworkElement HudControl() => SizeControl(GameSize.Hud, () => (int)Math.Round(_s.ManualHud ?? 100), v => _s.ManualHud = v, FillFit);

    /// <summary>A combo box over <paramref name="items"/> showing <paramref name="value"/>; a pick calls <paramref name="set"/>, then saves.</summary>
    ComboBox EnumCombo<T>((T Value, string Text)[] items, T value, Action<T> set) where T : struct, Enum
    {
        var c = Combo();
        c.ItemsSource = items.Select(i => new ComboBoxItem { Content = i.Text, Tag = i.Value }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => value.Equals(i.Tag)) ?? c.Items[0];
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: T v }) { set(v); Changed(); } };
        return c;
    }
}
