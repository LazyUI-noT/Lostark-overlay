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
        ShowRecords(opened, compared);
        _secRecords.Children.Clear();
        _secRecords.Children.Add(TimelineBar(picked));
        if (_registerNote is { } note) _secRecords.Children.Add(Note(note));
        _secRecords.Children.Add(TimelineWithList(all, opened));
        if (!_recordsHooked) { _recordsHooked = true; Body.SizeChanged += (_, _) => FitTimeline(); }
        Dispatcher.BeginInvoke(FitTimeline, DispatcherPriority.Loaded);
    }

    /// <summary>The timeline with this character's fights beside it (user, 2026-11-03): a row opens its fight; 비교 on a fight of
    /// the same raid · gate · difficulty puts its curves on the opened one's chart.</summary>
    UIElement TimelineWithList(IReadOnlyList<(string File, FightRecord Record)> all, (string File, FightRecord Record) opened)
    {
        string mine = ChipOf(opened);
        var list = new StackPanel();
        foreach (var x in all.Where(x => ChipOf(x) == mine)) list.Children.Add(SideRow(x, opened));
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
        text.Children.Add(new TextBlock { Text = $"{result}{r.StartedAt:MM-dd HH:mm}", FontWeight = isOpened ? FontWeights.SemiBold : FontWeights.Normal });
        text.Children.Add(new TextBlock
        {
            Text = $"{r.Gate ?? "알 수 없음"}{(string.IsNullOrEmpty(r.Difficulty) ? "" : $" [{r.Difficulty}]")}", FontSize = 11, Opacity = 0.75,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
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
    void ShowRecords((string File, FightRecord Record) opened, IReadOnlyList<(string File, FightRecord Record)> compared)
    {
        string shows = string.Join("|", compared.Prepend(opened).Select(x => x.File + "#" + x.Record.StartedAt.Ticks)) + "@" + _comparePhase;
        if (shows != _timelineShows)
        {
            _timelineShows = shows;
            var icons = RecordIcons(opened.Record);
            _hasIcons = icons.Count > 0;
            int phase = compared.Count > 0 ? _comparePhase : 0;
            _timeline.Show((opened.Record, Timeline.Offset(opened.Record, phase), icons), [.. compared.Select(x => (x.Record, Timeline.Offset(x.Record, phase)))]);
        }
        // A 4-man gate (by the fight's boss HP, else its name): the party's buff share too. Set before Keys, which redraws.
        var r = opened.Record;
        _timeline.SmallParty = (RaidData.ByHp(RaidGates, BossBook.FightHp(r.Boss))?.Gate ?? RaidGates.FirstOrDefault(g => RaidData.Label(g) == r.Gate))?.SmallParty == true;
        _timeline.Keys = _s.Keys;
        _timeline.UseIcons = _useIcons ?? _hasIcons;
        _timeline.FontScale = Math.Clamp(_s.RecordFontPercent, Settings.MinRecordFont, Settings.MaxRecordFont) / 100.0;
    }

    /// <summary>The top bar: ← 목록, the character and the fight(s), 보관 for one fight or 비교 맞추기 for two, 아이콘 / 키 when
    /// the fight has icons, and 스킬 등록.</summary>
    UIElement TimelineBar(IReadOnlyList<(string File, FightRecord Record)> picked)
    {
        var first = picked[0];
        var bar = new WrapPanel { Margin = new(0, 0, 0, 4) };
        var back = new Button { Content = "← 목록", Margin = new(0, 0, 12, 0) };
        back.Click += (_, _) => CloseRecord();
        bar.Children.Add(back);
        bar.Children.Add(BarText(ChipOf(first), bold: true));
        bar.Children.Add(BarText(string.Join("   vs   ", picked.Select(x => FightTitle(x.Record)))));
        if (picked.Count == 1)
        {
            bar.Children.Add(KeepBox(first));
            if (Timeline.AmsuGain(first.Record) is var (mine, all, share))   // 10.35% of the damage done while a bomb was up
                bar.Children.Add(BarText($"암수 이득: 내 {TimelineView.Amount(mine)} ({share * 100:0.0}%) · 전체 {TimelineView.Amount(all)}"));
        }
        else bar.Children.Add(AlignBox(picked));
        if (_hasIcons) AddLaneToggles(bar);
        var register = new Button { Content = "스킬 등록", Margin = new(12, 0, 0, 0) };
        register.Click += (_, _) => RegisterSkills(first.Record);
        bar.Children.Add(register);
        return bar;
    }

    static TextBlock BarText(string text, bool bold = false) => new()
    {
        Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0),
    };

    static string FightTitle(FightRecord r) =>
        $"{r.StartedAt:MM-dd HH:mm} · {r.Gate ?? "알 수 없음"}{(string.IsNullOrEmpty(r.Difficulty) ? "" : $" [{r.Difficulty}]")} · {(int)r.Duration / 60}:{(int)r.Duration % 60:00}";

    CheckBox KeepBox((string File, FightRecord Record) x)
    {
        var (store, name) = StoreOf(x.File);
        var keep = new CheckBox { Content = "보관", IsChecked = x.Record.Kept, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) };
        keep.Click += (_, _) => RecordAction(() => store?.SetKept(name, keep.IsChecked == true));
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
        string character = RecordCharacter.Of(r);
        _registerNote = SkillRegisterAdvice.For(character, ActiveLabel(), _gameRunning) switch
        {
            SkillRegister.Go => null,
            SkillRegister.PlayThatCharacter => $"이 전투의 캐릭터({character})로 게임에 접속해, 스킬을 쓸 수 있는 곳에서 일반 스킬이 모두 준비됐을 때 여기의 스킬 등록을 누르세요. 캐릭터 → 스킬창의 스킬 등록이나 트레이 메뉴에서도 됩니다.",
            _ => $"게임이 켜져 있을 때 등록합니다. 이 전투의 캐릭터({character})로 접속해, 일반 스킬이 모두 준비됐을 때 스킬 등록을 누르세요.",
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
