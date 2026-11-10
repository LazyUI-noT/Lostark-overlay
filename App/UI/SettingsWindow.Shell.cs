using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The window's frame: the top bar (<see cref="BuildTopBar"/>), the rail of areas on the left, and the page of
/// the area picked: 내 오버레이's canvas with the inspector (<see cref="BuildCanvasPage"/>), or another area's section
/// buttons over one section's form. The Fill methods fill the sections' panels.</summary>
public partial class SettingsWindow
{
    const string AreaOverlay = "내 오버레이", AreaCharacter = "직업 인식", AreaParty = "파티 공유", AreaFit = "화면·인식", AreaKeys = "키·단축키";
    const string AreaApp = "테마·업데이트", AreaScreens = "표시할 화면", AreaSkills = "스킬창", AreaJudgment = "단심", AreaBuffs = "버프 줄", AreaAmsu = "암수", AreaApi = "로아 API", AreaWarnings = "경고";
    /// <summary>A form's width at most: rows don't stretch across a wide window (user, 2026-11-05: long blocks sideways).</summary>
    const double FormMaxWidth = 620;
    const string SecFit = "스킬창 맞추기";   // not "스킬창": that is the rail's area for the boxes' look
    const string FluentDarkNav = "#202020", FluentLightNav = "#F3F3F3";   // the themes that leave the rail to Fluent
    readonly List<(TextBlock Heading, string Color)> _railHeadings = [];
    readonly Dictionary<string, string> _areaHelp = [];
    const string SecMembers = "파티원 모양";
    const string CardBrushKey = Themes.CardBrushKey, StrokeBrushKey = Themes.StrokeBrushKey;

    sealed class Area(string name, string lead, params (string Name, StackPanel Panel)[] sections)
    {
        public string Name { get; } = name;
        public string Lead { get; } = lead;
        public (string Name, StackPanel Panel)[] Sections { get; } = sections;
        public string Shown { get; set; } = sections.Length > 0 ? sections[0].Name : "";
    }

    // One panel per section, refilled by the Fill methods.
    // Each a CardStack: its rows in Windows 11 setting cards, headings between them.
    readonly CardStack _secCharacter = new(), _secFit = new();
    readonly CardStack _secSkillLook = new(), _secDisplay = new(), _secBuffs = new(), _secOther = new(), _secAmsu = new(), _secPlace = new(), _secCopy = new();
    readonly CardStack _secConnect = new(), _secMembers = new(), _secOrder = new(), _secSend = new();
    readonly CardStack _secKeys = new(), _secHotkeys = new(), _secApp = new(), _secApi = new(), _secWarnings = new();
    readonly StackPanel _context = new() { Orientation = Orientation.Horizontal };   // 편집 중 (top bar): the class the class sections edit
    Area[] _areas = [];
    Area _area = null!;

    // 업데이트 (bottom left, under 모두 기본값으로 되돌리기): 업데이트 확인 asks GitHub; once a newer release is known it
    // becomes "vX 받기", and pressing that is the user's go-ahead to download and install.
    static readonly string BaseTitle = $"GaugeOverlay {Updater.CurrentTag} 설정";
    static readonly TimeSpan CheckMessageFor = TimeSpan.FromSeconds(3);
    readonly Button _update = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 6, 0, 0), Content = "업데이트 확인" };
    Release? _release;

    /// <summary>The update button was pressed with a newer release known: download and install it.</summary>
    public event Action<Release>? UpdateRequested;
    /// <summary>업데이트 확인 was pressed; answer with <see cref="SetCheckResult"/>.</summary>
    public event Action? CheckRequested;

    /// <summary>A newer release ("vX 받기", "(업데이트 있음)" in the title), or null for none.</summary>
    public void SetUpdate(Release? release)
    {
        _release = release;
        _checkMessage.Stop();
        Title = release is null ? BaseTitle : $"{BaseTitle} (업데이트 있음)";
        SetUpdateBusy(false);
    }

    /// <summary>While downloading and installing: the button is off and says so.</summary>
    public void SetUpdateBusy(bool busy)
    {
        _update.IsEnabled = !busy;
        _update.Content = busy ? "업데이트 받는 중…" : _release is { } r ? UpdateLabel(r) : "업데이트 확인";
        _update.ToolTip = !busy && _release is { } b && UpdateCheck.BreaksParty(b.Version, Updater.Current)
            ? "큰 업데이트입니다. 업데이트하면 이전 버전을 쓰는 파티원과 공유 방을 함께 쓸 수 없으니, 파티원도 모두 업데이트해야 합니다." : null;
    }

    /// <summary>"vX 받기", with a yellow warning triangle in front when the update changes A or B of A.B.x (user, 2026-10-13).</summary>
    static object UpdateLabel(Release r)
    {
        var text = UpdateCheck.IsDowngrade(r, Updater.CurrentTag) ? $"{r.Tag}(안정)으로 되돌리기" : $"{r.Tag} 받기";
        if (!UpdateCheck.BreaksParty(r.Version, Updater.Current)) return text;
        const double Size = 14;
        var sign = new Grid { Width = Size, Height = Size, Margin = new(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        sign.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 7,0.5 L 13.5,13 L 0.5,13 Z"), Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x00)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x80, 0x60, 0x00)), StrokeThickness = 0.8, StrokeLineJoin = PenLineJoin.Round,
        });
        sign.Children.Add(new TextBlock
        {
            Text = "!", FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 0.5),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(sign);
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    /// <summary>The answer to 업데이트 확인: a newer release turns the button into "vX 받기"; otherwise it says 최신 버전입니다
    /// (or 확인하지 못했습니다 when GitHub couldn't be read) for <see cref="CheckMessageFor"/>.</summary>
    public void SetCheckResult(bool checkedOk, Release? newer)
    {
        if (newer is not null) { SetUpdate(newer); return; }
        if (_release is not null) { SetUpdateBusy(false); return; }   // the start-up check found one meanwhile
        _update.Content = checkedOk ? "최신 버전입니다" : "확인하지 못했습니다";
        _checkMessage.Start();
    }

    // One timer for the check message, stopped by a new check or a found release so an old one can't reset the button.
    readonly System.Windows.Threading.DispatcherTimer _checkMessage = new() { Interval = CheckMessageFor };

    void BuildShell()
    {
        Title = BaseTitle;
        _checkMessage.Tick += (_, _) => { _checkMessage.Stop(); SetUpdateBusy(false); };
        _areas =
        [
            new(AreaFavorites, "★를 단 설정을 한곳에서 바꿉니다. 설정 행을 오른쪽 클릭하거나, 검색 결과의 ☆로 답니다.", (AreaFavorites, _secFavorites)),
            new(AreaShow, "오버레이 창을 한눈에 보고 켜고 끕니다. 언제 보일지도 여기서 정합니다.", (AreaShow, _secShow)),
            new(AreaOverlay, ""),
            new(AreaScreens, "오버레이를 띄울 모니터와 언제 보일지를 정합니다.", (AreaScreens, _secScreens)),
            new(AreaCharacter, "지금 플레이하는 캐릭터와 코어 빌드입니다.", (AreaCharacter, _secCharacter)),
            new(AreaSkills, "스킬 칸을 켜고 끄고, 스킬마다 보이기와 색을 정합니다. 칸 모양은 모든 칸의 쿨 표시입니다.", ("내 스킬", _secSkillList), ("칸 모양", _secSkillLook)),
            new(AreaJudgment, "단죄·심판 룬의 심판 시간입니다.", (AreaJudgment, _secOther)),
            new(AreaBuffs, "내 버프 줄에서 찾아 남은 시간을 막대로 보여 줍니다.", (AreaBuffs, _secBuffs)),
            new(AreaAmsu, "보스에 걸린 암흑수류탄의 남은 시간입니다.", (AreaAmsu, _secAmsu)),
            new(AreaMeter, "보스가 받은 피해 중 각자의 몫(딜지분, 찬조)을 보여 줍니다.", (AreaMeter, _secMeter)),
            new(AreaParty, "연결, 파티원 칸의 모양과 순서, 보내고 받을 것을 정합니다.",
                ("연결", _secConnect), (SecMembers, _secMembers), ("순서", _secOrder), ("보내기·받기", _secSend)),
            new(AreaRaid, "보스를 인식하고, 줄 수 창과 디버프 창을 관문마다 켭니다.", ("레이드", _secRaid)),
            new(AreaRecords, "캐릭터마다 저장된 전투를 보고, 열어서 시간 순으로 봅니다. 둘을 골라 비교합니다.", (AreaRecords, _secRecords)),
            new(AreaHomework, "캐릭터마다 이번 주에 클리어한 레이드를 봅니다.", (AreaHomework, _secHomework)),
            new(AreaFit, "게임 화면의 스킬창을 맞추고, 게임 설정 값을 앱에 알려 줍니다. 감지에서 칸을 눌러 칸마다 읽은 값을 봅니다.", (SecFit, _secFit), (SecDetect, _secDetect)),
            new(AreaKeys, "게임에서 쓰는 스킬 키와 이 앱의 단축키입니다.", ("게임 키", _secKeys), ("앱 단축키", _secHotkeys)),
            new(AreaApi, "로스트아크 오픈 API 키를 넣으면 캐릭터 정보를 서버에서 직접 받아 옵니다.", (AreaApi, _secApi)),
            new(AreaWarnings, "작업 전에 묻는 경고 메시지를 켜고 끕니다.", (AreaWarnings, _secWarnings)),
            new(AreaApp, "화면 테마, 업데이트, 전체 초기화입니다.", (AreaApp, _secApp)),
        ];
        _area = _areas.First(a => a.Name == AreaShow);

        foreach (var group in FeatureMap.Rail)
        {
            var color = group.Color.Length > 0 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(group.Color)) : null;
            // A group of one area by its own name says it once, in the area: its bar keeps the colour.
            bool lone = group.Areas.Count == 1 && group.Areas[0] == group.Name;
            if (group.Name.Length > 0 && !lone)
            {
                var heading = new TextBlock { Text = group.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new(4, 6, 0, 1) };
                if (group.Color.Length > 0) _railHeadings.Add((heading, group.Color));
                Nav.Children.Add(heading);
            }
            foreach (var name in group.Areas)
            {
                var area = _areas.First(a => a.Name == name);
                var b = Toggle(name, "area", area == _area);
                b.Margin = new(0, lone ? 8 : 0, 0, 2);
                b.Padding = new(8, 3, 8, 3);
                b.Style = RailRow(b.Style);   // the rail is a list: only the picked row is filled
                // Full width with the group's bar: the areas read as one list, not as buttons of different widths.
                var label = AreaLabel(b, name, color);
                label.Children.Add(_railOrbs[name] = RailOrb());   // lit when something under this tab is new (RefreshRailOrbs)
                (b.Tag, b.Content, b.HorizontalContentAlignment, b.HorizontalAlignment) = (name, label, HorizontalAlignment.Left, HorizontalAlignment.Stretch);
                System.Windows.Automation.AutomationProperties.SetName(b, name);   // read as the area, not the icon glyph
                b.Checked += (_, _) => { if (_area != area) Go(name); };
                Nav.Children.Add(b);
            }
        }
        var theme = EnumCombo([(UiTheme.LostArk, "로스트아크"), (UiTheme.DeepTeal, "딥 틸"), (UiTheme.Graphite, "그래파이트"), (UiTheme.ArkesiaNight, "아크라시아 밤"),
            (UiTheme.Light, "밝게"), (UiTheme.Dark, "어둡게"), (UiTheme.System, "Windows 설정 따름")],
            _s.Theme, v => { _s.Theme = v; ApplyTheme(); });
        theme.MinWidth = 150;
        _secApp.Children.Add(Wrap(Label("테마"), theme));
        _secApp.Children.Add(_navNote);
        _secApp.Children.Add(ResetButton("전체 초기화",
            "키, 단축키, 오버레이 표시, 버프 막대와 디버프 칸 크기, 아덴 게이지, 클래스별 칸, 파티 설정, 스킬창 맞춤을 되돌립니다. "
            + "캐릭터, 오버레이 배치(위치·크기), 창 위치, 배운 게이지 값과 테마는 남습니다.",
            () => SettingsResets.All(_s)));
        _update.Click += (_, _) =>
        {
            if (_release is { } r) { UpdateRequested?.Invoke(r); return; }
            _checkMessage.Stop();
            (_update.IsEnabled, _update.Content) = (false, "확인 중…");
            CheckRequested?.Invoke();
        };
        _secApp.Children.Add(_update);
        // 업데이트 채널 (user, 2026-11-03): 베타 tries new features early; 안정 again takes a beta back to the stable release.
        var channel = EnumCombo([(UpdateChannel.Stable, "안정"), (UpdateChannel.Beta, "베타")], _s.Updates.Channel, v =>
        {
            _s.Updates.Channel = v;
            SetUpdate(null);
            _checkMessage.Stop();
            (_update.IsEnabled, _update.Content) = (false, "확인 중…");
            CheckRequested?.Invoke();
        });
        channel.ToolTip = "베타: 새 기능을 먼저 씁니다. 문제가 있으면 안정으로 바꿔 안정 버전으로 돌아갈 수 있습니다(설정 파일은 백업됩니다).";
        _secApp.Children.Add(Wrap(Label("업데이트 채널"), channel));
        _secApp.Children.Add(Check("시작할 때 자동 업데이트", _s.Updates.Check, v => _s.Updates.Check = v, Changed));
        NavHost.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        NavHost.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        Root.SetResourceReference(Panel.BackgroundProperty, BackgroundBrushKey);
        BuildTopBar();
        BuildSearch();
        // Opened on the 캐릭터 section: it is seen now (ShowBody only marks it while the window is up).
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) return;
            FindClassNews();   // once per opening; 직업 인식 shown below records what it lists
            if (_area.Name == AreaCharacter) MarkSupportSeen();
            RefreshRailOrbs();
        };
        ApplyTheme();
    }

    readonly TextBlock _navNote = Note("바꾼 설정은 바로 저장되고 오버레이에도 바로 보입니다. 파티원에게 보내려면 파티 공유 → 보내기·받기에서 따로 보내세요.");

    const string BackgroundBrushKey = Themes.BackgroundBrushKey, NavBrushKey = Themes.NavBrushKey;

    void ApplyTheme()
    {
        Themes.Apply(this, _s.Theme);
        PaintRail();
    }

    /// <summary>The group names in their map colours, moved only as far as this theme's rail needs to read them.</summary>
    void PaintRail()
    {
        if (SystemParameters.HighContrast)   // Fluent draws the system colours: the headings take them too
        {
            foreach (var (heading, _) in _railHeadings) heading.ClearValue(TextBlock.ForegroundProperty);
            return;
        }
        var palette = UiPalette.Of(_s.Theme);
        bool dark = _s.Theme == UiTheme.System ? Themes.AppsDark() : palette.Dark;
        string nav = palette.Colors.GetValueOrDefault(UiPalette.Nav) ?? (dark ? FluentDarkNav : FluentLightNav);
        foreach (var (heading, color) in _railHeadings)
            heading.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(UiContrast.Legible(color, nav)));
    }

    /// <summary>Segoe icons (Fluent on Windows 11, MDL2 on 10) for the areas: 사람, 보기, 여러 사람, 키보드.</summary>
    static readonly Dictionary<string, string> AreaIcons = new()
    {
        [AreaApp] = "\uE713", [AreaScreens] = "\uE7F4", [AreaSkills] = "\uE8FD", [AreaJudgment] = "\uE916", [AreaBuffs] = "\uE945", [AreaAmsu] = "\uE7C1", [AreaFavorites] = "\uE734", [AreaShow] = "\uE890", [AreaOverlay] = "\uE81E", [AreaCharacter] = "\uE77B", [AreaParty] = "\uE716", [AreaRaid] = "\uE7FC", [AreaMeter] = "\uE9D2", [AreaRecords] = "\uE81C", [AreaHomework] = "\uE9D5",[AreaFit] = "\uE7C5", [AreaKeys] = "\uE765", [AreaApi] = "\uE8D7", [AreaWarnings] = "\uE7BA",
    };

    /// <summary>An area's rail label: the bar in its group's colour (none when neutral), the icon, the name.</summary>
    static StackPanel AreaLabel(RadioButton row, string name, Brush? color)
    {
        var bar = new Border { Width = 3, Height = 16, CornerRadius = new(1.5), Margin = new(0, 0, 8, 0), Background = color ?? Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        var icon = new TextBlock
        {
            Text = AreaIcons[name], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14,
            Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var text = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        // A tab with a second line (settings-reorg order 6): its name over it, smaller.
        var subtitle = FeatureMap.RailSubtitles.TryGetValue(name, out var line) ? new TextBlock { Text = line, FontSize = 10.5 } : null;
        UIElement caption = subtitle is null ? text : new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { text, subtitle } };
        // Own TextBlocks skip the toggle's text colour, which left white on the sand fill (2.3:1). The theme's keys, not a binding
        // to the toggle's presenter: on a real start that drew every row black, and --demo hid it.
        void Paint()
        {
            var on = row.IsChecked == true;
            foreach (var part in new[] { icon, text }) part.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
            subtitle?.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextOnAccentFillColorSecondaryBrush" : "TextFillColorSecondaryBrush");
        }
        Paint();
        row.Checked += (_, _) => Paint();
        row.Unchecked += (_, _) => Paint();
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { bar, icon, caption } };
    }

    /// <summary>A toggle with no fill or edge at rest. A style, not local values: those would beat the picked state too.</summary>
    static Style RailRow(Style? toggle) => new(typeof(RadioButton), toggle)
    {
        Setters = { new Setter(BackgroundProperty, Brushes.Transparent), new Setter(BorderBrushProperty, Brushes.Transparent) },
    };

    /// <summary>A section button (or area button): one of a group, drawn as a toggle.</summary>
    RadioButton Toggle(string text, string group, bool on) => new()
    {
        Content = text, GroupName = group, IsChecked = on, Margin = new(0, 0, 6, 6), Padding = new(12, 6, 12, 6),
        Style = TryFindResource(typeof(ToggleButton)) as Style,
    };

    /// <summary>Shows <paramref name="area"/> at <paramref name="section"/>, else at the section it was last on.</summary>
    void Go(string area, string? section = null)
    {
        _area = _areas.FirstOrDefault(a => a.Name == area) ?? _area;
        if (section is not null && _area.Sections.Any(s => s.Name == section)) _area.Shown = section;
        foreach (var b in Nav.Children.OfType<RadioButton>()) b.IsChecked = (string)b.Tag == _area.Name;
        ShowArea();
    }

    string? _shownArea;

    void ShowArea()
    {
        // 즐겨찾기 edits through its own copies of the rows: the sections show the new values only once rebuilt
        if (_shownArea == AreaFavorites && _area.Name != AreaFavorites) BuildAll();
        if (_area.Name == AreaRecords && _shownArea != AreaRecords) EnterRecords();   // the list, the current character's chip
        if (_area.Name == AreaHomework) FillHomework();
        _shownArea = _area.Name;
        bool canvas = _area.Name == AreaOverlay && _canvas is not null;
        CanvasPage.Visibility = Shown(canvas);
        SectionPage.Visibility = Shown(!canvas);
        if (canvas) { RefreshInspector(); RefreshRailOrbs(); return; }
        StopPlay();
        if (_area.Name == AreaFavorites) FillFavorites();
        ShowHeader();
        ShowBody();
        ShowPreview();
        Body.ScrollToTop();
        DiagnosticsMaybeChanged();
        RefreshRailOrbs();
    }

    void ShowHeader()
    {
        Header.Children.Clear();
        Header.Children.Add(Helped(new() { FontSize = 22, FontWeight = FontWeights.SemiBold }, _area.Name, _areaHelp.GetValueOrDefault(_area.Name)));
        Header.Children.Add(Note(_area.Lead));
        if (_area.Sections.Length > 1)
        {
            var sections = new WrapPanel { Margin = new(0, 4, 0, 0) };
            foreach (var (name, _) in _area.Sections)
            {
                var b = Toggle(name, "section", name == _area.Shown);
                b.Checked += (_, _) => { if (_area.Shown == name) return; _area.Shown = name; ShowArea(); };
                sections.Children.Add(b);
            }
            Header.Children.Add(sections);
        }
        Header.Children.Add(Divider(4, 0));
    }

    void ShowBody()
    {
        var panel = _area.Sections.First(s => s.Name == _area.Shown).Panel;
        if (panel == _secCharacter && IsVisible) MarkSupportSeen();
        Form.Children.Clear();
        // Wide by nature: the scene, the timeline, the read-out table, the 숙제 grid.
        bool wide = _area.Name is AreaShow or AreaRecords or AreaHomework || _area.Shown is SecDetect;
        Form.MaxWidth = wide ? double.PositiveInfinity : FormMaxWidth;
        Form.HorizontalAlignment = wide ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        Detach(panel);
        Form.Children.Add(panel);   // its own cards (CardStack)
    }

    /// <summary>A thin line across, in the cards' stroke colour: under the section buttons, and between the parts of a section.</summary>
    static Border Divider(double above = 28, double below = 16)
    {
        var line = new Border { Height = 1, Margin = new(0, above, 0, below) };
        line.SetResourceReference(Border.BackgroundProperty, StrokeBrushKey);
        return line;
    }

    /// <summary>Takes a persistent element out of wherever it was shown last.</summary>
    static void Detach(UIElement? e)
    {
        switch ((e as FrameworkElement)?.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
    }

    /// <summary>--demo --tab: opens the area, or the section, with this name.</summary>
    internal void ShowTab(string name)
    {
        Show();
        Activate();
        var area = _areas.FirstOrDefault(a => a.Name == name) ?? _areas.FirstOrDefault(a => a.Sections.Any(s => s.Name == name));
        if (area is not null) Go(area.Name, area.Name == name ? null : name);
    }
}
