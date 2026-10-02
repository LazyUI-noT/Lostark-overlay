using System.IO;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>Which character is active: auto-detected from the class emblem (PLAN §7 item 6), with the emblem learned
/// the first time a character is used. Also resolves that character's class data from Profiles/.</summary>
public sealed class CharacterService(Settings settings, string profilesDir)
{
    const double Match = 0.85;
    readonly IReadOnlyList<ClassInfo> _classes = ProfileLoader.LoadClasses(profilesDir);

    public static readonly string DefaultProfilesDir = Path.Combine(AppContext.BaseDirectory, "Profiles");

    public CharacterSettings? Active =>
        settings.ActiveCharacter is { } k && settings.Characters.TryGetValue(k, out var c) ? c : null;

    /// <summary>직업군 → 클래스 → 직업각인 names, in classes.json order.</summary>
    public IEnumerable<(string Family, IEnumerable<(string Class, IReadOnlyList<string> Subclasses)> Classes)> ClassTree =>
        _classes.GroupBy(c => c.Family).Select(f => (f.Key, f.Select(c => (c.Name, (IReadOnlyList<string>)[.. c.Subclasses.Keys]))));

    /// <summary>Every class, in classes.json order.</summary>
    public IReadOnlyList<ClassInfo> Classes => _classes;

    public ClassInfo? ActiveClass => Active is { } c ? _classes.FirstOrDefault(x => x.Name == c.Class) : null;

    /// <summary>The active 직업각인's 아덴 skill slots (its own, else its class's).</summary>
    public IReadOnlyCollection<SlotId> AdenSlots => ActiveClass?.AdenSlotsOf(Active?.Subclass) ?? [];

    /// <summary>The active 직업각인's role (발키리: 해방자 서포터, 빛의 기사 딜러).</summary>
    public Role Role => ActiveClass?.RoleOf(Active?.Subclass) ?? Role.딜러;

    /// <summary>How the active 직업각인's 아덴 skills show they lack the resource (case 2), or None.</summary>
    public DarkCue AdenDark => ActiveClass?.AdenDark(Active?.Subclass) ?? DarkCue.None;

    /// <summary>The active character's class number (ClassInfo.Id), 0 when none is picked.</summary>
    public int ClassId => ActiveClass?.Id ?? 0;

    /// <summary>The active 직업각인's "adenGauge" rule in classes.json, or null.</summary>
    public GaugeRule? GaugeRule => Active is { } c ? ActiveClass?.Subclasses.GetValueOrDefault(c.Subclass) : null;

    (string?, string?) _gaugeFor;
    GaugeDef? _gauge;

    /// <summary>The active 직업각인's 아덴 게이지 (Profiles/aden), or null: none in classes.json, no definition, or a
    /// broken one (traced). Loaded once per 클래스·직업각인.</summary>
    public GaugeDef? Gauge
    {
        get
        {
            var key = (Active?.Class, Active?.Subclass);
            if (key != _gaugeFor) (_gaugeFor, _gauge) = (key, LoadGauge());
            return _gauge;
        }
    }

    GaugeDef? LoadGauge()
    {
        if (ActiveClass is not { } cls || Active is not { } c || cls.Subclasses.GetValueOrDefault(c.Subclass) is null
            || GaugeLoader.Folder(profilesDir, cls.Name, c.Subclass) is not { } dir) return null;
        try { return GaugeLoader.Parse(File.ReadAllText(Path.Combine(dir, GaugeLoader.JsonFile)), Platform.Png.Load(Path.Combine(dir, GaugeLoader.MaskFile))); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or NotSupportedException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"[gauge] {dir}: {ex.Message}");
            return null;
        }
    }

    bool _picked;

    /// <summary>Raised after <see cref="Pick"/>: the engine captures the emblem to learn it.</summary>
    public event Action? Picked;

    /// <summary>The user picked this character (tray, 설정 캐릭터, first run: all come here): it becomes active, and the next
    /// emblem capture is learned for it instead of switching back to a character whose emblem it resembles. Its 직업각인 is
    /// remembered for the class (Settings.SubclassHistory).</summary>
    public void Pick(string family, string className, string subclass)
    {
        settings.PickCharacter(family, className, subclass, remember: true);
        (_picked, _redetect) = (true, false);
        Picked?.Invoke();
    }

    /// <summary>기본 picked by hand (tray, 설정 캐릭터): no class until the next pick or 스킬바 다시 맞추기.</summary>
    public void PickDefault()
    {
        settings.ActiveCharacter = null;
        (_picked, _redetect) = (false, false);
    }

    string? _confirmed;   // the class the centre piece showed at the last detection

    /// <summary>Handles a freshly captured emblem: after a pick by hand it is learned for the picked character; else (스킬바
    /// 다시 맞추기) the character it confirms becomes active, or 기본 when none (<see cref="CharacterDetect"/>). Returns what
    /// changed, to tell the user, or null.</summary>
    public string? OnEmblem(Image emblem)
    {
        var bgra = Convert.ToBase64String(emblem.ToBytes());
        if (_picked)
        {
            // Picked from the tray because detection got it wrong: this emblem is the picked character's from now on.
            _picked = false;
            if (Active is { } picked) picked.EmblemBgra = bgra;
            return null;
        }
        _redetect = false;   // 스킬바 다시 맞추기 goes by the history
        var refs = _classEmblems ??= LoadClassEmblems();
        var shown = _confirmed = ClassEmblemMatcher.Shown(emblem, refs);
        bool Fits(string cls) => refs.Any(r => r.Class == cls) ? ClassEmblemMatcher.Other(emblem, cls, refs) is null : shown is null;
        var was = settings.ActiveCharacter;
        var now = CharacterDetect.Resolve(settings, _classes, Learned(emblem), Fits, shown, bgra);
        if (now == was) return null;
        return now is null ? "캐릭터를 알아보지 못해 기본으로 돌아갔습니다. 트레이나 설정 → 캐릭터에서 고를 수 있습니다." : $"캐릭터를 {now}(으)로 바꿨습니다.";
    }

    /// <summary>The characters whose learned emblem matches <paramref name="emblem"/>, with the score.</summary>
    List<(string Key, double Score)> Learned(Image emblem)
    {
        var matches = new List<(string Key, double Score)>();
        foreach (var (key, c) in settings.Characters)
        {
            if (c.EmblemBgra is null) continue;
            if (Decode(c.EmblemBgra) is not { } known) { c.EmblemBgra = null; continue; }   // old format or damaged: learned again
            double score = EmblemMatcher.Similarity(emblem, known);
            if (score >= Match) matches.Add((key, score));
        }
        return matches;
    }

    string? _iconsFor;
    Dictionary<string, Image>? _icons;
    string? _missFor;   // the class the 초각스 misses below were counted for
    int _misses;
    // Ready 초각스 crops in a row unlike every icon of the class that mean another class is set: one can be a crop under
    // a window.
    const int MissesForMismatch = 2;

    /// <summary>Another class is set than the one played: the skill bar's centre piece shows another class (<see
    /// cref="ShownClass"/>), or the 초각스 icon was unlike every 직업각인 icon of the active class twice in a row. Cleared by a
    /// capture that agrees or by picking another class.</summary>
    public bool ClassMismatch => (_misses >= MissesForMismatch && _missFor == ActiveClass?.Name) || ShownClass is not null;

    List<ClassEmblem>? _classEmblems;
    string? _shown, _shownFor;   // the class the last emblem capture showed, when not the one set for it

    /// <summary>Another class than the one set, told by the skill bar's centre piece at the last emblem capture (스킬바 다시
    /// 맞추기 or a pick by hand), or null.</summary>
    public string? ShownClass => _shownFor == ActiveClass?.Name ? _shown : null;

    /// <summary>Checks a freshly captured emblem against each gauge class's centre piece (Profiles/aden/*/emblem_empty.png and
    /// emblem_full.png; ClassEmblemMatcher).</summary>
    public void CheckClass(Image emblem)
    {
        _classEmblems ??= LoadClassEmblems();
        (_shownFor, _shown) = (ActiveClass?.Name, ActiveClass is { } cls ? ClassEmblemMatcher.Other(emblem, cls.Name, _classEmblems) : null);
    }

    List<ClassEmblem> LoadClassEmblems()
    {
        var list = new List<ClassEmblem>();
        foreach (var dir in Directory.Exists(Path.Combine(profilesDir, "aden")) ? Directory.GetDirectories(Path.Combine(profilesDir, "aden")) : [])
        {
            string empty = Path.Combine(dir, "emblem_empty.png"), full = Path.Combine(dir, "emblem_full.png");
            if (!File.Exists(empty) || !File.Exists(full)) continue;
            try { list.Add(new ClassEmblem(Path.GetFileName(dir).Split('_')[0], Platform.Png.Load(empty), Platform.Png.Load(full))); }
            catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
                System.Diagnostics.Trace.WriteLine($"[class] {dir}: {ex.Message}");
            }
        }
        return list;
    }

    /// <summary>The active class has an icon for every 직업각인 (Profiles/subclass) or a centre piece (Profiles/aden), so <see cref="ClassMismatch"/> can be told.</summary>
    public bool ClassCheckable => ActiveClass is { } cls
        && (cls.Subclasses.Keys.All(s => File.Exists(Path.Combine(profilesDir, "subclass", cls.Name, s + ".png")))
            || (_classEmblems ??= LoadClassEmblems()).Any(e => e.Class == cls.Name));

    /// <summary>Handles a ready 초각스 slot crop (스킬바 다시 맞추기): sets the 직업각인 its icon shows when that differs from
    /// the active character's; an icon unlike every 직업각인 of a class the centre piece didn't confirm is another class, so
    /// 기본. Returns what changed, to tell the user, or null. Only for a class with an icon for every 직업각인 in
    /// Profiles/subclass/&lt;클래스&gt;/&lt;직업각인&gt;.png.</summary>
    /// <param name="greyedSkills">The skill bar is greyed out (a town): the icon is compared by its shapes alone, and when that
    /// can't tell, its 일반 slots are looked for in each 직업각인's submitted skills (스킬 등록, SkillPool). Null in colour.</param>
    /// <remarks>Skipped for a class with a 직업각인 in the history or whose 직업각인 have the same skills, unless asked for
    /// (<see cref="RedetectSubclass"/>); a 직업각인 read, or confirmed, goes into the history.</remarks>
    public string? OnSubclassSlot(Image slot, IReadOnlyList<Image>? greyedSkills)
    {
        bool asked = _redetect;
        _redetect = false;
        if (Active is not { } c || ActiveClass is not { } cls) return null;
        if (!asked && (cls.SameSkills || cls.Remembered(settings.SubclassHistory) is not null)) return null;
        bool greyed = greyedSkills is not null;
        // Kept only once loaded, so icons added (or a file unlocked) while running are picked up at the next capture.
        if (_iconsFor != cls.Name || _icons is null) (_iconsFor, _icons) = (cls.Name, LoadIcons(cls));
        // ponytail: Pick runs here on the UI thread (about 25 ms, once per 스킬바 다시 맞추기); Task.Run if it shows.
        string? sub = null;
        if (_icons is not null)
        {
            (sub, bool unlike) = SubclassMatcher.Match(slot, _icons, greyed);
            (_missFor, _misses) = (cls.Name, unlike ? (_missFor == cls.Name ? _misses + 1 : 1) : 0);
            // Asked for (직업각인 다시 감지) the class stands: an unlike icon is one it can't tell.
            if (unlike && !asked && _confirmed != cls.Name)
            {
                settings.ActiveCharacter = null;
                return "초각스 아이콘이 고른 클래스와 달라 기본으로 돌아갔습니다. 트레이나 설정 → 캐릭터에서 고를 수 있습니다.";
            }
        }
        if (sub is null && greyedSkills is not null) sub = SkillPool.Pick(greyedSkills, _skills.Load(cls.Name, cls.Subclasses.Keys));
        if (sub is null)
            return !asked ? null : _icons is null && !greyed ? $"{cls.Name}의 초각스 아이콘이 없어 직업각인을 읽을 수 없습니다. {c.Subclass}(으)로 둡니다."
                : $"직업각인을 알아보지 못해 {c.Subclass}(으)로 둡니다.";
        if (sub != c.Subclass) return Detected(sub);
        settings.SubclassHistory[cls.Name] = sub;
        return asked ? $"직업각인이 {sub}(으)로 맞습니다." : null;
    }

    bool _redetect;

    /// <summary>직업각인 다시 감지: the next ready 초각스 is read past the history. Returns why it can't be (no class, or a class whose
    /// 직업각인 have the same skills), or null when the engine should capture it.</summary>
    public string? RedetectSubclass()
    {
        if (ActiveClass is not { } cls) return NoSubclass;
        if (cls.SameSkills) return SameSkillsNote;
        _redetect = true;
        return null;
    }

    /// <summary>Why the active class's 직업각인 is only picked by hand, or null.</summary>
    public string? SameSkillsNote => ActiveClass is { SameSkills: true } ? SameSkills : null;
    const string SameSkills = "두 직업각인의 스킬이 같아 알아볼 수 없습니다. 마지막으로 고른 직업각인을 씁니다 (트레이나 설정 → 캐릭터에서 바꿀 수 있습니다).";

    string Detected(string sub)
    {
        settings.DetectedSubclass(sub);
        return $"직업각인을 {sub}(으)로 바꿨습니다.";
    }

    readonly SkillPoolStore _skills = new(SkillPoolStore.DefaultDir);
    const string NoSubclass = "캐릭터와 직업각인을 먼저 고르세요 (트레이나 설정 → 캐릭터).";

    /// <summary>Why 스킬 등록 can't run now (no character or 직업각인 picked, or its 직업각인 have the same skills), or null.</summary>
    public string? SkillsBlocked => Active is { Subclass.Length: > 0 } && ActiveClass is not null ? SameSkillsNote : NoSubclass;

    /// <summary>스킬 등록: adds <paramref name="ready"/> (the 일반 slots ready in colour, null when the bar is greyed out) to the active
    /// 직업각인's pool. Returns what happened, to tell the user.</summary>
    public string SubmitSkills(IReadOnlyList<Image>? ready)
    {
        if (Active is not { Subclass.Length: > 0 } c || ActiveClass is not { } cls) return NoSubclass;
        if (ready is null) return "스킬바가 회색입니다. 스킬을 쓸 수 있는 곳(마을 밖)에서 다시 눌러 주세요.";
        if (ready.Count == 0) return "준비된 일반 스킬이 없습니다. 재사용 대기 중이 아닐 때 다시 눌러 주세요.";
        try
        {
            var (added, total) = _skills.Add(cls.Name, c.Subclass, ready);
            return $"{cls.Name} · {c.Subclass}: 스킬 {added}개 추가 (총 {total}개)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"[skills] {ex.Message}");
            return "스킬을 저장하지 못했습니다: " + ex.Message;
        }
    }

    /// <summary>Empties the active 직업각인's pool of submitted skills. Returns what happened, to tell the user.</summary>
    public string ClearSkills()
    {
        if (Active is not { Subclass.Length: > 0 } c || ActiveClass is not { } cls) return NoSubclass;
        try { _skills.Clear(cls.Name, c.Subclass); return $"{cls.Name} · {c.Subclass}의 등록된 스킬을 지웠습니다."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "스킬을 지우지 못했습니다: " + ex.Message; }
    }

    Dictionary<string, Image>? LoadIcons(ClassInfo cls)
    {
        var dir = Path.Combine(profilesDir, "subclass", cls.Name);
        try
        {
            var files = cls.Subclasses.Keys.ToDictionary(s => s, s => Path.Combine(dir, s + ".png"));
            var missing = files.Where(f => !File.Exists(f.Value)).Select(f => f.Key).ToList();
            if (missing.Count == 0) return files.ToDictionary(f => f.Key, f => Platform.Png.Load(f.Value));
            // None at all is a class without icons yet; some is a misnamed or lost file.
            if (missing.Count < files.Count) System.Diagnostics.Trace.WriteLine($"[subclass] {dir}: no icon for {string.Join(", ", missing)}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"[subclass] {dir}: {ex.Message}");
            return null;
        }
    }

    static Image? Decode(string s)
    {
        try { return Image.FromBytes(Convert.FromBase64String(s)); }
        catch (FormatException) { return null; }
    }
}
