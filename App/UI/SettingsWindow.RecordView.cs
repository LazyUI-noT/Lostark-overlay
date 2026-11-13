using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록's timeline page (records-timeline.design.md): a top bar (← 목록, the character and the fight, 보관 or
/// 비교 맞추기, 아이콘 / 키, 스킬 등록) and the timeline filling the rest of the page. One fight, or the two picked for 비교.</summary>
public partial class SettingsWindow
{
    const double PageSpare = 40;   // the page's padding and the cards' edges below the timeline

    /// <summary>At most this many fights compared with the opened one (user, 2026-11-03).</summary>
    const int MaxCompared = 3;

    void FillTimeline(IReadOnlyList<(string File, FightRecord Record)> all)
    {
        if (_opened is null) { _opened = _compare[0]; _compare.RemoveAt(0); }   // two picked in the list: the first opens, the second compares
        if (all.FirstOrDefault(x => x.File == _opened) is not { File: not null } opened) { FillList(all); return; }
        _compare.RemoveAll(f => all.FirstOrDefault(x => x.File == f) is not { File: not null } c || !Timeline.SameRaid(opened.Record, c.Record));
        var compared = _compare.Select(f => all.First(x => x.File == f)).ToList();
        var picked = new List<(string File, FightRecord Record)> { opened };
        picked.AddRange(compared);
        if (compared.Count > 0) _comparePhase = Math.Min(_comparePhase, picked.Max(x => x.Record.Phases.Count));
        ShowRecords(opened, compared, all);
        _secRecords.Children.Clear();
        _secRecords.Children.Add(TimelineBar(picked));
        var checks = ChartChecks(opened.Record);
        AddSkillRate(checks, opened, compared);
        _secRecords.Children.Add(checks);
        if (_registerNote is { } note) _secRecords.Children.Add(Note(note));
        _secRecords.Children.Add(TimelineWithList(all, opened));
        if (!_recordsHooked) { _recordsHooked = true; Body.SizeChanged += (_, _) => FitTimeline(); }
        Dispatcher.BeginInvoke(FitTimeline, DispatcherPriority.Loaded);
    }

    bool _otherChars;   // 다른 캐릭터: the side list also offers the other characters' and the imported fights of this raid

    /// <summary>The timeline with this character's fights beside it (user, 2026-11-03): a row opens its fight; 비교 on a fight of
    /// the same raid · gate · difficulty puts its curves on the opened one's chart. 다른 캐릭터 (user, 2026-11-04) adds the other
    /// characters' and the imported fights of that raid and difficulty.</summary>
    UIElement TimelineWithList(IReadOnlyList<(string File, FightRecord Record)> all, (string File, FightRecord Record) opened)
    {
        string mine = ChipOf(opened);
        var list = new StackPanel();
        var others = new CheckBox { Content = "다른 캐릭터", IsChecked = _otherChars, Margin = new(6, 0, 0, 6), ToolTip = "다른 캐릭터와 가져온 기록 중 같은 레이드 · 같은 난이도도 비교합니다." };
        others.Click += (_, _) => { _otherChars = others.IsChecked == true; FillRecords(); };
        list.Children.Add(others);
        foreach (var x in all.Where(x => ChipOf(x) == mine)) list.Children.Add(SideRow(x, opened));
        if (_otherChars)
            foreach (var x in all.Where(x => ChipOf(x) is var chip && chip != mine && chip != TestChip && Timeline.SameRaid(opened.Record, x.Record)))
                list.Children.Add(SideRow(x, opened));
        var side = new ScrollViewer { Content = list, Width = 250, Margin = new(0, 8, 8, 4), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        side.SetBinding(HeightProperty, new System.Windows.Data.Binding(nameof(ActualHeight)) { Source = _timeline });
        if (_timeline.Parent is Panel old) old.Children.Remove(_timeline);
        var grid = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        Grid.SetColumn(_timeline, 1);
        grid.Children.Add(side);
        grid.Children.Add(_timeline);
        return grid;
    }

    /// <summary>One fight in the side list: the opened one marked, the same raid lit with 비교, the rest dimmed.</summary>
    UIElement SideRow((string File, FightRecord Record) x, (string File, FightRecord Record) opened)
    {
        var r = x.Record;
        bool isOpened = x.File == opened.File, same = !isOpened && Timeline.SameRaid(opened.Record, r);
        string result = r.Cleared switch { true => "클리어 ", false => "실패 ", _ => "" };
        var text = new StackPanel { Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
        string whose = ChipOf(x) == ChipOf(opened) ? "" : IsImported(x.File) ? $"{ImportChip} " : $"{RecordCharacter.Of(r)} · ";
        text.Children.Add(new TextBlock { Text = $"{whose}{result}{r.StartedAt:MM-dd HH:mm}", FontWeight = isOpened ? FontWeights.SemiBold : FontWeights.Normal });
        // [logo] raid [difficulty] (user, 2026-11-10); a long raid name shortens, the capsule stays
        var logo = RaidBadgeView.Of(r.Gate, null, size: 10);
        var gate = Muted(new TextBlock { Text = r.Gate ?? "알 수 없음", FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var difficulty = RaidBadgeView.Of(null, r.Difficulty, size: 10);
        difficulty.Margin = new(6, 0, 0, 0);
        var line = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (e, i) in new UIElement[] { logo, gate, difficulty }.Select((e, i) => (e, i)))
        {
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(e, i);
            line.Children.Add(e);
        }
        text.Children.Add(line);
        if (!isOpened) text.MouseLeftButtonUp += (_, _) => OpenRecord(x.File);
        var row = new DockPanel { Margin = new(0, 2, 0, 2), Opacity = isOpened || same ? 1 : 0.45 };
        if (same)
        {
            var compare = new CheckBox { Content = "비교", IsChecked = _compare.Contains(x.File), VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
            compare.Click += (_, _) =>
            {
                if (!_compare.Remove(x.File)) { _compare.Add(x.File); if (_compare.Count > MaxCompared) _compare.RemoveAt(0); }
                FillRecords();
            };
            DockPanel.SetDock(compare, Dock.Right);
            row.Children.Add(compare);
        }
        row.Children.Add(text);
        var box = new Border { Child = row, Padding = new(6, 3, 6, 3), CornerRadius = new(4), Background = Brushes.Transparent };
        if (isOpened) box.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");   // the theme's, light or dark
        return box;
    }

    /// <summary>Gives the timeline its fights. They are handed over again only when they changed, so the same ones keep their zoom.</summary>
    void ShowRecords((string File, FightRecord Record) opened, IReadOnlyList<(string File, FightRecord Record)> compared, IReadOnlyList<(string File, FightRecord Record)> all)
    {
        // The whole run on one chart, whichever of its files was opened: from this character's list only, so imported ones join only each other.
        var run = FightRegions.Chain(opened.Record, all.Where(x => x.File is not null && ChipOf(x) == ChipOf(opened)).Select(x => x.Record));
        string shows = string.Join("|", compared.Prepend(opened).Select(x => x.File + "#" + x.Record.StartedAt.Ticks)) + "@" + _comparePhase + "+" + string.Join(",", run.Select(f => f.StartedAt.Ticks));
        if (shows != _timelineShows)
        {
            _timelineShows = shows;
            var icons = RecordIcons(opened.Record);
            _hasIcons = icons.Count > 0;
            int phase = compared.Count > 0 ? _comparePhase : 0;
            var whole = FightRegions.Join(run);
            _timelineWhole = whole;
            _timeline.Show((whole, Timeline.Offset(opened.Record, phase), icons), [.. compared.Select(x =>
                new TimelineView.ComparedFight(x.Record, Timeline.Offset(x.Record, phase), LookOf(x, opened), LabelOf(x, opened)))], FightRegions.Of(whole, RaidGates),
                UnhittableOf(whole), max => RaidData.HpPerLine(RaidGates, whole.Gate, max), MarksOf(whole));
        }
        // A 4-man gate (by the fight's boss HP, else its name): the party's buff share too. Set before Keys, which redraws.
        var r = opened.Record;
        _timeline.BuffShareShown = _s.Meter.BuffShare() != ThreeWay.끄기;
        _timeline.BuffLocked = Services.BuffUnlock.Locked;
        _timeline.SmallParty = (RaidData.ByHp(RaidGates, BossBook.FightHp(r.Boss))?.Gate ?? RaidGates.FirstOrDefault(g => RaidData.Label(g) == r.Gate))?.SmallParty == true;
        _timeline.Keys = _s.Keys;
        _timeline.UseIcons = _useIcons ?? _hasIcons;
        _timeline.FontScale = Math.Clamp(_s.RecordFontPercent, Settings.MinRecordFont, Settings.MaxRecordFont) / 100.0;
    }

    FightRecord? _timelineWhole;   // the run on the chart: what the skill average counts

    IReadOnlyList<(double T, bool Enter)> MarksOf(FightRecord f) =>
        Timeline.GimmickMarks(f, (RaidData.ByHp(RaidGates, BossBook.FightHp(f.Boss))?.Gate ?? RaidGates.FirstOrDefault(g => RaidData.Label(g) == f.Gate))?.IsShadow == true);

    IReadOnlyList<(double From, double To)> UnhittableOf(FightRecord f) =>
        Timeline.Outside(Timeline.BossUnhittable(f, max => RaidData.HpPerLine(RaidGates, f.Gate, max)), Timeline.GimmickSpans(f, MarksOf(f)));

    /// <summary>Beside the chart's checks: a skill to ring and average, picked per class · 직업각인 whatever the meter counts (it starts on
    /// the meter's), 보스 타격 불가 제외, and the average of the opened fight and each compared one of its class.</summary>
    void AddSkillRate(Panel row, (string File, FightRecord Record) opened, IReadOnlyList<(string File, FightRecord Record)> compared)
    {
        var whole = _timelineWhole ?? opened.Record;
        string key = RecordCharacter.ClassOf(opened.Record);
        string Name(SlotId s) => SkillNameOf(whole, s);
        var used = whole.Events.Where(e => e.Name == whole.Me && e.Kind == RecordEventKind.사용).Select(e => (SlotId)e.What).ToHashSet();
        if (ClassProfiles.WindowsOf(_s, key).TimelineSkill is { } pick) used.Add(pick);
        var skill = Combo();
        skill.MinWidth = 110;
        skill.VerticalAlignment = VerticalAlignment.Center;
        skill.ItemsSource = SkillChoices(s => $"{SlotKeyName(s)} · {Name(s)}", SlotInfo.All.Where(used.Contains));
        skill.SelectedItem = skill.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (SlotId?)i.Tag == ClassProfiles.WindowsOf(_s, key).TimelineSkill) ?? skill.Items[0];
        var exclude = new CheckBox { Content = "보스 타격 불가 제외", IsChecked = _s.Meter.SkillRateExcludes, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 12, 0),
            ToolTip = "평균을 낼 때 보스 타격 불가 시간과 그 사이에 쓴 스킬을 뺍니다." };
        var rate = BarText("", bold: true);
        var sameClass = compared.Where(x => RecordCharacter.ClassOf(x.Record) == RecordCharacter.ClassOf(opened.Record)).ToList();   // a slot is another skill in another class
        void Update()
        {
            var slot = ClassProfiles.WindowsOf(_s, key).TimelineSkill;
            _timeline.RingSlot = slot;
            exclude.Visibility = slot is null ? Visibility.Collapsed : Visibility.Visible;
            rate.Text = slot is { } s ? $"{SlotKeyName(s)} {RateOf(whole, s)}" + string.Concat(sameClass.Select(x => $" · {LabelOf(x, opened)} {RateOf(x.Record, s)}")) : "";
        }
        skill.SelectionChanged += (_, _) =>
        {
            if (skill.SelectedItem is not ComboBoxItem item) return;
            ClassProfiles.EditWindows(_s, key).TimelineSkill = (SlotId?)item.Tag;
            Changed();
            Update();
        };
        exclude.Click += (_, _) => { _s.Meter.SkillRateExcludes = exclude.IsChecked == true; Changed(); Update(); };
        // One group, so it wraps whole beside the chart's checks (user, 2026-11-09): the skill, its rate, then 제외 for it.
        rate.Margin = new(8, 0, 0, 0);
        exclude.Margin = new(16, 0, 0, 0);
        exclude.MinWidth = 0;
        var group = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var e in new UIElement[] { BarText("스킬"), skill, rate, exclude }) group.Children.Add(e);
        row.Children.Add(group);
        Update();
    }

    /// <summary><paramref name="s"/>'s name in <paramref name="f"/>: the name given on the meter, the record's, else the key.</summary>
    string SkillNameOf(FightRecord f, SlotId s) =>
        ClassProfiles.WindowsOf(_s, RecordCharacter.ClassOf(f)).NameOf(s) ?? f.Skills?.GetValueOrDefault(s)?.Name ?? SlotKeyName(s);

    /// <summary>My casts of <paramref name="s"/> a minute, 보스 타격 불가 taken off when 제외 is on.</summary>
    string RateOf(FightRecord f, SlotId s) =>
        Timeline.SkillRate(f, s, _s.Meter.SkillRateExcludes ? UnhittableOf(f) : []) is { } n ? $"{n:0.0}회/분" : "–";

    /// <summary>A compared fight's look: the opened character's, my other character's, or imported.</summary>
    static TimelineView.CompareLook LookOf((string File, FightRecord Record) x, (string File, FightRecord Record) opened) =>
        IsImported(x.File) ? TimelineView.CompareLook.Imported
            : ChipOf(x) == ChipOf(opened) ? TimelineView.CompareLook.Same : TimelineView.CompareLook.OtherCharacter;

    /// <summary>A compared fight in the legend: its date, with its character or [가져옴] and class when not the opened one's.</summary>
    static string LabelOf((string File, FightRecord Record) x, (string File, FightRecord Record) opened) => LookOf(x, opened) switch
    {
        TimelineView.CompareLook.Imported => $"{ImportChip} {RecordCharacter.ClassOf(x.Record)} {x.Record.StartedAt:MM-dd HH:mm}",
        TimelineView.CompareLook.OtherCharacter => $"{RecordCharacter.Of(x.Record)} {x.Record.StartedAt:MM-dd HH:mm}",
        _ => $"{x.Record.StartedAt:MM-dd HH:mm}",
    };

    /// <summary>The top bar: ← 목록, the character and the fight(s), 보관 for one fight or 비교 맞추기 for two, 아이콘 / 키 when
    /// the fight has icons, and 스킬 등록.</summary>
    UIElement TimelineBar(IReadOnlyList<(string File, FightRecord Record)> picked)
    {
        var first = picked[0];
        // Two rows (user, 2026-11-09): what the fight is, then what can be done with it, on the right. One row wrapped anywhere.
        var bar = new WrapPanel { Margin = new(0, 0, 0, 4) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 0, 4) };
        var back = new Button { Content = "← 목록", Margin = new(0, 0, 12, 0) };
        back.Click += (_, _) => CloseRecord();
        bar.Children.Add(back);
        bar.Children.Add(BarText(ChipOf(first), bold: true));
        foreach (var (x, i) in picked.Select((x, i) => (x, i)))   // each fight: date · gate, its difficulty capsule, its length
        {
            if (i > 0) bar.Children.Add(BarText("vs"));
            bar.Children.Add(BarText($"{x.Record.StartedAt:MM-dd HH:mm} ·"));
            bar.Children.Add(RaidBadgeView.Of(x.Record.Gate, null));   // [logo] raid [difficulty] (user, 2026-11-10)
            bar.Children.Add(new TextBlock { Text = x.Record.Gate ?? "알 수 없음", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) });
            var difficulty = RaidBadgeView.Of(null, x.Record.Difficulty);
            difficulty.Margin = new(0, 0, 12, 0);
            bar.Children.Add(difficulty);
            bar.Children.Add(BarText($"{(int)x.Record.Duration / 60}:{(int)x.Record.Duration % 60:00}"));
            if (RecordPower.Label(x.Record) is { } power)   // item level · 전투력 when saved (lostark-api.plan.md phase 5), in the 직업각인's role colour
            {
                var text = BarText(power);
                text.Foreground = HomeworkWindow.RoleBrush(MenuPower.RoleIn(_chars.Classes, CombatPowerBook.KeyOf(x.Record.MySubclass)));
                bar.Children.Add(text);
            }
        }
        if (picked.Count == 1)
        {
            if (Timeline.AmsuGain(first.Record) is var (mine, all, share))   // 10.35% of the damage done while a bomb was up
                bar.Children.Add(BarText($"암수 이득: 내 {AmountText.Damage(mine)} ({share * 100:0.0}%) · 전체 {AmountText.Damage(all)}"));
            if (!IsImported(first.File)) actions.Children.Add(KeepButton(first.File, first.Record.Kept, new(0, 0, 12, 0)));   // imported: kept for good, no 보관 (as in the list)
            if (MeterCompareButton(first.File, first.Record, new(0, 0, 12, 0)) is { } meter) actions.Children.Add(meter);
        }
        else actions.Children.Add(AlignBox(picked));
        if (_hasIcons) AddLaneToggles(actions);
        var register = new Button { Content = "스킬 등록", Margin = new(12, 0, 0, 0) };
        register.Click += (_, _) => RegisterSkills(first.Record);
        actions.Children.Add(register);
        var export = new Button { Content = picked.Count > 1 ? $"내보내기 ({picked.Count})" : "내보내기", Margin = new(12, 0, 0, 0), ToolTip = "보이는 전투를 다른 사람에게 보낼 파일로 저장합니다." };
        export.Click += (_, _) => ExportFights([.. picked.Select(x => x.Record)]);
        actions.Children.Add(export);
        return new StackPanel { Children = { bar, actions } };
    }

    /// <summary>What the chart shows: its kinds of line, 보스 HP, 보스 타격 불가, and each player.</summary>
    WrapPanel ChartChecks(FightRecord r)
    {
        var row = new WrapPanel { Margin = new(0, 0, 0, 4) };
        CheckBox Box(string text, bool on, Action<bool> set)
        {
            // MinWidth 0: Fluent's 120 left a wide gap after a short label (user, 2026-11-09)
            var box = new CheckBox { Content = text, IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0), MinWidth = 0 };
            box.Click += (_, _) => set(box.IsChecked == true);
            row.Children.Add(box);
            return box;
        }
        void Part(string text, TimelineView.ChartParts part) => Box(text, _timeline.Parts.HasFlag(part), on =>
            _timeline.Parts = on ? _timeline.Parts | part : _timeline.Parts & ~part);
        Part("초당 피해량", TimelineView.ChartParts.Dps);
        Part("피해량", TimelineView.ChartParts.Damage);
        Part("보스 HP", TimelineView.ChartParts.BossHp);
        Part("보스 타격 불가", TimelineView.ChartParts.Unhittable);
        foreach (string name in r.Samples.Select(s => s.Name).Distinct().OrderBy(n => n != r.Me))
            Box(name, _timeline.Shows(name), on => _timeline.SetShown(name, on));
        return row;
    }

    static TextBlock BarText(string text, bool bold = false) => new()
    {
        Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0),
    };

    /// <summary>보관 as a button, gold (the theme's on colour) while kept (user, 2026-11-10). Off on a fight past the newest
    /// <see cref="Settings.RecordKeep"/>, it goes to the 휴지통 at once, after a warning.</summary>
    System.Windows.Controls.Primitives.ToggleButton KeepButton(string file, bool kept, Thickness margin)
    {
        var (store, name) = StoreOf(file);
        var keep = new System.Windows.Controls.Primitives.ToggleButton
        {
            Content = "보관", IsChecked = kept, Margin = margin, Padding = new(10, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource(typeof(System.Windows.Controls.Primitives.ToggleButton)) as Style,
            ToolTip = kept ? "보관을 풉니다. 최근 기록 수 밖의 기록이면 바로 휴지통으로 갑니다." : "보관하면 최근 기록에서 밀려나도 보관 탭에 남습니다.",
        };
        keep.Click += (_, _) =>
        {
            bool on = keep.IsChecked == true;
            if (!on && store?.UnkeepWouldBin(name, _s.RecordKeep) == true
                && !Warn(WarningKind.UnkeepToBin, "이 기록은 최근 기록 수 밖이라 보관을 풀면 바로 휴지통으로 갑니다. 휴지통에서 되돌리기를 누르면 돌아옵니다."))
            {
                keep.IsChecked = true;
                return;
            }
            RecordAction(() => store?.SetKept(name, on, _s.RecordKeep));
        };
        return keep;
    }

    /// <summary>비교 맞추기: the two fights line up at the start, or at the start of a phase.</summary>
    StackPanel AlignBox(IReadOnlyList<(string File, FightRecord Record)> picked)
    {
        int phases = picked.Max(x => x.Record.Phases.Count);
        var align = Combo();
        align.ItemsSource = Enumerable.Range(0, phases + 1).Select(i => new ComboBoxItem { Content = i == 0 ? "전투 시작" : $"페이즈 {i + 1} 시작", Tag = i }).ToList();
        align.SelectedIndex = _comparePhase;
        align.SelectionChanged += (_, _) => { if (align.SelectedItem is ComboBoxItem { Tag: int i }) { _comparePhase = i; FillRecords(); } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new(0, 0, 12, 0),
            Children = { BarText("비교 맞추기"), align },
        };
    }

    /// <summary>아이콘 / 키: my lane's casts drawn with their icons or with their keys.</summary>
    void AddLaneToggles(WrapPanel bar)
    {
        bool icons = _timeline.UseIcons;
        var iconBox = Toggle("아이콘", "skillLane", icons);
        iconBox.Checked += (_, _) => SetIcons(true);
        var keyBox = Toggle("키", "skillLane", !icons);
        keyBox.Checked += (_, _) => SetIcons(false);
        bar.Children.Add(iconBox);
        bar.Children.Add(keyBox);
    }

    void SetIcons(bool on)
    {
        _useIcons = on;
        FillRecords();
    }

    /// <summary>스킬 등록 registers the character being played. Another character's fight, or the game off, gets instructions
    /// instead (SkillRegisterAdvice): the button can't capture another character's bar.</summary>
    void RegisterSkills(FightRecord r)
    {
        string character = RecordCharacter.ClassOf(r);
        _registerNote = SkillRegisterAdvice.For(character, ActiveLabel(), _gameRunning) switch
        {
            SkillRegister.Go => null,
            SkillRegister.SetClassFirst => "이 전투에는 클래스가 없습니다. 전투 기록 목록에서 이 전투의 편집을 눌러 클래스와 직업각인을 정한 뒤 스킬 등록을 누르세요.",
            SkillRegister.PlayThatCharacter => $"이 전투의 캐릭터({character})로 게임에 접속해, 스킬을 쓸 수 있는 곳에서 일반스킬이 모두 준비됐을 때 여기의 스킬 등록을 누르세요. 화면·인식 → 스킬창 맞추기의 스킬 등록이나 트레이 메뉴에서도 됩니다.",
            _ => $"게임이 켜져 있을 때 등록합니다. 이 전투의 캐릭터({character})로 접속해, 일반스킬이 모두 준비됐을 때 스킬 등록을 누르세요.",
        };
        if (_registerNote is null) SkillsSubmitRequested?.Invoke();   // the result comes back through SetSkillsResult
        FillRecords();
    }

    /// <summary>스킬 등록 finished, or failed: the timeline redraws with the icons it has now, and the page says what happened.</summary>
    void SkillsRegistered(string text)
    {
        (_registerNote, _timelineShows) = (text, null);
        FillRecords();
    }

    /// <summary>The timeline takes the rest of the page under its bar; its curves grow into the room.</summary>
    void FitTimeline()
    {
        if (!_timeline.IsDescendantOf(_secRecords)) return;
        _timeline.MinHeight = Math.Max(0, Body.ActualHeight - (_secRecords.ActualHeight - _timeline.ActualHeight) - PageSpare);
    }
}
