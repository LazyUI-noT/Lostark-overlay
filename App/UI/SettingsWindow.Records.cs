using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록 (spec § D, records-timeline.design.md): the saved fights of one character (a chip each), 자동 저장 and 보관
/// apart, with 보관, 비교 and 삭제 per row. Opening a row, or two picked for 비교, swaps the page to its timeline
/// (SettingsWindow.RecordView.cs).</summary>
public partial class SettingsWindow
{
    const string AreaRecords = "전투 기록";
    const string TestChip = "테스트";   // the 테스트 모드 fights' chip: no class is named that
    const string TestKey = "test/";   // a test record's key in the list (_opened, _compare): its own file names may repeat the game's
    const string RecordsLead = "DPS 미터기가 따라간 전투를 저장합니다. 자동 기록은 정한 개수만 남기고(가장 오래된 것부터 지움), 보관한 기록은 개수에 들지 않고 지울 때까지 남깁니다. " +
        "캐릭터(직업각인)마다 따로 저장합니다. 기록을 열면 파티원 모두의 DPS · 누적 피해, 내 스킬 사용, 내 공증 · 아덴 피증 · 초각스 피증 구간, 페이즈 경계를 시간 순으로 봅니다. 휠이나 +/−로 확대하고, 끌거나 ←/→로 옮깁니다.";
    readonly CardStack _secRecords = new();
    readonly TimelineView _timeline = new() { Margin = new(0, 8, 0, 4) };
    readonly List<string> _compare = [];
    string? _opened, _timelineShows, _recordChar, _registerNote;
    int _comparePhase;
    bool _keptTab, _hasIcons, _recordsHooked;
    bool? _useIcons;   // 아이콘 / 키 as picked on this fight; null: icons when the fight has any
    double _listScroll;
    FightRecord? _firstPick;   // the list's first 비교 pick: only its raid's rows offer 비교

    /// <summary>Where the fights are saved; null: none (the area says so).</summary>
    public RecordStore? Records { get; set; }

    /// <summary>테스트 모드's records, kept apart (own folder, own count): listed after the game's, under the 테스트 chip.</summary>
    public RecordStore? TestRecords { get; set; }

    (RecordStore? Store, string File) StoreOf(string key) => key.StartsWith(TestKey) ? (TestRecords, key[TestKey.Length..]) : (Records, key);

    /// <summary>My skill icons for a record (its class and 직업각인, as saved by 스킬 등록), drawn on my lane.</summary>
    IReadOnlyDictionary<SlotId, ImageSource> RecordIcons(FightRecord r)
    {
        if (_chars.Classes.FirstOrDefault(c => c.Id == r.MyClass)?.Name is not { } cls || r.MySubclass is not { } sub) return new Dictionary<SlotId, ImageSource>();
        try
        {
            return new IconStore(IconStore.DefaultDir).Load(cls, sub).ToDictionary(kv => kv.Key, kv => (ImageSource)Frozen(
                System.Windows.Media.Imaging.BitmapSource.Create(kv.Value.Width, kv.Value.Height, 96, 96, PixelFormats.Bgra32, null, kv.Value.Bgra, kv.Value.Stride)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] icons: {ex.Message}"); return new Dictionary<SlotId, ImageSource>(); }
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>A fight was saved: the list again (from any thread).</summary>
    public void RefreshRecords() => Dispatcher.BeginInvoke(FillRecords);

    /// <summary>The label of the character being played now (the active class and 직업각인).</summary>
    string ActiveLabel() => RecordCharacter.Label(_chars.Active?.Class, _chars.Active?.Subclass);

    /// <summary>The page was entered from the menu: the list, with the current character's chip picked.</summary>
    void EnterRecords()
    {
        (_recordChar, _opened, _registerNote) = (null, null, null);
        _compare.Clear();
        FillRecords();
    }

    /// <summary>Every saved fight: the game's (newest first), then 테스트 모드's, each under its key.</summary>
    IReadOnlyList<(string File, FightRecord Record)> AllRecords()
    {
        try { return [.. Records?.List() ?? [], .. (TestRecords?.List() ?? []).Select(x => (TestKey + x.File, x.Record))]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] list failed: {ex.Message}"); return []; }
    }

    void FillRecords()
    {
        var all = AllRecords();
        if (_opened is not null && all.All(x => x.File != _opened)) _opened = null;
        _compare.RemoveAll(f => all.All(x => x.File != f));
        if (_opened is null && _compare.Count < 2) FillList(all);
        else FillTimeline(all);
    }

    /// <summary>The list: the settings above it, then the picked character's fights (chips, tabs, rows).</summary>
    void FillList(IReadOnlyList<(string File, FightRecord Record)> all)
    {
        var p = Refill(_secRecords, AreaRecords, RecordsLead);
        p.Children.Add(SliderRow("글자 크기", Settings.MinRecordFont, Settings.MaxRecordFont,
            Math.Clamp(_s.RecordFontPercent, Settings.MinRecordFont, Settings.MaxRecordFont), 10, v => $"{v:0}%", v => _s.RecordFontPercent = (int)v, Changed));
        p.Children.Add(Row("", Check("자동 저장", _s.RecordAutoSave, v => _s.RecordAutoSave = v, () => { Changed(); FillRecords(); },
            "끄면 마지막 전투 하나만 '저장 안 됨'으로 남기고 다음 전투가 덮어씁니다. 남기려면 보관을 누릅니다. 기록 자체를 끄려면 DPS 미터기 → 전투 기록을 끕니다.")));
        p.Children.Add(SliderRow("멈춤 허용", Settings.MinRecordPause, Settings.MaxRecordPause, Math.Clamp(_s.RecordPauseSeconds, Settings.MinRecordPause, Settings.MaxRecordPause), 10,
            v => $"{v:0}초", v => _s.RecordPauseSeconds = (int)v, Changed));
        p.Children.Add(Note("던전에서 기록합니다(가디언 토벌 제외). 던전 이름을 읽지 못하면 '알 수 없음'으로 저장하고, 줄의 편집에서 던전 · 난이도 · 결과 · 클래스를 고칠 수 있습니다. 전분이 새로 시작되면 앞 전투를 저장하고, 새 보스가 같으면(재도전) 실패, 다르면(다음 관문) 클리어로 정합니다. " +
            "다음 기록이 없으면 보스의 마지막 HP가 500억(작은 보스는 최대 HP의 5%) 아래이거나 보스 HP 바가 3초 넘게 사라졌으면 클리어입니다. " +
            "전멸 등으로 타이머가 멈추거나 컷신 · 인벤토리에 가려지면 '멈춤 허용'만큼 기다렸다가 끝냅니다."));
        if (_s.RecordAutoSave)
            p.Children.Add(SliderRow("자동 기록 남기기", 1, 50, Math.Clamp(_s.RecordKeep, 1, 50), 1, v => $"{v:0}개", v => _s.RecordKeep = (int)v, Changed));
        var chips = ChipsOf(all);
        if (chips.Count == 0) { p.Children.Add(Note("저장된 기록이 없습니다.")); return; }
        string picked = (chips.FirstOrDefault(c => c.Character == _recordChar) ?? chips[0]).Character;
        _recordChar = picked;
        p.Children.Add(ChipRow(chips, picked));
        var fights = all.Where(x => ChipOf(x) == picked).ToList();
        p.Children.Add(TabRow(fights));
        var shown = fights.Where(x => x.Record.Kept == _keptTab).ToList();   // the held 'last' fight sorts first on the 자동 tab
        if (shown.Count == 0) p.Children.Add(Note(_keptTab ? "보관한 기록이 없습니다." : "자동 저장된 기록이 없습니다."));
        _firstPick = _compare.Count > 0 ? all.FirstOrDefault(x => x.File == _compare[0]).Record : null;   // read once, for every row's 비교
        foreach (var (file, r) in shown)
        {
            p.Children.Add(RecordRow(file, r));
            if (file == _editing) p.Children.Add(RecordEditor(file, r, all));
        }
    }

    /// <summary>The chip a fight is listed under: its character, or 테스트 for a 테스트 모드 fight.</summary>
    static string ChipOf((string File, FightRecord Record) x) => x.File.StartsWith(TestKey) ? TestChip : RecordCharacter.Of(x.Record);

    /// <summary>One chip per character with fights (the one being played first, the newest after it, 알 수 없음 last), then 테스트.</summary>
    IReadOnlyList<RecordChip> ChipsOf(IReadOnlyList<(string File, FightRecord Record)> all)
    {
        var chips = RecordCharacter.Chips(all.Where(x => !x.File.StartsWith(TestKey)).Select(x => x.Record), ActiveLabel());
        var test = all.Where(x => x.File.StartsWith(TestKey)).Select(x => x.Record).ToList();
        if (test.Count == 0) return chips;
        return [.. chips, new RecordChip(TestChip, test.Count(r => !r.Kept), test.Count(r => r.Kept), test.Max(r => r.StartedAt))];
    }

    UIElement ChipRow(IReadOnlyList<RecordChip> chips, string picked)
    {
        var row = new WrapPanel { Margin = new(0, 4, 0, 6) };
        foreach (var c in chips)
        {
            var chip = Toggle($"{c.Character} {c.Auto + c.Kept}", "recordChip", c.Character == picked);
            chip.Checked += (_, _) => { _recordChar = c.Character; _compare.Clear(); FillRecords(); };
            row.Children.Add(chip);
        }
        return row;
    }

    UIElement TabRow(IReadOnlyList<(string File, FightRecord Record)> fights)
    {
        int auto = fights.Count(x => !x.Record.Kept && !RecordStore.IsHeld(StoreOf(x.File).File));   // the held fight is not one of the N
        int kept = fights.Count(x => x.Record.Kept);
        var row = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var autoTab = Toggle($"자동 저장 ({auto}/{_s.RecordKeep})", "recordTab", !_keptTab);
        autoTab.Checked += (_, _) => SetKeptTab(false);
        var keptTab = Toggle($"보관 ({kept})", "recordTab", _keptTab);
        keptTab.Checked += (_, _) => SetKeptTab(true);
        row.Children.Add(autoTab);
        row.Children.Add(keptTab);
        return row;
    }

    void SetKeptTab(bool kept)
    {
        _keptTab = kept;
        FillRecords();
    }

    UIElement RecordRow(string file, FightRecord r)
    {
        string share = r.MyShare is { } s ? $"{s * 100:0.0}%" : "–";
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, MinWidth = 300, MaxWidth = 520, Cursor = Cursors.Hand,
            Background = Brushes.Transparent, Focusable = true,
        };
        var (store, name) = StoreOf(file);
        string held = (store == TestRecords ? "[테스트]   " : "") + (RecordStore.IsHeld(name) ? "[저장 안 됨]   " : "") + (r.Cleared switch { true => "클리어   ", false => "실패   ", _ => "" });
        text.Children.Add(new TextBlock { Text = $"{held}{r.StartedAt:MM-dd HH:mm}   {r.Gate ?? "알 수 없음"}   {r.Difficulty}   {(int)r.Duration / 60}:{(int)r.Duration % 60:00}   내 몫 {share}" });
        text.Children.Add(new TextBlock { Text = string.Join(", ", r.Members.Select(m => m.Name)), FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
        text.MouseLeftButtonUp += (_, _) => OpenRecord(file);
        text.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) OpenRecord(file); };

        Thickness gap = new(12, 0, 0, 0);
        var keep = new CheckBox { Content = "보관", IsChecked = r.Kept, Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        keep.Click += (_, _) => RecordAction(() => store?.SetKept(name, keep.IsChecked == true));
        // 비교 only within one raid · gate · difficulty: once a fight is picked, only its raid's rows offer it.
        bool comparable = _compare.Count == 0 || _compare.Contains(file) || _firstPick is { } first && Timeline.SameRaid(first, r);
        var compare = new CheckBox
        {
            Content = "비교", IsChecked = _compare.Contains(file), Margin = gap, VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = comparable, Opacity = comparable ? 1 : 0.4,
        };
        compare.Click += (_, _) =>
        {
            if (!_compare.Remove(file)) _compare.Add(file);   // the second pick opens the first with it compared
            FillRecords();
        };
        var edit = new Button { Content = _editing == file ? "편집 닫기" : "편집", Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        edit.Click += (_, _) => { _editing = _editing == file ? null : file; FillRecords(); };
        var delete = new Button { Content = "삭제", Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "이 기록을 지울까요? 되돌릴 수 없습니다.", AreaRecords, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                RecordAction(() => store?.Delete(name));
        };
        var row = new WrapPanel { Margin = new(0, 4, 0, 4) };
        foreach (var c in new UIElement[] { text, keep, compare, edit, delete }) row.Children.Add(c);
        return row;
    }

    /// <summary>Demo (--record-view): opens a fight (one with 암수 when there is), or compares the two newest.</summary>
    public void DemoOpenRecord(bool compare)
    {
        var all = AllRecords();
        if (all.Count == 0) return;
        if (!compare || all.Count < 2) { OpenRecord((all.FirstOrDefault(x => x.Record.AmsuOn) is { File: { } f } ? f : all[0].File)); return; }   // one with 암수 when there is
        _compare.Clear();
        _compare.AddRange(all.Take(2).Select(x => x.File));
        FillRecords();
    }

    /// <summary>Opens a fight: the page becomes its timeline. The list's scroll is kept for ← 목록.</summary>
    void OpenRecord(string file)
    {
        if (_opened is null) _listScroll = Body.VerticalOffset;   // from the list, not from the timeline's side list
        (_opened, _registerNote, _useIcons) = (file, null, null);
        _compare.Clear();
        FillRecords();
        Body.ScrollToTop();
    }

    /// <summary>← 목록: the list again, as it was (same chip, tab and scroll).</summary>
    void CloseRecord()
    {
        (_opened, _registerNote) = (null, null);
        _compare.Clear();
        FillRecords();
        Dispatcher.BeginInvoke(() => Body.ScrollToVerticalOffset(_listScroll), DispatcherPriority.Loaded);
    }

    void RecordAction(Action act)
    {
        try { act(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] {ex.Message}"); }
        FillRecords();
    }
}
