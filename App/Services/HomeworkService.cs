using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>숙제 and who is being played (homework.plan.md), glued to the app: the game menu's reads and loading screens drive
/// <see cref="CharacterWatch"/>, the grid's cards fill the character list, saved fights are named after the character and their
/// clears kept (<see cref="HomeworkStore"/>), and 불명 fights are given the character once it is known. The decisions are
/// Core's; this holds the state. Settings change on the UI thread only.</summary>
public sealed class HomeworkService(Settings settings, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> shipped, IReadOnlyList<string> classNames,
    HomeworkStore clears, RecordStore records, Dispatcher ui, Action settingsChanged, Action recordsChanged)
{
    volatile CharacterWatch _watch = CharacterWatch.Start(DateTime.Now);
    (string? Class, string? Sub) _playing;
    NameVote _currentVote = NameVote.None, _pickedVote = NameVote.None;   // the portrait's and the header's names, twice alike
    readonly Dictionary<string, NameVote> _levelVotes = [];   // each character's item level, twice alike

    /// <summary>The shipped raids and the ones the user added.</summary>
    public IReadOnlyList<HomeworkRaid> Raids => Homework.AllRaids(shipped, settings.Homework.CustomRaids);

    /// <summary>The character being played; null: 캐릭터 불명.</summary>
    public string? Current => _watch.Current;

    /// <summary>The character, the list or the clears changed (UI thread): 설정 → 숙제 and 전투 기록 show it again.</summary>
    public event Action? Updated;

    public IReadOnlyList<HomeworkClear> Clears() => clears.Load();

    IReadOnlyList<string> Known => [.. settings.Homework.Characters.Select(c => c.Name)];

    /// <summary>A game menu read (UI thread). The big names (under the portrait: who is played; the right panel's header: the card
    /// clicked) join the list once read alike twice, with the class of their card; the cards' own small names are too often
    /// misread to add anyone (--read-menu, 2026-11-04), so a character is listed once its card is clicked.</summary>
    public void Menu(MenuRead read, TimeSpan t)
    {
        if (!read.Open) { (_currentVote, _pickedVote) = (NameVote.None, NameVote.None); return; }
        (_currentVote, var current) = _currentVote.Read(read.Current is { } c ? CharacterName.Snap(c, Known) : null);
        (_pickedVote, var picked) = _pickedVote.Read(read.Picked is { } p ? CharacterName.Snap(p, Known) : null);
        bool listed = false;
        if (current is not null) listed |= Add(current, ClassOnCard(read, current));
        if (picked is not null) listed |= Add(picked, ClassOnCard(read, picked));
        foreach (var card in read.Cards)
            if (card.ItemLevel is { } level && CharacterName.Nearest(card.Name, CardNames(current)) is { } name) listed |= ReadLevel(name, level);
        if (current is not null && read.CurrentLevel is { } mine) listed |= ReadLevel(current, mine);
        var (watch, change) = _watch.Menu(read with { Current = current, Picked = picked }, Known, t);
        _watch = watch;
        if (listed) settingsChanged();
        Apply(change);
        if (listed && change is null) Updated?.Invoke();
    }

    /// <summary>A loading screen ended (UI thread): a switch picked in the grid happens now.</summary>
    public void Loaded(TimeSpan t)
    {
        var (watch, change) = _watch.Loaded(t, DateTime.Now);
        _watch = watch;
        Apply(change);
    }

    /// <summary>The app's class and 직업각인 (UI thread, on every change): learn the 직업각인 of the character played, notice a
    /// switch the menu didn't show, or know the character by a 직업각인 only it has.</summary>
    public void Playing(string? className, string? subclass)
    {
        if (string.IsNullOrWhiteSpace(subclass)) subclass = null;
        if ((className, subclass) == _playing) return;
        _playing = (className, subclass);
        var (watch, change) = _watch.Playing(className, subclass, settings.Homework.Characters, DateTime.Now);
        _watch = watch;
        Apply(change);
        if (Learn()) { settingsChanged(); Updated?.Invoke(); }
    }

    /// <summary>A character's tags this week, in pick order (UI thread).</summary>
    public IReadOnlyList<HomeworkTag> TagsOf(HomeworkCharacter c, IReadOnlyList<HomeworkClear> all)
    {
        var week = Homework.WeekStart(DateTime.Now);
        var mine = all.Where(x => x.Character == c.Name).ToList();
        var raids = Raids;
        return [.. c.Picks.Select(p => Homework.Tag(p, mine, raids, settings.Homework.Tags, week)).OfType<HomeworkTag>()];
    }

    /// <summary>One pick's tag this week (UI thread); null when its raid is no longer known.</summary>
    public HomeworkTag? TagOf(HomeworkCharacter c, HomeworkPick pick, IReadOnlyList<HomeworkClear> all) =>
        Homework.Tag(pick, all.Where(x => x.Character == c.Name), Raids, settings.Homework.Tags, Homework.WeekStart(DateTime.Now));

    /// <summary>An added raid's tag clicked (UI thread): cleared this week, or back.</summary>
    public void ToggleClear(HomeworkCharacter c, HomeworkPick pick)
    {
        try { clears.Toggle(new HomeworkClear(c.Name, pick.Raid, pick.Difficulty, DateTime.Now)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not kept: {ex.Message}"); return; }
        Updated?.Invoke();
    }

    /// <summary>A raid added on 설정 → 숙제 (UI thread); automatic: on every character with room. False without a name or a difficulty.</summary>
    public bool AddRaid(string name, IEnumerable<(string Difficulty, string Tag, HomeworkColor Color)> rows, bool auto)
    {
        if (Homework.NewCustom(Homework.CustomPrefix + Guid.NewGuid().ToString("N"), name, rows, auto) is not { } custom) return false;
        settings.Homework.CustomRaids = [.. settings.Homework.CustomRaids, custom];
        if (auto)
            foreach (var c in settings.Homework.Characters) c.Picks = [.. Homework.WithRaid(c.Picks, Homework.AsRaid(custom))];
        settingsChanged();
        Updated?.Invoke();
        return true;
    }

    /// <summary>An added raid removed, with its picks and tag names (UI thread).</summary>
    public void RemoveRaid(string id)
    {
        settings.Homework.CustomRaids = [.. settings.Homework.CustomRaids.Where(r => r.Id != id)];
        foreach (var c in settings.Homework.Characters) c.Picks = [.. c.Picks.Where(p => p.Raid != id)];
        foreach (var key in settings.Homework.Tags.Keys.Where(k => k.StartsWith(id + "|", StringComparison.Ordinal)).ToList()) settings.Homework.Tags.Remove(key);
        settingsChanged();
        Updated?.Invoke();
    }

    /// <summary>The grid's cards with picks and their tags (UI thread); none while 숙제 표시 is off.</summary>
    public IReadOnlyList<(MenuCard Card, IReadOnlyList<HomeworkTag> Tags)> GridTags(MenuRead read)
    {
        if (!read.Open || !settings.Homework.ShowOnGrid) return [];
        var all = Clears();
        return [.. Homework.OnGrid(read, _watch.Current, settings.Homework.Characters).Select(x => (x.Card, TagsOf(x.Character, all)))];
    }

    /// <summary>The fight as saved: named after the character being played (any thread).</summary>
    public FightRecord Name(FightRecord fight) => fight with { Character = _watch.Current };

    /// <summary>A fight was saved or settled (any thread): its clear is kept and its raid's pick raised.</summary>
    public void Saved(FightRecord r)
    {
        if (Homework.ClearOf(r, gates, Raids) is not { } clear) return;
        try { clears.Add(clear, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not kept: {ex.Message}"); return; }
        Trace.WriteLine($"[homework] {clear.Character}: {clear.Raid} [{clear.Difficulty}] cleared");
        ui.BeginInvoke(() =>
        {
            if (settings.Homework.Find(clear.Character) is { } c && Homework.Raise(c.Picks, clear, Raids) is var raised && !ReferenceEquals(raised, c.Picks))
            {
                c.Picks = [.. raised];
                settingsChanged();
            }
            Updated?.Invoke();
        });
    }

    /// <summary>A fight saved as 클리어 was settled 실패 later (the next record's boss was the same): its clear is taken back.</summary>
    public void Failed(FightRecord settled)
    {
        if (Homework.ClearOf(settled with { Cleared = true }, gates, Raids) is not { } was) return;
        try { if (!clears.Remove(was)) return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not taken back: {ex.Message}"); return; }
        Trace.WriteLine($"[homework] {was.Character}: {was.Raid} [{was.Difficulty}] not cleared after all");
        ui.BeginInvoke(() => Updated?.Invoke());
    }

    /// <summary>An edit of a fight changes its 숙제 (the clear it makes, this week): 편집 asks before <see cref="Edited"/>.</summary>
    public bool Affects(FightRecord before, FightRecord after)
    {
        var week = Homework.WeekStart(DateTime.Now);
        var (was, now) = (Homework.ClearOf(before, gates, Raids), Homework.ClearOf(after, gates, Raids));
        return was != now && (was?.At >= week || now?.At >= week);
    }

    /// <summary>편집 confirmed for 숙제: the old fight's clear taken back, the new one's kept.</summary>
    public void Edited(FightRecord before, FightRecord after)
    {
        try { if (Homework.ClearOf(before, gates, Raids) is { } was) clears.Remove(was); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not taken back: {ex.Message}"); }
        Saved(after);
        Updated?.Invoke();
    }

    /// <summary>A character added by hand on 설정 → 숙제 (UI thread); false when the name isn't one or is known already.</summary>
    public bool AddByHand(string text)
    {
        if (CharacterName.Clean(text) is not { } name || settings.Homework.Find(name) is not null) return false;
        Add(name, null);
        settingsChanged();
        Updated?.Invoke();
        return true;
    }

    void Apply(CharacterChange? change)
    {
        if (change is null) return;
        Trace.WriteLine($"[character] {change.Name ?? "캐릭터 불명"}{(change.BackfillFrom is { } from ? $" (불명 fights since {from:HH:mm} are its)" : "")}");
        if (Learn()) settingsChanged();
        Updated?.Invoke();
        if (change is { Name: { } name, BackfillFrom: { } since }) _ = Task.Run(() => Backfill(name, since));
    }

    /// <summary>The 불명 fights since <paramref name="since"/> were <paramref name="name"/>'s: rewritten under it, their clears kept.</summary>
    void Backfill(string name, DateTime since)
    {
        try
        {
            int n = 0;
            foreach (var (file, _) in records.List().Where(x => string.IsNullOrWhiteSpace(x.Record.Character) && x.Record.StartedAt >= since))
                if (records.SetCharacter(file, name) is { } named)   // read again under the store's lock: a result settled meanwhile stays
                {
                    Saved(named);
                    n++;
                }
            if (n > 0) { Trace.WriteLine($"[character] {n} 불명 fight(s) given to {name}"); recordsChanged(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[character] backfill failed: {ex.Message}"); }
    }

    /// <summary>The names a card can be: not the character being played, whose card shows "접속 중" and is never read.</summary>
    IReadOnlyList<string> CardNames(string? current) => [.. Known.Where(n => n != current)];

    /// <summary>A level read for <paramref name="name"/>: counts once read alike twice in a row, and only upward
    /// (<see cref="Homework.LevelFrom"/>); true when it changed the character.</summary>
    bool ReadLevel(string name, int read)
    {
        var (vote, level) = _levelVotes.GetValueOrDefault(name, NameVote.None).Read(read.ToString());
        _levelVotes[name] = vote;
        if (level is null || settings.Homework.Find(name) is not { } c) return false;
        int next = Homework.LevelFrom(c.ItemLevel, read);
        if (next == c.ItemLevel) return false;
        SetLevel(c, next);
        return true;
    }

    void SetLevel(HomeworkCharacter c, int level)
    {
        var picks = Homework.PicksAfter(c.Picks, c.ItemLevel, level, settings.Homework.FollowLevel, Raids);
        c.ItemLevel = level;
        c.Picks = [.. picks];
        Trace.WriteLine($"[homework] {c.Name}: level {level}, {string.Join(", ", c.Picks.Select(p => $"{p.Raid} {p.Difficulty}"))}");
    }

    /// <summary>A level typed on 설정 → 숙제 (UI thread): trusted as it is, up or down; empty forgets it.</summary>
    public void TypedLevel(HomeworkCharacter c, int? level)
    {
        if (level == c.ItemLevel) return;
        if (level is { } typed) SetLevel(c, typed);
        else c.ItemLevel = null;
        settingsChanged();
        Updated?.Invoke();
    }

    /// <summary>The class on <paramref name="name"/>'s card (the card whose name is nearest to it), snapped to a known class; null
    /// when no card is its (the card being played shows "접속 중" instead).</summary>
    string? ClassOnCard(MenuRead read, string name)
    {
        var names = Known.Append(name).Distinct().ToList();
        var card = read.Cards.FirstOrDefault(c => CharacterName.Nearest(c.Name, names) == name);
        return card?.ClassName is { } cls ? CharacterName.Nearest(cls, classNames) : null;
    }

    /// <summary>Adds a character to the list, or fills its class when it had none; true when the list changed.</summary>
    bool Add(string name, string? className)
    {
        if (settings.Homework.Find(name) is { } known)
        {
            if (known.ClassName is not null || className is null) return false;
            known.ClassName = className;
            return true;
        }
        var picks = settings.Homework.CustomRaids.Where(r => r.Auto).Select(Homework.AsRaid).Aggregate((IReadOnlyList<HomeworkPick>)[], Homework.WithRaid);
        settings.Homework.Characters.Add(new HomeworkCharacter { Name = name, ClassName = className, Picks = [.. picks] });
        return true;
    }

    /// <summary>The character played takes the app's 직업각인 when the app is on its class (read from its card: the app's class
    /// may still be the last character's right after a switch); true when that changed it.</summary>
    bool Learn()
    {
        if (settings.Homework.Find(_watch.Current) is not { } c || c.ClassName is null || c.ClassName != _playing.Class) return false;
        if (_playing.Sub is not { } sub || c.Subclass == sub) return false;
        c.Subclass = sub;
        return true;
    }
}
