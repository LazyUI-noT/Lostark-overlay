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
public sealed class HomeworkService(Settings settings, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids, IReadOnlyList<string> classNames,
    HomeworkStore clears, RecordStore records, Dispatcher ui, Action settingsChanged, Action recordsChanged)
{
    volatile CharacterWatch _watch = CharacterWatch.Start(DateTime.Now);
    (string? Class, string? Sub) _playing;
    NameVote _currentVote = NameVote.None, _pickedVote = NameVote.None;   // the portrait's and the header's names, twice alike

    public IReadOnlyList<HomeworkRaid> Raids => raids;

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
        return [.. c.Picks.Select(p => Homework.Tag(p, mine, raids, settings.Homework.Tags, week)).OfType<HomeworkTag>()];
    }

    /// <summary>The grid's cards with picks and their tags (UI thread); none while 숙제 표시 is off.</summary>
    public IReadOnlyList<(MenuCard Card, IReadOnlyList<HomeworkTag> Tags)> GridTags(MenuRead read)
    {
        if (!read.Open || !settings.Homework.ShowOnGrid) return [];
        var all = Clears();
        return [.. read.Cards.Select(card => (card, c: settings.Homework.Find(CharacterName.Nearest(card.Name, Known))))
            .Where(x => x.c is { Hidden: false, Picks.Count: > 0 })
            .Select(x => (x.card, TagsOf(x.c!, all)))];
    }

    /// <summary>The fight as saved: named after the character being played (any thread).</summary>
    public FightRecord Name(FightRecord fight) => fight with { Character = _watch.Current };

    /// <summary>A fight was saved or settled (any thread): its clear is kept and its raid's pick raised.</summary>
    public void Saved(FightRecord r)
    {
        if (Homework.ClearOf(r, gates, raids) is not { } clear) return;
        try { clears.Add(clear, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not kept: {ex.Message}"); return; }
        Trace.WriteLine($"[homework] {clear.Character}: {clear.Raid} [{clear.Difficulty}] cleared");
        ui.BeginInvoke(() =>
        {
            if (settings.Homework.Find(clear.Character) is { } c && Homework.Raise(c.Picks, clear, raids) is var raised && !ReferenceEquals(raised, c.Picks))
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
        if (Homework.ClearOf(settled with { Cleared = true }, gates, raids) is not { } was) return;
        try { if (!clears.Remove(was)) return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not taken back: {ex.Message}"); return; }
        Trace.WriteLine($"[homework] {was.Character}: {was.Raid} [{was.Difficulty}] not cleared after all");
        ui.BeginInvoke(() => Updated?.Invoke());
    }

    /// <summary>An edit of a fight changes its 숙제 (the clear it makes, this week): 편집 asks before <see cref="Edited"/>.</summary>
    public bool Affects(FightRecord before, FightRecord after)
    {
        var week = Homework.WeekStart(DateTime.Now);
        var (was, now) = (Homework.ClearOf(before, gates, raids), Homework.ClearOf(after, gates, raids));
        return was != now && (was?.At >= week || now?.At >= week);
    }

    /// <summary>편집 confirmed for 숙제: the old fight's clear taken back, the new one's kept.</summary>
    public void Edited(FightRecord before, FightRecord after)
    {
        try { if (Homework.ClearOf(before, gates, raids) is { } was) clears.Remove(was); }
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
        settings.Homework.Characters.Add(new HomeworkCharacter { Name = name, ClassName = className });
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
