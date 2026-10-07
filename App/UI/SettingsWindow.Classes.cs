using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>클래스별: per class, your boxes (나) and party members' (파티원): what is shown, where, in what colour and
/// for members what size, plus 복사. The class edited is picked in the header's 편집 중 line; the rules are
/// <see cref="ClassProfiles"/>'. A class is read without being created and made (from the template) on its first edit.</summary>
public partial class SettingsWindow
{
    const string AllClasses = "*";   // 복사 → 대상: every class and the template
    string? _classShown;             // the class picked in 편집 중 (ClassProfiles.Template = 기본); null = the character's
    readonly StackPanel _skillsClass = new();   // 스킬 칸 under the box options: the class's 나 part
    CopyParts _copyParts = CopyParts.Color | CopyParts.Size | CopyParts.Layout;
    bool _copyFromMembers, _copyToMembers = true;
    string _copyTo = AllClasses;
    string? _copyResult;

    static string ClassName(string cls) => cls == ClassProfiles.Template ? "기본" : cls;

    string ActiveOrTemplate => ClassProfiles.ActiveKey(_s) ?? ClassProfiles.Template;
    string ShownClass => _classShown ?? ActiveOrTemplate;

    /// <summary>What depends on the class picked: the 편집 중 line and the sections that edit that class.</summary>
    void FillClassSections()
    {
        FillContext();
        string cls = ShownClass;
        var read = ClassProfiles.Of(_s, cls);
        ClassSettings Edit() => ClassProfiles.Edit(_s, cls);
        MinePart(Refill(_skillsClass, $"나 ({ClassName(cls)})"), read, Edit);
        _skillsClass.Children.Add(ResetButton("모든 클래스의 칸 설정 지우기", "모든 클래스의 표시·배치·색·크기·효과 설정을 지웁니다(나, 파티원). 개별 설정 여부는 남습니다.",
            () => SettingsResets.Classes(_s)));
        FillPlace(read, Edit);
        CopyPart(Refill(_secCopy, "복사", "이 클래스의 나 또는 파티원 설정에서 고른 부분을 다른 설정으로 복사합니다. 크기는 파티원 설정에만 있습니다."), cls);
        FillMembers();
    }

    /// <summary>편집 중: the class picker with 개별 설정. The box options and the preview are the character's class,
    /// which the line says when another class is picked.</summary>
    void FillContext()
    {
        string cls = ShownClass, active = ActiveOrTemplate;
        _context.Children.Clear();
        // each class, then its 직업각인 ("워로드 · 전투태세"), which follow it until 개별
        var names = _chars.Classes.SelectMany(c => c.Subclasses.Keys.Select(sub => $"{c.Name} · {sub}").Prepend(c.Name));
        var pick = PickCombo(names.Prepend(ClassProfiles.Template), cls, v => { _classShown = v; Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); }); });
        pick.MinWidth = 160;
        var line = Wrap(new TextBlock { Text = "편집 중", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, pick);
        if (cls == active && _chars.Active is { } c) line.Children.Add(new TextBlock { Text = $"· {c.Subclass}", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0) });
        if (cls != ClassProfiles.Template)
            line.Children.Add(Check("개별 설정", ClassProfiles.IsOwn(_s, cls),
                v => { ClassProfiles.SetOwn(_s, cls, v); Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); }); }, Changed));
        line.Margin = new(0, 8, 0, 4);
        _context.Children.Add(line);
        string? note = cls == ClassProfiles.Template
            ? "기본: 클래스 정보가 없을 때의 표시이고, 개별 설정을 켜지 않은 클래스가 따르는 설정입니다. 버프·초각스·각성기·일반스킬만 보이고, 아덴 게이지와 아덴스킬은 보이지 않습니다."
            : !ClassProfiles.IsOwn(_s, cls) ? $"{ClassName(ClassProfiles.Parent(cls)!)}을(를) 따르는 중입니다. 보이는 값은 그 값이고, 바꾸려면 개별 설정을 켜세요."
            : ClassProfiles.Parent(cls) != ClassProfiles.Template ? "이 직업각인의 내 칸과 내 창의 위치·크기는 따로 저장됩니다. 파티원 설정은 클래스의 것입니다." : null;
        if (cls != active) note = $"{note} 미리보기와 칸 옵션은 지금 캐릭터({ClassName(active)}) 기준입니다.".Trim();
        if (note is not null) _context.Children.Add(Note(note));
    }

    /// <summary>배치: the 레이아웃 편집기, 편집 모드 and the class's 나 layout.</summary>
    void FillPlace(ClassSettings read, Func<ClassSettings> edit)
    {
        var p = Refill(_secPlace, "배치", "위치, 크기, 칸 배치, 판 색, 오버레이 프로필은 레이아웃 편집기에서 게임 화면 위에 놓고 바꿉니다. 편집 모드는 게임 안에서 판을 끌어 옮기고, 오른쪽 아래 모서리로 전체 크기를 바꿉니다.");
        var editor = new Button { Content = "레이아웃 편집기 열기", Padding = new(12, 4, 12, 4),
            ToolTip = "게임 화면(스크린샷) 위에서 오버레이와 칸의 위치·크기, 판 색, 오버레이 프로필을 바꿉니다." };
        editor.Click += (_, _) => LayoutEditorRequested?.Invoke();
        var mode = new Button { Content = "편집 모드" };
        mode.Click += (_, _) => EditModeRequested?.Invoke();
        p.Children.Add(Wrap(editor, mode));
        PlacementRow(p, read.Mine, () => edit().Mine, "레이아웃 편집기에서 내 칸을 끌어 옮기면 이 클래스의 배치가 됩니다.");
    }

    void MinePart(Panel p, ClassSettings read, Func<ClassSettings> edit)
    {
        var mine = read.Mine;
        DisplayRows(p, mine, () => edit().Mine, enabled: true);
        p.Children.Add(Row("색", EnumCombo([(ColorSource.Default, "기본"), (ColorSource.Personal, "개인 설정")],
            mine.Color, v => { edit().Mine.Color = v; Dispatcher.BeginInvoke(FillClassSections); })));
        p.Children.Add(Note("칸 하나의 색은 위의 칸 옵션에서 바꿉니다 (바꾸면 개인 설정이 됩니다)."));
        var autoDim = Check("쿨타임 색 자동 (어둡게)", mine.AutoDim, v => { edit().Mine.AutoDim = v; Dispatcher.BeginInvoke(FillClassSections); }, Changed);
        autoDim.ToolTip = "끄면 색마다 쿨타임 동안의 색을 고릅니다. 처음에는 지금 자동으로 어둡게 한 색입니다.";
        autoDim.Visibility = Shown(mine.Color == ColorSource.Personal);
        p.Children.Add(autoDim);
        bool personal = mine.Color == ColorSource.Personal;
        // 아이콘으로 표시: a group drawn as icons has no colour rows here, only the note saying where its colour still shows.
        if (personal && mine.Icons.Count > 0) p.Children.Add(Note($"{string.Join(", ", mine.Icons.Order())} {IconColourNote}"));
        ColorRows(p, mine, () => edit().Mine, g => ClassProfiles.BuiltIn[g],
            enabled: personal, skillsOwnRows: !mine.SkillsOneColor, dims: !mine.AutoDim, icons: mine.Icons);
        SkillColorRows(p, mine, () => edit().Mine, enabled: personal && !mine.ShowsIcon(SlotGroup.일반스킬));
        GlowRows(p, mine, () => edit().Mine);
    }

    /// <summary>빛 색 is picked in 칸 옵션 under 쿨 돈 동안 계속 빛남 (BoxOptions.SetGlowColor); here only the way back to
    /// every glow white, once a colour was picked.</summary>
    void GlowRows(Panel p, MineProfile read, Func<MineProfile> edit)
    {
        if (!read.CustomGlow) return;
        var white = new Button { Content = "빛 색 모두 흰색으로", HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "칸 옵션에서 고른 빛 색을 끄고 모든 칸이 흰색으로 빛나게 합니다." };
        white.Click += (_, _) =>
        {
            var e = edit();
            (e.CustomGlow, e.PulseColor, e.SlotPulseColors) = (false, [], []);   // gone, not waiting to come back
            Changed();
            Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); });
        };
        p.Children.Add(Row(Described("빛 색", "칸 옵션의 '쿨 돈 동안 계속 빛남' 아래에서 고릅니다"), white));
    }

    /// <param name="mine">The 나 settings 내 설정 복사 copies (a 직업각인's own when one is picked); null = the class's.</param>
    void MembersPart(Panel p, ClassSettings read, Func<ClassSettings> edit, MineProfile? mine = null)
    {
        var m = read.Members;
        p.Children.Add(Note("이 클래스 파티원의 칸입니다. '내 설정 적용'을 켠 부분은 내 오버레이 → 스킬 칸의 '나' 설정을 쓰고, 아래 파티원 설정은 그대로 남습니다."));
        void Refill() => Dispatcher.BeginInvoke(FillClassSections);
        var copyMine = new Button { Content = "내 설정 복사", ToolTip = "'나'의 표시, 배치, 색, 크기를 이 클래스의 파티원 설정으로 복사합니다." };
        copyMine.Click += (_, _) => { var c = edit(); ClassProfiles.Copy(_s, mine ?? c.Mine, [c.Members], CopyParts.All); Changed(); Refill(); };
        p.Children.Add(Row("내 설정 적용",
            Check("색", m.UseMineColor, v => { edit().Members.UseMineColor = v; Refill(); }, Changed),
            Check("표시", m.UseMineDisplay, v => { edit().Members.UseMineDisplay = v; Refill(); }, Changed),
            Check("크기", m.UseMineSize, v => { edit().Members.UseMineSize = v; Refill(); }, Changed),
            copyMine));
        DisplayRows(p, m, () => edit().Members, enabled: !m.UseMineDisplay);
        PlacementRow(p, m, () => edit().Members, "레이아웃 편집기에서 파티원 판의 칸을 끌어 옮기면 이 클래스의 파티원 배치가 됩니다.");

        if (!m.UseMineSize)
        {
            p.Children.Add(SliderRow("가로 크기", ClassProfiles.MinScale, ClassProfiles.MaxScale, m.Width, 0.05, v => $"{v * 100:0}%", v => edit().Members.Width = v, Changed));
            p.Children.Add(SliderRow("세로 크기", ClassProfiles.MinScale, ClassProfiles.MaxScale, m.Height, 0.05, v => $"{v * 100:0}%", v => edit().Members.Height = v, Changed));
            p.Children.Add(Note("보내기·받기의 '파티원 크기 받기'나 '파티원 배치 받기'를 켜면, 받은 크기와 배치를 이 크기 안에 비율을 지켜 맞춥니다. 이보다 커지지 않습니다."));
        }
        if (m.UseMineColor) return;

        p.Children.Add(Row("색", EnumCombo([(ColorSource.Received, "파티원 설정 받기"), (ColorSource.Default, "기본 (역할 색)"), (ColorSource.Personal, "개인 설정")],
            m.Color, v => { edit().Members.Color = v; Refill(); })));
        p.Children.Add(Note("기본: 일반스킬은 아래 기본 역할 색, 나머지는 기본 색입니다. 파티원 설정 받기: 파티원이 보낸 색이고, 아직 받지 못한 그룹은 기본 색입니다."));
        ColorRows(p, m, () => edit().Members, g => ClassProfiles.BuiltIn[g],
            enabled: m.Color == ColorSource.Personal);
    }

    void CopyPart(Panel p, string cls)
    {
        var names = _chars.Classes.Select(c => c.Name).ToList();
        var parts = new List<UIElement>();
        foreach (var (part, text) in new[] { (CopyParts.Color, "색"), (CopyParts.Size, "크기"), (CopyParts.Layout, "배치"), (CopyParts.Display, "표시"), (CopyParts.Effects, "효과") })
            parts.Add(Check(text, _copyParts.HasFlag(part), v => _copyParts = v ? _copyParts | part : _copyParts & ~part, () => { }));
        p.Children.Add(Row("복사할 것", [.. parts]));
        p.Children.Add(Row("가져올 설정", SideCombo(_copyFromMembers, v => _copyFromMembers = v), Label($"({ClassName(cls)})", 0.7)));
        var to = PickCombo(names.Prepend(ClassProfiles.Template).Prepend(AllClasses), _copyTo, v => _copyTo = v);
        var copy = new Button { Content = "복사" };
        copy.Click += (_, _) =>
        {
            if (_copyParts == 0) { _copyResult = "복사할 것을 고르세요."; Dispatcher.BeginInvoke(FillClassSections); return; }
            var c = ClassProfiles.Of(_s, cls);   // copying from a class doesn't make it 개별
            ClassProfile from = _copyFromMembers ? c.Members : c.Mine;
            // 모든 클래스: 기본 and the 개별 classes (the rest follow 기본).
            var classes = _copyTo == AllClasses ? names.Where(n => ClassProfiles.IsOwn(_s, n)).Prepend(ClassProfiles.Template) : [_copyTo];
            var targets = classes.Select(n => ClassProfiles.Edit(_s, n)).Select(t => _copyToMembers ? (ClassProfile)t.Members : t.Mine).ToList();
            ClassProfiles.Copy(_s, from, targets, _copyParts);
            _copyResult = $"{targets.Count(t => t != from)}곳에 복사했습니다.";
            Changed();
            Dispatcher.BeginInvoke(FillClassSections);
        };
        p.Children.Add(Row("대상", SideCombo(_copyToMembers, v => _copyToMembers = v), to, copy));
        if (_copyResult is { } result) p.Children.Add(Note(result));
    }

    /// <summary>표시: groups, gauge and hidden slots of <paramref name="read"/>; edits go to <paramref name="edit"/>().</summary>
    void DisplayRows(Panel p, ClassProfile read, Func<ClassProfile> edit, bool enabled)
    {
        if (!enabled) return;   // following 나: nothing of its own to show
        var groups = new List<UIElement>();
        foreach (var g in Enum.GetValues<SlotGroup>())
            groups.Add(Check(g.ToString(), read.Groups.GetValueOrDefault(g, true), v => edit().Groups[g] = v, Changed));
        groups.Add(Check("아덴 게이지", read.Gauge, v => edit().Gauge = v, Changed));
        p.Children.Add(Row(Described("표시", "끈 그룹은 빠지고 판이 그만큼 줄어듭니다"), [.. groups]));
        var slots = new List<UIElement>();
        foreach (var id in SlotInfo.All)
            slots.Add(Check(SlotWithKey(_s, id), read.HiddenSlots.Contains(id), v =>
            {
                var hidden = edit().HiddenSlots;
                if (v) hidden.Add(id); else hidden.Remove(id);
            }, Changed));
        p.Children.Add(Row(Described("숨길 칸", "빈 칸 등을 숨깁니다. 자리는 비워 둡니다"), [.. slots]));
    }

    void PlacementRow(Panel p, ClassProfile read, Func<ClassProfile> edit, string hint)
    {
        var reset = new Button { Content = "칸 배치 되돌리기", IsEnabled = read.Layout is not null, ToolTip = "처음의 고정된 배치로 되돌립니다." };
        reset.Click += (_, _) => { edit().Layout = null; Changed(); Dispatcher.BeginInvoke(FillClassSections); };
        p.Children.Add(Row(Described("칸 배치", hint), Label(read.Layout is null ? "기본 (고정된 배치)" : "사용자 (옮긴 배치)", 0.7), reset));
    }

    /// <summary>A colour row per group: the profile's own colour, else <paramref name="fallback"/> (shown while it isn't
    /// 개인 설정, when the rows are disabled).</summary>
    /// <param name="icons">Groups drawn as icons (yours only): their rows are left out.</param>
    void ColorRows(Panel p, ClassProfile read, Func<ClassProfile> edit, Func<SlotGroup, string> fallback, bool enabled, bool skillsOwnRows = false, bool dims = false,
        IReadOnlySet<SlotGroup>? icons = null)
    {
        foreach (var g in Enum.GetValues<SlotGroup>())
        {
            if (skillsOwnRows && g == SlotGroup.일반스킬) continue;   // the eight rows of SkillColorRows stand in for it
            if (icons?.Contains(g) == true) continue;
            string colour = read.Colors.GetValueOrDefault(g) ?? fallback(g);
            var row = ColorRow(Label(g.ToString()), colour, v => edit().Colors[g] = v, swatch: true, warnGold: true);
            row.Visibility = Shown(enabled);
            p.Children.Add(row);
            if (!dims) continue;
            var dim = DimRow(colour, read.DimColors.GetValueOrDefault(g), v => edit().DimColors[g] = v);
            dim.Visibility = Shown(enabled);
            p.Children.Add(dim);
        }
    }

    /// <summary>쿨타임 색 of a colour row: the chosen one, else <paramref name="colour"/> dimmed as the overlay does it
    /// automatically, which is where the picker starts.</summary>
    UIElement DimRow(string colour, string? chosen, Action<string> set) =>
        ColorRow(Label("      쿨타임"), chosen ?? (ParseHex(colour) is { } c && OverlayRenderer.Level(c, CooldownLook.CooldownLevel) is var d
            ? $"#{d.R:X2}{d.G:X2}{d.B:X2}" : colour), set, swatch: true);

    /// <summary>일반스킬 모두 같은 색: on, the one 일반스킬 row of <see cref="ColorRows"/>; off, the eight skills each with its
    /// own row (yours only, not sent to the party), shown with its key, one not set in the group's colour.</summary>
    void SkillColorRows(Panel p, ClassProfile read, Func<ClassProfile> edit, bool enabled)
    {
        var rows = new StackPanel { Visibility = Shown(enabled) };
        rows.Children.Add(Check("일반스킬 모두 같은 색", read.SkillsOneColor,
            v => { edit().SkillsUnified = v; Dispatcher.BeginInvoke(FillClassSections); }, Changed));
        p.Children.Add(rows);
        if (read.SkillsOneColor) return;
        rows.Children.Add(Note("스킬마다 색을 정합니다. 내 화면에만 쓰입니다."));
        string group = read.Colors.GetValueOrDefault(SlotGroup.일반스킬) ?? ClassProfiles.BuiltIn[SlotGroup.일반스킬];
        for (int i = 0; i < 8; i++)
        {
            var id = SlotId.일반1 + i;
            string colour = read.SlotColors.GetValueOrDefault(id) ?? group;
            rows.Children.Add(ColorRow(Label($"   {SlotWithKey(_s, id)}"), colour, v => edit().SlotColors[id] = v, swatch: true, warnGold: true));
            if (!read.AutoDim)   // a skill's own cooldown colour goes with its own colour
                rows.Children.Add(DimRow(colour, read.SlotDimColors.GetValueOrDefault(id),
                    v => { var e = edit(); e.SlotColors.TryAdd(id, colour); e.SlotDimColors[id] = v; }));
        }
    }

    /// <summary>나 / 파티원.</summary>
    static ComboBox SideCombo(bool members, Action<bool> set)
    {
        var c = new ComboBox { MinWidth = 90, ItemsSource = new[] { "나", "파티원" }, SelectedIndex = members ? 1 : 0 };
        c.SelectionChanged += (_, _) => set(c.SelectedIndex == 1);
        return c;
    }

    /// <summary>A class picker (template and 모든 클래스 named).</summary>
    static ComboBox PickCombo(IEnumerable<string> classes, string value, Action<string> set)
    {
        var c = Combo();
        c.ItemsSource = classes.Select(n => new ComboBoxItem { Content = n == AllClasses ? "모든 클래스" : ClassName(n), Tag = n }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value) ?? c.Items[0];
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: string v }) set(v); };
        return c;
    }
}
