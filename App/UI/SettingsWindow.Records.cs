using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록 (spec § D): the saved fights (date, raid · gate, length, party, my share) with 열기, 보관, 비교 and 삭제,
/// and the timeline of the one opened, or of the two picked for 비교 on one time axis.</summary>
public partial class SettingsWindow
{
    const string AreaRecords = "전투 기록";
    readonly CardStack _secRecords = new();
    readonly TimelineView _timeline = new() { Margin = new(0, 8, 0, 4) };
    readonly List<string> _compare = [];
    string? _opened, _timelineShows;
    int _comparePhase;

    /// <summary>Where the fights are saved; null: none (the area says so).</summary>
    public RecordStore? Records { get; set; }

    /// <summary>테스트 모드's records, kept apart (own folder, own count): listed after the game's, marked [테스트].</summary>
    public RecordStore? TestRecords { get; set; }

    const string TestKey = "test/";   // a test record's key in the list (_opened, _compare): its own file names may repeat the game's

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

    void FillRecords()
    {
        var p = Refill(_secRecords, AreaRecords, "DPS 미터기가 따라간 전투를 저장합니다. 자동 기록은 정한 개수만 남기고(가장 오래된 것부터 지움), 보관한 기록은 개수에 들지 않고 지울 때까지 남깁니다. " +
            "열면 파티원 모두의 DPS · 누적 피해, 내 스킬 사용, 내 공증 · 아덴 피증 · 초각스 피증 구간, 페이즈 경계를 시간 순으로 봅니다. 휠이나 +/−로 확대하고, 끌거나 ←/→로 옮깁니다.");
        p.Children.Add(Row("", Check("자동 저장", _s.RecordAutoSave, v => _s.RecordAutoSave = v, () => { Changed(); FillRecords(); },
            "끄면 마지막 전투 하나만 '저장 안 됨'으로 남기고 다음 전투가 덮어씁니다. 남기려면 보관을 누릅니다. 기록 자체를 끄려면 DPS 미터기 → 전투 기록을 끕니다.")));
        p.Children.Add(SliderRow("멈춤 허용", Settings.MinRecordPause, Settings.MaxRecordPause, Math.Clamp(_s.RecordPauseSeconds, Settings.MinRecordPause, Settings.MaxRecordPause), 10,
            v => $"{v:0}초", v => _s.RecordPauseSeconds = (int)v, Changed));
        p.Children.Add(Note("레이드에서만 기록합니다(가디언 토벌 제외). 보스 HP가 10,000 아래로 내려간 채 전분 타이머가 멈추면 클리어로 바로 끝내고, " +
            "전멸 등으로 타이머가 멈추거나 컷신 · 인벤토리에 가려지면 기다렸다가 '멈춤 허용'이 지나면 실패로 끝냅니다. 재시작으로 전분이 새로 시작해도 앞 전투를 저장합니다."));
        if (_s.RecordAutoSave)
            p.Children.Add(SliderRow("자동 기록 남기기", 1, 50, Math.Clamp(_s.RecordKeep, 1, 50), 1, v => $"{v:0}개", v => _s.RecordKeep = (int)v, Changed));
        IReadOnlyList<(string File, FightRecord Record)> list = [];
        try { list = [.. Records?.List() ?? [], .. (TestRecords?.List() ?? []).Select(x => (TestKey + x.File, x.Record))]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] list failed: {ex.Message}"); }
        if (_opened is not null && list.All(x => x.File != _opened)) _opened = null;
        _compare.RemoveAll(f => list.All(x => x.File != f));
        if (list.Count == 0)
        {
            p.Children.Add(Note("저장된 기록이 없습니다."));
            return;
        }
        foreach (var (file, r) in list) p.Children.Add(RecordRow(file, r));

        var picked = _compare.Count == 2 ? list.Where(x => _compare.Contains(x.File)).ToList() : list.Where(x => x.File == _opened).ToList();
        if (picked.Count == 0) return;
        if (picked.Count == 2)
        {
            int phases = picked.Max(x => x.Record.Phases.Count);
            _comparePhase = Math.Min(_comparePhase, phases);   // records swapped for ones with fewer phases
            var align = Combo();
            align.ItemsSource = Enumerable.Range(0, phases + 1).Select(i => new ComboBoxItem { Content = i == 0 ? "전투 시작" : $"페이즈 {i + 1} 시작", Tag = i }).ToList();
            align.SelectedIndex = _comparePhase;
            align.SelectionChanged += (_, _) => { if (align.SelectedItem is ComboBoxItem { Tag: int i }) { _comparePhase = i; FillRecords(); } };
            p.Children.Add(Row("비교 맞추기", align));
        }
        string shows = string.Join("|", picked.Select(x => x.File)) + "@" + _comparePhase;
        if (shows != _timelineShows)   // the same records keep their zoom when the list refills
        {
            _timelineShows = shows;
            _timeline.Show([.. picked.Select(x => (x.Record, Timeline.Offset(x.Record, picked.Count == 2 ? _comparePhase : 0),
                RecordIcons(x.Record)))]);
        }
        p.Children.Add(_timeline);
    }

    UIElement RecordRow(string file, FightRecord r)
    {
        string share = r.MyShare is { } s ? $"{s * 100:0.0}%" : "–";
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 300, MaxWidth = 520 };
        var (store, name) = StoreOf(file);
        string held = (store == TestRecords ? "[테스트]   " : "") + (RecordStore.IsHeld(name) ? "[저장 안 됨]   " : "") + (r.Cleared switch { true => "클리어   ", false => "실패   ", _ => "" });
        text.Children.Add(new TextBlock { Text = $"{held}{r.StartedAt:MM-dd HH:mm}   {r.Gate ?? "알 수 없음"}   {r.Difficulty}   {(int)r.Duration / 60}:{(int)r.Duration % 60:00}   내 몫 {share}" });
        text.Children.Add(new TextBlock { Text = string.Join(", ", r.Members.Select(m => m.Name)), FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });

        Thickness gap = new(12, 0, 0, 0);
        bool openNow = _opened == file && _compare.Count < 2;
        var open = new Button { Content = openNow ? "닫기" : "열기", Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        open.Click += (_, _) => { _opened = openNow ? null : file; _compare.Clear(); FillRecords(); };
        var keep = new CheckBox { Content = "보관", IsChecked = r.Kept, Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        keep.Click += (_, _) => RecordAction(() => store?.SetKept(name, keep.IsChecked == true));
        var compare = new CheckBox { Content = "비교", IsChecked = _compare.Contains(file), Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        compare.Click += (_, _) =>
        {
            if (!_compare.Remove(file)) { _compare.Add(file); if (_compare.Count > 2) _compare.RemoveAt(0); }
            FillRecords();
        };
        var delete = new Button { Content = "삭제", Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "이 기록을 지울까요? 되돌릴 수 없습니다.", AreaRecords, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                RecordAction(() => store?.Delete(name));
        };
        var row = new WrapPanel { Margin = new(0, 4, 0, 4) };
        foreach (var c in new UIElement[] { text, open, keep, compare, delete }) row.Children.Add(c);
        return row;
    }

    void RecordAction(Action act)
    {
        try { act(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] {ex.Message}"); }
        FillRecords();
    }
}
