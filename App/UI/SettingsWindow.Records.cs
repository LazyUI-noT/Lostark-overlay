using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using GaugeOverlay.App.Services;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록 (spec § D, records-timeline.design.md): the saved fights of one character (a chip each), 자동 저장 and 보관
/// apart, with 보관, 비교 and 삭제 per row. Opening a row, or two picked for 비교, swaps the page to its timeline
/// (SettingsWindow.RecordView.cs).</summary>
public partial class SettingsWindow
{
    const string AreaRecords = "타임라인";
    const string TestChip = RecordCharacter.TestClass;   // the 테스트 모드 fights' chip, "[Test]" (user, 2026-11-05): no character is named that
    const string TestKey = "test/";   // a test record's key in the list (_opened, _compare): its own file names may repeat the game's
    const string RecordsLead = "DPS 미터기가 따라간 전투를 저장합니다. 캐릭터마다 최근 기록을 정한 개수까지 남기고 오래된 것부터 지웁니다. 보관한 기록도 최근 개수에 들고, 밀려나도 보관 탭에 남습니다. 밀려난 기록과 삭제한 기록은 휴지통으로 가고, 휴지통은 매주 정한 시각에 비워집니다. " +
        "캐릭터마다 따로 저장합니다(ESC 게임 메뉴에서 이름을 읽기 전에는 '캐릭터 불명'). 기록을 열면 파티원 모두의 DPS · 누적 피해, 내 스킬 사용, 내 공증 · 아덴 피증 · 초각스 피증 구간, 페이즈 경계를 시간 순으로 볼 수 있습니다. 휠이나 +/−로 확대하고, 끌거나 ←/→로 옮깁니다." +
        "\n\n레이드·던전에서 기록합니다(가디언 토벌 제외). 콘텐츠 이름을 못 읽으면 '알 수 없음'으로 저장되고, 줄의 편집에서 콘텐츠 · 난이도 · 결과 · 클래스를 고칠 수 있습니다." +
        "\n\n전분이 새로 시작되면 앞 전투를 저장합니다. 새 보스가 같으면(재도전) 실패, 다르면(다음 관문) 클리어입니다." +
        "\n\n다음 기록이 없으면, 보스의 마지막 HP가 500억(작은 보스는 최대 HP의 5%) 아래이거나 보스 체력바가 3초 넘게 사라졌을 때 클리어입니다.";
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

    (RecordStore? Store, string File) StoreOf(string key)
    {
        if (key.StartsWith(BinKey))   // a 휴지통 fight: the bin of the store its key names
        {
            var (home, file) = StoreOf(key[BinKey.Length..]);
            return (home?.Bin, file);
        }
        return key.StartsWith(TestKey) ? (TestRecords, key[TestKey.Length..]) : key.StartsWith(ImportKey) ? (ImportedRecords, key[ImportKey.Length..]) : (Records, key);
    }

    static bool IsImported(string key) => key.StartsWith(ImportKey);

    /// <summary>What my lane draws per slot (skill-icons.plan.md, Core SkillFaces): this PC's saved icon (스킬 등록, by the record's
    /// class and 직업각인) when it shows the fight's skill, else that skill's icon fetched from Smilegate's CDN (requested here when
    /// missing; the face swaps when it lands), else a name chip; slots the fight doesn't know keep the saved icon, else the key.</summary>
    Dictionary<SlotId, TimelineView.SlotFace> RecordIcons(FightRecord r)
    {
        var saved = new Dictionary<SlotId, Core.Domain.Image>();
        string? cls = _chars.Classes.FirstOrDefault(c => c.Id == r.MyClass)?.Name;
        if (cls is not null && r.MySubclass is { } sub)
            try { saved = new IconStore(IconStore.DefaultDir).Load(cls, sub); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] icons: {ex.Message}"); }
        var faces = new Dictionary<SlotId, TimelineView.SlotFace>();
        foreach (var slot in SlotInfo.All)
        {
            var img = saved.GetValueOrDefault(slot);
            var skill = r.Skills?.GetValueOrDefault(slot);
            string? savedName = img is not null && skill is not null && cls is not null && FightSkills.KindOf(slot) is { } kind
                ? SkillMatch.Identify(IconPrint.Framings(img), SkillLibrary.Shipped.Pool(cls, kind)).Skill?.Name : null;
            string? local = skill is null ? null : SkillIconDownloader.Local(skill.Icon);
            switch (SkillFaces.Of(r, slot, img is not null, savedName, local is not null))
            {
                case SkillFaceKind.Saved: faces[slot] = new(Bitmap(img!), skill); break;
                case SkillFaceKind.Library: faces[slot] = new(Png(local!), skill); break;
                case SkillFaceKind.Chip:
                    faces[slot] = new(null, skill);
                    SkillIconDownloader.Request(skill!.Icon);
                    break;
            }
        }
        if (!_iconsHooked)
        {
            _iconsHooked = true;
            SkillIconDownloader.Landed += (icon, path) => Dispatcher.BeginInvoke(() => IconLanded(icon, path));
        }
        _faces = faces;
        return faces;
    }

    Dictionary<SlotId, TimelineView.SlotFace>? _faces;   // the opened timeline's faces
    bool _iconsHooked;

    /// <summary>A downloaded icon: the chips of the opened timeline that show its skill become the icon.</summary>
    void IconLanded(string icon, string path)
    {
        if (_faces is not { } faces) return;
        var chips = faces.Where(f => f.Value.Image is null && f.Value.Skill?.Icon == icon).Select(f => f.Key).ToList();
        if (chips.Count == 0 || Png(path) is not { } png) return;
        foreach (var slot in chips) faces[slot] = faces[slot] with { Image = png };
        _timeline.InvalidateVisual();
    }

    static ImageSource Bitmap(Core.Domain.Image img) =>
        Frozen(System.Windows.Media.Imaging.BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Bgra, img.Stride));

    /// <summary>A downloaded icon; null when its file can't be read (the chip stays).</summary>
    static ImageSource? Png(string path)
    {
        try
        {
            using var file = File.OpenRead(path);   // not a Uri: a '#' or '%' in the folder would break one
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;   // an icon's size, whatever the file says
            bmp.StreamSource = file;
            bmp.EndInit();
            return Frozen(bmp);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            Trace.WriteLine($"[skillicons] {path}: {ex.Message}");
            return null;
        }
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>A fight was saved: the list again (from any thread).</summary>
    public void RefreshRecords() => Dispatcher.BeginInvoke(FillRecords);

    /// <summary>The label of the character being played now (the active class and 직업각인).</summary>
    string ActiveLabel() => RecordCharacter.Label(_chars.Active?.Class, _chars.Active?.Subclass);

    /// <summary>The page was entered from the menu: the list, with the current character's chip picked.</summary>
    void EnterRecords()
    {
        (_recordChar, _opened, _registerNote, _editing) = (null, null, null, null);   // back from another page: an unapplied edit is 취소
        _compare.Clear();
        FillRecords();
    }

    /// <summary>Every saved fight: the game's (newest first), then 테스트 모드's, each under its key.</summary>
    IReadOnlyList<(string File, FightRecord Record)> AllRecords()
    {
        try
        {
            return [.. Records?.List() ?? [], .. (TestRecords?.List() ?? []).Select(x => (TestKey + x.File, x.Record)),
                .. (ImportedRecords?.List() ?? []).Select(x => (ImportKey + x.File, x.Record))];
        }
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
        p.Children.Add(SliderRow("멈춤 허용", Settings.MinRecordPause, Settings.MaxRecordPause, Math.Clamp(_s.RecordPauseSeconds, Settings.MinRecordPause, Settings.MaxRecordPause), 10,
            v => $"{v:0}초", v => _s.RecordPauseSeconds = (int)v, Changed,
            "전멸 등으로 타이머가 멈추거나 컷신 · 인벤토리에 가려지면 이 시간만큼 기다렸다가 전투를 끝냅니다."));
        p.Children.Add(KeepSlider());
        var chips = ChipsOf(all);
        var bin = BinRecords();
        var names = chips.Select(c => c.Character).Concat(bin.Count > 0 ? [BinChip] : []).ToList();
        if (names.Count == 0) { p.Children.Add(ShareRow([])); p.Children.Add(Note("저장된 기록이 없습니다.")); return; }
        string picked = _recordChar is { } was && names.Contains(was) ? was : names[0];
        _recordChar = picked;
        p.Children.Add(ChipRow(chips, picked, bin.Count));
        if (picked == BinChip) { FillBin(p, bin); return; }
        var fights = all.Where(x => ChipOf(x) == picked).ToList();
        bool imported = picked == ImportChip;   // all kept until deleted: no 자동 / 보관 tabs
        if (!imported) p.Children.Add(TabRow(fights));
        var listed = imported ? fights : _keptTab ? KeptTabOf(fights) : RecentOf(fights);
        bool prunes = !imported && picked != TestChip;   // 테스트 mixes characters
        _goesNext = prunes ? [.. RecordStore.GoesNext(fights, Held, x => x.Record.Kept, _s.RecordKeep).Select(x => x.File)] : [];
        var raids = Homework?.Raids ?? [];
        var groups = _s.RecordSortByRaid
            ? RaidOrder.Group(listed, x => x.Record.Gate, x => x.Record.Difficulty, RaidGates, raids, _s.RecordRaidOrder)
            : [(null, listed)];
        var shown = groups.SelectMany(g => g.Fights).ToList();
        p.Children.Add(ShareRow(shown));
        if (shown.Count == 0) p.Children.Add(Note(_keptTab ? "보관한 기록이 없습니다." : "최근 기록이 없습니다."));
        _firstPick = _compare.Count > 0 ? all.FirstOrDefault(x => x.File == _compare[0]).Record : null;   // read once, for every row's 비교
        foreach (var (raid, inRaid) in groups)
        {
            if (_s.RecordSortByRaid) p.Children.Add(RaidHeader(raid, raids));
            foreach (var (file, r) in inRaid)
            {
                (UIElement Panel, UIElement[] Buttons)? editor = file == _editing ? RecordEditor(file, r, all) : null;
                p.Children.Add(RecordRow(file, r, editor?.Buttons));
                if (editor is { } e) p.Children.Add(e.Panel);
            }
        }
    }

    /// <summary>레이드순's heading over a raid's fights: its name, and ▲ ▼ moving the raid, its gates and difficulties with it
    /// (user, 2026-11-11). Null raid: the fights of no known raid, last, not movable.</summary>
    UIElement RaidHeader(HomeworkRaid? raid, IReadOnlyList<HomeworkRaid> raids)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 2) };
        row.Children.Add(new TextBlock { Text = raid?.Name ?? "그 밖의 콘텐츠", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (raid is null) return row;
        foreach (var (glyph, step, tip) in new[] { ("▲", -1, "이 레이드를 위로"), ("▼", 1, "이 레이드를 아래로") })
        {
            var move = new Button { Content = glyph, Margin = new(8, 0, 0, 0), Padding = new(6, 0, 6, 0), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(move, $"{raid.Name} {tip}");
            move.Click += (_, _) => { _s.RecordRaidOrder = [.. RaidOrder.Move(raids, _s.RecordRaidOrder, raid.Id, step)]; Changed(); FillRecords(); };
            row.Children.Add(move);
        }
        return row;
    }

    /// <summary>The chip a fight is listed under: its character, 테스트 for a 테스트 모드 fight, [가져옴] for an imported one.</summary>
    static string ChipOf((string File, FightRecord Record) x) =>
        x.File.StartsWith(TestKey) ? TestChip : IsImported(x.File) ? ImportChip : RecordCharacter.Of(x.Record);

    /// <summary>One chip per character with fights (the one being played first, the newest after it, 캐릭터 불명 last), then 테스트
    /// and [가져옴].</summary>
    IReadOnlyList<RecordChip> ChipsOf(IReadOnlyList<(string File, FightRecord Record)> all)
    {
        var chips = RecordCharacter.Chips(all.Where(x => !x.File.StartsWith(TestKey) && !IsImported(x.File)).Select(x => x.Record), Homework?.Current).ToList();
        foreach (var (chip, key) in new[] { (TestChip, TestKey), (ImportChip, ImportKey) })
            if (all.Where(x => x.File.StartsWith(key)).Select(x => x.Record).ToList() is { Count: > 0 } some)
                chips.Add(new RecordChip(chip, some.Count(r => !r.Kept), some.Count(r => r.Kept), some.Max(r => r.StartedAt)));
        return chips;
    }

    UIElement ChipRow(IReadOnlyList<RecordChip> chips, string picked, int binCount)
    {
        var row = new WrapPanel { Margin = new(0, 4, 0, 6) };
        void Add(string key, string text)
        {
            var chip = Toggle(text, "recordChip", key == picked);
            chip.Checked += (_, _) => { (_recordChar, _editing) = (key, null); _compare.Clear(); FillRecords(); };
            row.Children.Add(chip);
        }
        foreach (var c in chips) Add(c.Character, $"{c.Character} {c.Auto + c.Kept}");
        if (binCount > 0)
        {
            Add(BinChip, $"휴지통 {binCount}");
            ((FrameworkElement)row.Children[^1]).Margin = new(12, 0, 6, 6);   // set apart: not a character
        }
        return row;
    }

    /// <summary>최근 기록 남기기. Lowered (on release: a drag passes many numbers), every character's unkept fights past the new
    /// number go to the 휴지통 at once, after a warning; 취소 puts the number back (user, 2026-11-10).</summary>
    UIElement KeepSlider()
    {
        int before = Math.Clamp(_s.RecordKeep, RecordStore.MinKeep, RecordStore.MaxKeep), pending = before;
        // The number stays here until release: written live, a fight saved mid-drag would prune at it unwarned.
        var row = SliderRow("최근 기록 남기기", RecordStore.MinKeep, RecordStore.MaxKeep, before, 1, v => $"{v:0}개", v => pending = (int)v, () => { });
        void Apply(int keep) { before = keep; _s.RecordKeep = keep; Changed(); }
        void Commit()
        {
            int now = Math.Clamp(pending, RecordStore.MinKeep, RecordStore.MaxKeep);
            if (now == before) return;
            if (now > before) { Apply(now); return; }   // raised: nothing moves
            var stores = new[] { Records, TestRecords }.OfType<RecordStore>().ToList();
            int moving = stores.Sum(s => s.OverflowCount(now));
            if (moving > 0 && !Warn(WarningKind.PushToBin, $"최근 기록을 {now}개로 줄이면 보관하지 않은 기록 {moving}개가 휴지통으로 갑니다. 휴지통에서 되돌리기를 누르면 돌아옵니다."))
            {
                FillRecords();   // rebuilds the slider at the kept number
                return;
            }
            Apply(now);
            RecordAction(() => { foreach (var s in stores) s.PruneAll(now); });
        }
        row.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => Commit()), handledEventsToo: true);
        row.AddHandler(KeyUpEvent, new KeyEventHandler((_, _) => Commit()), handledEventsToo: true);
        return row;
    }

    UIElement TabRow(IReadOnlyList<(string File, FightRecord Record)> fights)
    {
        int recent = RecentOf(fights).Count(x => !Held(x));   // the held fight is not one of the N
        int kept = KeptTabOf(fights).Count;
        var row = new WrapPanel { Margin = new(0, 0, 0, 8) };
        var autoTab = Toggle($"최근 ({recent}/{Math.Clamp(_s.RecordKeep, RecordStore.MinKeep, RecordStore.MaxKeep)})", "recordTab", !_keptTab);
        autoTab.Checked += (_, _) => SetKeptTab(false);
        var keptTab = Toggle($"보관 ({kept})", "recordTab", _keptTab);
        keptTab.Checked += (_, _) => SetKeptTab(true);
        row.Children.Add(autoTab);
        row.Children.Add(keptTab);
        row.Children.Add(Muted(new TextBlock { Text = "정렬", Margin = new(18, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center }));
        foreach (var (byRaid, text) in new[] { (false, "날짜순"), (true, "레이드순") })
        {
            var sort = Toggle(text, "recordSort", _s.RecordSortByRaid == byRaid);
            sort.ToolTip = byRaid ? "레이드별로 모읍니다. 레이드 안은 1관문부터, 관문 안은 높은 난이도부터, 최근 것부터. ▲ ▼로 레이드 순서를 바꿉니다." : "최근 것부터 보여 줍니다.";
            sort.Checked += (_, _) => { _s.RecordSortByRaid = byRaid; Changed(); FillRecords(); };
            row.Children.Add(sort);
        }
        return row;
    }

    bool Held((string File, FightRecord Record) x) => RecordStore.IsHeld(StoreOf(x.File).File);

    /// <summary>The 최근 tab: the held fight, then the newest N saved ones, 보관 or not (RecordStore.Recent).</summary>
    List<(string File, FightRecord Record)> RecentOf(IReadOnlyList<(string File, FightRecord Record)> fights) =>
        [.. RecordStore.Recent(fights, Held, _s.RecordKeep)];

    /// <summary>The 보관 tab: the 보관 fights (an unkept one past the newest N is in the 휴지통 instead).</summary>
    static List<(string File, FightRecord Record)> KeptTabOf(IReadOnlyList<(string File, FightRecord Record)> fights) =>
        [.. fights.Where(x => x.Record.Kept)];

    /// <summary>The listed fights the next save deletes, read once per fill: each row shows a dark orange ! (user, 2026-11-10).</summary>
    HashSet<string> _goesNext = [];

    static readonly SolidColorBrush DeleteRed = new(Color.FromRgb(0xC4, 0x2B, 0x1C));
    static readonly SolidColorBrush GoesNextOrange = new(Color.FromRgb(0xD9, 0x6A, 0x00));

    void SetKeptTab(bool kept)
    {
        (_keptTab, _editing) = (kept, null);   // leaving an open editor unapplied is 취소 (user, 2026-11-09)
        FillRecords();
    }

    /// <summary>One fight in the list; <paramref name="editing"/>: its editor is open, so 적용 · 취소 stand where 편집 was.</summary>
    UIElement RecordRow(string file, FightRecord r, UIElement[]? editing)
    {
        var text = RecordText(file, r);
        text.Cursor = Cursors.Hand;
        text.Focusable = true;
        text.MouseLeftButtonUp += (_, _) => OpenRecord(file);
        text.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) OpenRecord(file); };
        var (store, name) = StoreOf(file);
        bool imported = IsImported(file);

        Thickness gap = new(12, 0, 0, 0);
        var keep = KeepButton(file, r.Kept, gap);
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
        var edit = new Button { Content = "편집", Margin = gap, VerticalAlignment = VerticalAlignment.Center };
        edit.Click += (_, _) => { _editing = file; FillRecords(); };   // another row's open editor closes unapplied: 취소
        var delete = DeleteButton(gap, "휴지통으로 옮깁니다. 휴지통에서 되돌리기를 누르면 돌아옵니다.");
        delete.Click += (_, _) => RecordAction(() => store?.Delete(name));   // to the 휴지통: no question (user, 2026-11-10)
        var row = new WrapPanel { Margin = new(0, 4, 0, 4) };
        // An imported fight is kept until deleted and isn't mine to edit: 비교 and 삭제 only.
        UIElement[] editButtons = editing ?? [edit];
        foreach (var c in imported ? new UIElement[] { text, compare, delete } : [text, keep, compare, .. editButtons, delete]) row.Children.Add(c);
        if (!imported && MeterCompareButton(file, r, gap) is { } meter) row.Children.Insert(row.Children.IndexOf(editButtons[0]), meter);
        return NameButtons(row, r);
    }

    static Button DeleteButton(Thickness margin, string tip, string label = "삭제")
    {
        var delete = new Button { Content = label, Margin = margin, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
        Fill(delete, DeleteRed.Color, Brushes.White);   // user, 2026-11-09
        return delete;
    }

    /// <summary>Each button of a fight's row read with the fight (screen readers): "10-30 21:40 3막 · 3관문 삭제", not a bare 삭제
    /// repeated down the list.</summary>
    static WrapPanel NameButtons(WrapPanel row, FightRecord r)
    {
        foreach (var c in row.Children.OfType<System.Windows.Controls.Primitives.ButtonBase>())
            if (c.Content is string label)
                System.Windows.Automation.AutomationProperties.SetName(c, $"{r.StartedAt:MM-dd HH:mm} {r.Gate ?? "알 수 없음"} {label}");
        return row;
    }

    /// <summary>A fight's two lines (when, raid, length; whose and my numbers), the same in the list and the 휴지통.</summary>
    StackPanel RecordText(string file, FightRecord r, bool inBin = false)
    {
        string share = (r.MyShare is { } s ? $"{s * 100:0.0}%" : "–") + (Timeline.MyDps(r) is { } dps ? $" · DPS {AmountText.Dps(dps)}" : "")
            + (ClassProfiles.WindowsOf(_s, RecordCharacter.ClassOf(r)).TimelineSkill is { } pick ? $" · {SlotKeyName(pick)} {RateOf(r, pick)}" : "");   // the timeline's pick (user, 2026-11-09)
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Width = 520,   // one width: every row's 보관 · 비교 · buttons line up (user, 2026-11-10)
            Background = Brushes.Transparent,
        };
        var (store, name) = StoreOf(file);
        bool imported = IsImported(file);
        string held = (store == TestRecords ? "[테스트]   " : "") + (imported ? $"{ImportChip} [{RecordCharacter.ClassOf(r)}]   " : "")
            + (RecordStore.IsHeld(name) ? "[저장 안 됨]   " : "") + (r.Cleared switch { true => "클리어   ", false => "실패   ", _ => "" });
        // [logo] raid [difficulty] (user, 2026-11-10)
        var logo = RaidBadgeView.Of(r.Gate, null);
        logo.Margin = new(8, 0, 0, 0);
        var difficulty = RaidBadgeView.Of(null, r.Difficulty);
        difficulty.Margin = new(6, 0, 8, 0);
        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = $"{held}{r.StartedAt:MM-dd HH:mm}", VerticalAlignment = VerticalAlignment.Center },
                logo,
                new TextBlock { Text = r.Gate ?? "알 수 없음", VerticalAlignment = VerticalAlignment.Center },
                difficulty,
                new TextBlock { Text = $"{(int)r.Duration / 60}:{(int)r.Duration % 60:00}", VerticalAlignment = VerticalAlignment.Center },
            },
        };
        if (!inBin && _goesNext.Contains(file))   // warn before the next save deletes it, so none goes by accident
            top.Children.Insert(0, new TextBlock
            {
                Text = "!", FontWeight = FontWeights.Bold, Foreground = GoesNextOrange, Margin = new(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "다음 전투가 저장되면 휴지통으로 갑니다. 남기려면 보관을 누르세요.",
            });
        text.Children.Add(top);
        // second line: my class emblem and character, then my numbers (user, 2026-11-10: not the party's names)
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 2, 0, 0) };
        if (Emblems.For(r.MyClassName) is { } emblem)
            who.Children.Add(new System.Windows.Controls.Image { Source = emblem, Width = 16, Height = 16, Margin = new(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = RecordCharacter.ClassOf(r) });
        who.Children.Add(new TextBlock
        {
            Text = $"{RecordCharacter.Of(r)}   내 몫 {share}", FontSize = 11, Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(who);
        return text;
    }

    /// <summary>「실시간 비교」 (user, 2026-11-11; was 미터기 비교, apart from the timeline's 비교): ticked on the one fight per
    /// character × raid · gate · difficulty the DPS 미터기 draws as its dashed line (picked, else the newest). Ticking another picks
    /// it; unticking the picked one goes back to the newest (lostark-api.plan.md phase 6). None for a fight without character
    /// or gate, an imported one, or the unsaved last fight (its file is the next fight's too).</summary>
    CheckBox? MeterCompareButton(string file, FightRecord r, Thickness margin)
    {
        var (store, name) = StoreOf(file);
        if (store is null || !MeterCompare.CanChoose(r) || IsImported(file) || RecordStore.IsHeld(name)) return null;
        var box = new CheckBox { Content = "실시간 비교", Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        bool Chosen() => MeterCompare.IsChosen(_s.MeterCompare, r, name);
        void Show()   // read at each click too: the timeline bar's box isn't rebuilt with the list
        {
            bool chosen = Chosen(), inUse = MeterCompare.InUse(store.List(), _s.MeterCompare, r) == name;
            box.IsChecked = inUse;
            box.ToolTip = chosen ? "DPS 미터기가 이 전투와 비교합니다(고름). 끄면 이 레이드 · 관문 · 난이도의 가장 최근 전투로 돌아갑니다."
                : inUse ? "DPS 미터기가 이 전투와 비교합니다(자동: 가장 최근). 다른 전투를 켜면 그 전투로 바뀝니다."
                : "DPS 미터기 선그래프에 이 전투의 내 DPS를 점선으로 겹칩니다. 레이드 · 관문 · 난이도마다 하나입니다.";
        }
        Show();
        box.Click += (_, _) =>
        {
            _s.MeterCompare = Chosen() ? MeterCompare.Auto(_s.MeterCompare, r) : MeterCompare.Choose(_s.MeterCompare, r, name);
            Changed();
            FillRecords();
            Show();
        };
        return box;
    }

    /// <summary>Demo (--record-view): opens a fight (one with 암수 when there is), or compares the two newest.</summary>
    public void DemoOpenRecord(bool compare, bool run = false)
    {
        var all = AllRecords();
        if (all.Count == 0) return;
        if (run && all.FirstOrDefault(x => FightRegions.Chain(x.Record, all.Select(a => a.Record)).Count > 1) is { File: { } joined }) { OpenRecord(joined); return; }
        if (!compare || all.Count < 2) { OpenRecord((all.FirstOrDefault(x => x.Record.AmsuOn) is { File: { } f } ? f : all[0].File)); return; }   // one with 암수 when there is
        _compare.Clear();
        _compare.AddRange(all.Take(2).Select(x => x.File));
        FillRecords();
    }

    /// <summary>Opens a fight: the page becomes its timeline. The list's scroll is kept for ← 목록.</summary>
    void OpenRecord(string file)
    {
        if (_opened is null) _listScroll = Body.VerticalOffset;   // from the list, not from the timeline's side list
        (_opened, _registerNote, _useIcons, _editing) = (file, null, null, null);
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
