using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The window's frame: four areas, each with sections picked by buttons in a header that never scrolls, one
/// section's form shown at a time, and in 내 오버레이 the overlay preview (pinned on the right when the window is wide,
/// above the form when not). Every setting has one section; the Fill methods fill the sections' panels.</summary>
public partial class SettingsWindow
{
    const string AreaCharacter = "캐릭터·인식", AreaOverlay = "내 오버레이", AreaParty = "파티", AreaKeys = "키·단축키";
    const string SecSkills = "스킬 칸", SecBuffs = "버프/디버프", SecMembers = "파티원 모양";
    const double WideNav = 760, WidePreview = 1040, PreviewWidth = 330;
    const string CardBrushKey = Themes.CardBrushKey, StrokeBrushKey = Themes.StrokeBrushKey;

    sealed class Area(string name, string lead, params (string Name, StackPanel Panel)[] sections)
    {
        public string Name { get; } = name;
        public string Lead { get; } = lead;
        public (string Name, StackPanel Panel)[] Sections { get; } = sections;
        public string Shown { get; set; } = sections[0].Name;
    }

    // One panel per section, refilled by the Fill methods.
    // Each a CardStack: its rows in Windows 11 setting cards, headings between them.
    readonly CardStack _secCharacter = new(), _secFit = new();
    readonly CardStack _secDisplay = new(), _secSkills = new(), _secGauge = new(), _secBuffs = new(), _secOther = new(), _secPlace = new(), _secCopy = new();
    readonly CardStack _secConnect = new(), _secMembers = new(), _secOrder = new(), _secSend = new();
    readonly CardStack _secKeys = new(), _secHotkeys = new();
    readonly StackPanel _context = new();   // 편집 중: the class the class sections edit
    Area[] _areas = [];
    Area _area = null!;
    bool _wide = true, _previewPinned;

    // 업데이트 (bottom left, under 모두 기본값으로 되돌리기): 업데이트 확인 asks GitHub; once a newer release is known it
    // becomes "vX 받기", and pressing that is the user's go-ahead to download and install.
    static readonly string BaseTitle = $"GaugeOverlay v{Updater.Current.ToString(3)} 설정";
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
        _update.Content = busy ? "업데이트 받는 중…" : _release is { } r ? $"{r.Tag} 받기" : "업데이트 확인";
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
            new(AreaCharacter, "캐릭터를 고르고 게임 화면의 스킬창을 맞춥니다.",
                ("캐릭터", _secCharacter), ("스킬창", _secFit)),
            new(AreaOverlay, "보고 싶은 요소를 고르고 미리보기로 확인하세요. 미리보기의 칸이나 게이지를 누르면 그 설정으로 갑니다.",
                ("전체", _secDisplay), (SecSkills, _secSkills), ("아덴 게이지", _secGauge), (SecBuffs, _secBuffs), ("기타", _secOther), ("배치", _secPlace), ("복사", _secCopy)),
            new(AreaParty, "연결, 파티원 칸의 모양과 순서, 보내고 받을 것을 정합니다.",
                ("연결", _secConnect), (SecMembers, _secMembers), ("순서", _secOrder), ("보내기·받기", _secSend)),
            new(AreaKeys, "게임에서 쓰는 스킬 키와 이 앱의 단축키입니다.",
                ("게임 키", _secKeys), ("앱 단축키", _secHotkeys)),
        ];
        _area = _areas[1];

        foreach (var area in _areas)
        {
            var b = Toggle(area.Name, "area", area == _area);
            // Full width with an icon: the four areas read as one list, not as buttons of four widths.
            (b.Tag, b.Content, b.HorizontalContentAlignment, b.HorizontalAlignment) = (area.Name, AreaLabel(area.Name), HorizontalAlignment.Left, HorizontalAlignment.Stretch);
            System.Windows.Automation.AutomationProperties.SetName(b, area.Name);   // read as the area, not the icon glyph
            b.Checked += (_, _) => { if (_area != area) Go(area.Name); };
            Nav.Children.Add(b);
        }
        var theme = EnumCombo([(UiTheme.DeepTeal, "딥 틸"), (UiTheme.System, "Windows 설정 따름"), (UiTheme.Light, "밝게"), (UiTheme.Dark, "어둡게")],
            _s.Theme, v => { _s.Theme = v; ApplyTheme(); });
        theme.MinWidth = 150;
        NavFoot.Children.Add(Wrap(Label("테마"), theme));
        NavFoot.Children.Add(_navNote);
        NavFoot.Children.Add(ResetButton("모두 기본값으로 되돌리기",
            "키, 단축키, 오버레이 표시, 버프 막대와 디버프 칸 크기, 아덴 게이지, 클래스별 칸, 파티 설정, 스킬창 맞춤을 되돌립니다. "
            + "캐릭터, 오버레이 프로필(위치·크기), 창 위치, 배운 게이지 값과 테마는 남습니다.",
            () => SettingsResets.All(_s)));
        _update.Click += (_, _) =>
        {
            if (_release is { } r) { UpdateRequested?.Invoke(r); return; }
            _checkMessage.Stop();
            (_update.IsEnabled, _update.Content) = (false, "확인 중…");
            CheckRequested?.Invoke();
        };
        NavFoot.Children.Add(_update);
        NavFoot.Children.Add(Check("시작할 때 업데이트 확인", _s.Updates.Check, v => _s.Updates.Check = v, Changed));
        NavHost.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        NavHost.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        Root.SetResourceReference(Panel.BackgroundProperty, BackgroundBrushKey);
        SizeChanged += (_, _) => Relayout();
        // Opened on the 캐릭터 section: it is seen now (ShowBody only marks it while the window is up).
        IsVisibleChanged += (_, _) => { if (IsVisible && _area.Name == AreaCharacter && _area.Shown == "캐릭터") MarkSupportSeen(); };
        ApplyTheme();
    }

    readonly TextBlock _navNote = Note("바꾼 설정은 바로 저장되고 오버레이에 반영됩니다. 파티원에게는 파티 → 보내기·받기에서 따로 보냅니다.");

    const string BackgroundBrushKey = Themes.BackgroundBrushKey, NavBrushKey = Themes.NavBrushKey;

    void ApplyTheme() => Themes.Apply(this, _s.Theme);

    /// <summary>Segoe icons (Fluent on Windows 11, MDL2 on 10) for the areas: 사람, 보기, 여러 사람, 키보드.</summary>
    static readonly Dictionary<string, string> AreaIcons = new()
    {
        [AreaCharacter] = "", [AreaOverlay] = "", [AreaParty] = "", [AreaKeys] = "",
    };

    static StackPanel AreaLabel(string name)
    {
        var icon = new TextBlock
        {
            Text = AreaIcons[name], FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14,
            Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center } } };
    }

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

    void ShowArea()
    {
        ShowHeader();
        ShowBody();
        Body.ScrollToTop();
    }

    void ShowHeader()
    {
        Detach(_context);
        Header.Children.Clear();
        Header.Children.Add(new TextBlock { Text = _area.Name, FontSize = 22, FontWeight = FontWeights.SemiBold });
        Header.Children.Add(Note(_area.Lead));
        if (ContextShown) Header.Children.Add(_context);
        var sections = new WrapPanel { Margin = new(0, 4, 0, 0) };
        foreach (var (name, _) in _area.Sections)
        {
            var b = Toggle(name, "section", name == _area.Shown);
            b.Checked += (_, _) => { if (_area.Shown == name) return; _area.Shown = name; ShowArea(); };
            sections.Children.Add(b);
        }
        Header.Children.Add(sections);
        Header.Children.Add(Divider(4, 0));
    }

    /// <summary>The class line shows where class settings are edited: 내 오버레이 and 파티 → 파티원 모양.</summary>
    bool ContextShown => _area.Name == AreaOverlay || _area.Name == AreaParty && _area.Shown == SecMembers;

    void ShowBody()
    {
        var panel = _area.Sections.First(s => s.Name == _area.Shown).Panel;
        if (panel == _secCharacter && IsVisible) MarkSupportSeen();
        bool preview = _area.Name == AreaOverlay && _quickBlock is not null;
        _previewPinned = preview && _wide && ActualWidth >= WidePreview;
        PreviewColumn.Width = new(_previewPinned ? PreviewWidth : 0);
        // The preview stays where it is when that's still its place: this runs from its own click.
        object? place = !preview ? null : _previewPinned ? PreviewHost : Form;
        if (_quickBlock?.Parent != place) Detach(_quickBlock);
        for (int i = Form.Children.Count - 1; i >= 0; i--)
            if (Form.Children[i] != _quickBlock) Form.Children.RemoveAt(i);
        if (place == PreviewHost) PreviewHost.Content = _quickBlock;
        else if (place == Form && _quickBlock!.Parent is null) Form.Children.Add(_quickBlock);
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

    static Border Card(UIElement content)
    {
        var card = new Border { Child = content, Padding = new(20, 16, 20, 16), CornerRadius = new(8), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, CardBrushKey);
        card.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        return card;
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

    /// <summary>Narrow windows: the areas become a row across the top and the preview goes above the form.</summary>
    void Relayout()
    {
        bool wide = ActualWidth >= WideNav;
        bool pin = _area.Name == AreaOverlay && _quickBlock is not null && wide && ActualWidth >= WidePreview;
        if (wide == _wide && pin == _previewPinned) return;
        _wide = wide;
        NavColumn.Width = new(wide ? 200 : 0);
        Grid.SetRow(NavHost, wide ? 1 : 0);
        Grid.SetColumnSpan(NavHost, wide ? 1 : 2);
        NavHost.BorderThickness = wide ? new(0, 0, 1, 0) : new(0, 0, 0, 1);
        NavHost.Padding = wide ? new(10, 16, 10, 12) : new(12, 8, 12, 2);
        Nav.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
        _navNote.Visibility = Shown(wide);   // the row across the top keeps 테마 and 모두 되돌리기
        NavFoot.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
        ShowBody();
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
