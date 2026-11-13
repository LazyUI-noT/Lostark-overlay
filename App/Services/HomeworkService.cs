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
    HomeworkStore clears, RecordStore records, Dispatcher ui, Action settingsChanged, Action recordsChanged, Func<bool> hasKey)
{
    volatile IReadOnlyList<string> _fightNames = [];   // the names on saved fights: known without a key too (settings-access.plan.md B)
    bool _menuOpen;
    volatile CharacterWatch _watch = CharacterWatch.Start(DateTime.Now);
    (string? Class, string? Sub) _playing;
    NameVote _currentVote = NameVote.None, _pickedVote = NameVote.None;   // the portrait's and the header's names, twice alike
    readonly Dictionary<string, NameVote> _levelVotes = [];   // each character's item level, twice alike
    readonly Dictionary<(double X, double Y), NameVote> _cardVotes = [];   // each card's small name, twice alike, by its place on the grid
    string? _ownersLogged;   // the last [homework] owners line: logged on change only
    IReadOnlyDictionary<int, string>? _orderMismatch;   // the last read's sure cards when they fit the learned card order nowhere (CardOrder.Step)

    /// <summary>The shipped raids and the ones the user added.</summary>
    public IReadOnlyList<HomeworkRaid> Raids => Homework.AllRaids(shipped, settings.Homework.CustomRaids);

    /// <summary>The character being played; null: 캐릭터 불명.</summary>
    public string? Current => _watch.Current;

    /// <summary>The character, the list or the clears changed (UI thread): 설정 → 숙제 and 전투 기록 show it again.</summary>
    public event Action? Updated;

    /// <summary>The game menu named the character now played (UI thread): App switches to its remembered 직업각인 (NameSwitch).</summary>
    public event Action<HomeworkCharacter?>? CharacterRead;

    public IReadOnlyList<HomeworkClear> Clears() => clears.Load();

    IReadOnlyList<string> Known => [.. settings.Homework.Characters.Select(c => c.Name)];

    /// <summary>The names the big names snap to: not the ones from a card's small name, which a big name corrects instead.</summary>
    IReadOnlyList<string> Confirmed => [.. settings.Homework.Characters.Where(c => !c.FromCard).Select(c => c.Name)];

    /// <summary>A game menu read (UI thread). The big names (under the portrait: who is played; the right panel's header: the card
    /// clicked) join the list once read alike twice, with the class of their card. The cards' small names join too, twice alike
    /// (homework-auto.plan.md, user 2026-11-08), marked <see cref="HomeworkCharacter.FromCard"/>: they can misread the same way
    /// every time, so a big name near one renames it.</summary>
    /// <remarks>Without an API key (settings-access.plan.md B, user 2026-11-11) no name joins the list: a read counts only when
    /// it snaps to a listed name or one on a saved fight, else the watch takes it as someone else (캐릭터 불명). Class and item
    /// level still fill in for listed characters.</remarks>
    public void Menu(MenuRead read, TimeSpan t)
    {
        bool key = hasKey();
        if (read.Open && !_menuOpen && !key) LoadFightNames();
        _menuOpen = read.Open;
        if (!read.Open) { (_currentVote, _pickedVote, _orderMismatch) = (NameVote.None, NameVote.None, null); _cardVotes.Clear(); return; }
        IReadOnlyList<string>? known = key ? null : [.. Known, .. _fightNames];   // null: every name counts
        IReadOnlyList<string> snapTo = known is null ? Confirmed : [.. Confirmed, .. _fightNames];
        (_currentVote, var voted) = _currentVote.Read(read.Current is { } c ? CharacterName.Snap(c, snapTo) : null);
        (_pickedVote, var votedPick) = _pickedVote.Read(read.Picked is { } p ? CharacterName.Snap(p, snapTo) : null);
        string? Counted(string? name) => name is null || known is null || known.Contains(name) ? name : null;
        var (current, picked) = (Counted(voted), Counted(votedPick));
        bool listed = false;
        if (current is not null) listed |= Confirm(current, read, played: true) | Add(current, ClassOnCard(read, current), register: key);
        if (picked is not null) listed |= Confirm(picked, read, played: false) | Add(picked, ClassOnCard(read, picked), register: key);
        if (key)
            foreach (var (card, owner) in CardOrder.Owners(read, current, settings.Homework.Characters, settings.Homework.Order))
                listed |= FromCard(card, current, owned: owner is not null);   // a card the order places is that character, not a new one
        foreach (var card in read.Cards)
        {
            var others = CardCharacters(current);
            if (card.ClassName is { } cls && Homework.SureOwner(card, others) is { } sure)
                listed |= Add(sure, CharacterName.Nearest(cls, classNames));   // fills only an empty class, kept for good: sure owners only
            if (card.ItemLevel is { } level && Homework.CardOwner(card, others) is { } name) listed |= ReadLevel(name, level);
        }
        if (current is not null && read.CurrentLevel is { } mine) listed |= ReadLevel(current, mine);
        listed |= LearnOrder(read, current);
        LogOwners(read, current ?? _watch.Current);
        var (watch, change) = _watch.Menu(read with { Current = voted, Picked = votedPick }, known ?? Known, t, onlyKnown: known is not null);
        _watch = watch;
        if (listed) settingsChanged();
        Apply(change);
        if (listed && change is null) Updated?.Invoke();
    }

    /// <summary>The names on saved fights (in the background, at each menu opening without a key): a name typed in 기록 수정 counts.</summary>
    void LoadFightNames() => Task.Run(() =>
    {
        try { _fightNames = [.. records.List().Select(x => x.Record.Character).OfType<string>().Where(n => n.Length > 0).Distinct()]; }
        catch (Exception ex) { Trace.WriteLine($"[character] saved fights' names not read: {ex.Message}"); }   // background: keep the last names
    });

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

    /// <summary>클리어 clicked, or an added raid's tag (UI thread): cleared this week, or back.</summary>
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
        if (!read.Open || !settings.Homework.ShowOnGrid || !settings.HomeworkOn(hasKey())) return [];
        var all = Clears();
        return [.. Homework.OnGrid(read, _watch.Current, settings.Homework.Characters, settings.Homework.Order).Select(x => (x.Card, TagsOf(x.Character, all)))];
    }

    /// <summary>The 전투력 tags for an open menu (UI thread): the cards' owners and the played character; none while
    /// 게임 메뉴에 전투력 표시 is off. Not gated by 숙제 표시.</summary>
    public IReadOnlyList<PowerTag> PowerTags(MenuRead read, Func<string, Role> roleOf) =>
        settings.ApiShowCombatPower
            ? MenuPower.Tags(read, _watch.Current, CardOrder.Owners(read, _watch.Current, settings.Homework.Characters, settings.Homework.Order), settings.CombatPower, roleOf)
            : [];

    /// <summary>The fight as saved: named after the character being played (any thread).</summary>
    public FightRecord Name(FightRecord fight) => fight with { Character = _watch.Current };

    /// <summary>A fight was saved or settled (any thread): its clear is kept and its raid's pick raised.</summary>
    public void Saved(FightRecord r)
    {
        if (Homework.ClearOf(r, gates, Raids) is { } clear) Keep(clear);
    }

    void Keep(HomeworkClear clear)
    {
        if (!settings.HomeworkOn(hasKey())) return;   // 숙제 off: no clears recorded (a new install without a key)
        try { clears.Add(clear, DateTime.Now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not kept: {ex.Message}"); return; }
        Trace.WriteLine($"[homework] {clear.Character}: {clear.Raid} [{clear.Difficulty}] {(clear.Unsure ? "cleared, to check" : "cleared")}");
        ui.BeginInvoke(() =>
        {
            if (!clear.Unsure && settings.Homework.Find(clear.Character) is { } c && Homework.Raise(c.Picks, clear, Raids) is var raised && !ReferenceEquals(raised, c.Picks))
            {
                c.Picks = [.. raised];
                settingsChanged();
            }
            Updated?.Invoke();
        });
    }

    readonly List<(WatchDecision Decision, DateTime At)> _unknown = [];   // decided while 캐릭터 불명, given the character read next

    /// <summary>The fallback decided (<see cref="HomeworkWatch"/>, UI thread): a clear kept for the character played, or held until
    /// it is known (as 불명 fights are); a retry takes the clear back.</summary>
    public void Fallback(WatchDecision d)
    {
        Trace.WriteLine($"[homework] fallback: {d.Raid} [{d.Difficulty ?? "?"}] {d.Outcome}");
        if (_watch.Current is { } who) KeepFallback(d, who, DateTime.Now);
        else if (d.Outcome != WatchOutcome.Failed) _unknown.Add((d, DateTime.Now));
        else _unknown.RemoveAll(x => x.Decision.Raid == d.Raid);
    }

    void KeepFallback(WatchDecision d, string who, DateTime at)
    {
        if (d.Outcome == WatchOutcome.Failed)
        {
            // Only the fallback's own clear: one set by hand since (a click replaces it) stays.
            if (!_fallbackKept.Remove((who, d.Raid), out var mine)) return;
            try { if (!clears.Remove(mine)) return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] clear not taken back: {ex.Message}"); return; }
            ui.BeginInvoke(() => Updated?.Invoke());
            return;
        }
        if (Raids.FirstOrDefault(r => r.Id == d.Raid) is not { } raid) return;
        // The difficulty the data can't tell is the one picked, else the lowest: it is 확인필요 either way.
        string difficulty = d.Difficulty ?? settings.Homework.Find(who)?.Picks.FirstOrDefault(p => p.Raid == raid.Id)?.Difficulty ?? raid.Levels[0].Difficulty;
        var clear = new HomeworkClear(who, raid.Id, difficulty, at, d.Outcome == WatchOutcome.Unsure);
        _fallbackKept[(who, raid.Id)] = clear;
        Keep(clear);
    }

    // ponytail: in memory only, so a retry after an app restart keeps the clear (rare; 미완 is one click).
    readonly Dictionary<(string Character, string Raid), HomeworkClear> _fallbackKept = [];

    /// <summary>The 불명 stretch since <paramref name="since"/> was <paramref name="name"/>'s: the fallback's decisions in it count for them.</summary>
    void GiveFallback(string name, DateTime since)
    {
        foreach (var (d, at) in _unknown.Where(x => x.At >= since).ToList()) KeepFallback(d, name, at);
        _unknown.Clear();
    }

    /// <summary>[미완 | 클리어 | 확인필요] clicked (UI thread): this week's state of the pick's raid. A clear that is there keeps its difficulty
    /// when only the state changes.</summary>
    public void SetState(HomeworkCharacter c, HomeworkPick pick, HomeworkPart part)
    {
        var now = DateTime.Now;
        var was = clears.Load().Where(x => x.Character == c.Name && x.Raid == pick.Raid && x.At >= Homework.WeekStart(now)).MaxBy(x => x.At);
        var to = part == HomeworkPart.미완 ? null : new HomeworkClear(c.Name, pick.Raid, was?.Difficulty ?? pick.Difficulty, now, part == HomeworkPart.확인필요);
        try { clears.Set(c.Name, pick.Raid, now, to); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] state not kept: {ex.Message}"); return; }
        Updated?.Invoke();
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
        // No Learn() here: the class shown is still the last character's, and a same-class alt would take its 직업각인.
        // Playing learns once the class follows (NameSwitch, the skill bar).
        Updated?.Invoke();
        if (change.Name is { } read) CharacterRead?.Invoke(settings.Homework.Find(read));
        if (change is { Name: { } name, BackfillFrom: { } since })
        {
            GiveFallback(name, since);
            _ = Task.Run(() => Backfill(name, since));
        }
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

    /// <summary>Whose a grid card can be: every listed character but the one played (its card shows 접속 중).</summary>
    IReadOnlyList<HomeworkCharacter> CardCharacters(string? current) => [.. settings.Homework.Characters.Where(c => c.Name != current)];

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

    /// <summary>The class on <paramref name="name"/>'s card (the card surely its, Homework.SureOwner), snapped to a known class; null
    /// when no card is its (the card being played shows "접속 중" instead).</summary>
    string? ClassOnCard(MenuRead read, string name)
    {
        var characters = settings.Homework.Characters.Any(c => c.Name == name) ? settings.Homework.Characters
            : [.. settings.Homework.Characters, new HomeworkCharacter { Name = name }];
        var card = read.Cards.FirstOrDefault(c => Homework.SureOwner(c, characters) == name);
        return card?.ClassName is { } cls ? CharacterName.Nearest(cls, classNames) : null;
    }

    /// <summary>The grid's card order after this read (<see cref="CardOrder.Step"/>, card-order.plan.md); true when it changed.</summary>
    bool LearnOrder(MenuRead read, string? current)
    {
        (var order, _orderMismatch) = CardOrder.Step(settings.Homework.Order, _orderMismatch, CardOrder.Sure(read, current, settings.Homework.Characters));
        if (order.SequenceEqual(settings.Homework.Order)) return false;
        settings.Homework.Order = [.. order];   // a new list: the save thread reads it
        Trace.WriteLine($"[homework] card order: {string.Join(" / ", order.Select(n => n.Length > 0 ? n : "?"))}");
        return true;
    }

    /// <summary>Whose each card is, as the grid tags take it (a replay or a user's log shows it): logged when it changes.</summary>
    void LogOwners(MenuRead read, string? current)
    {
        var hw = settings.Homework;
        var owners = CardOrder.Owners(read, current, hw.Characters, hw.Order).Select(x => $"{x.Card.Name}→{x.Owner ?? "?"}");
        bool placed = read.PlayingCard is not null || CardOrder.PlayingCard(read, current, hw.Characters, hw.Order) is not null;
        string line = string.Join(" / ", owners) + (placed ? $" + 접속 중→{current ?? "?"}" : "");
        if (line == _ownersLogged) return;
        _ownersLogged = line;
        Trace.WriteLine($"[homework] owners: {line}");
    }

    /// <summary>The 원정대 from the Lost Ark API (lostark-api.plan.md: the API wins, the screen fills gaps): misread card names take
    /// the API's spelling, every character is listed with its class, item level and (from its armory) 직업각인, and its name counts
    /// as confirmed. UI thread.</summary>
    public void FromApi(IReadOnlyList<ApiCharacter> roster, IReadOnlyList<ApiProfile> profiles)
    {
        bool changed = false;
        foreach (var (from, to) in ApiRoster.Renames(settings.Homework.Characters, [.. roster.Select(c => c.Name)]))
            changed |= Move(from, to);
        foreach (var a in roster)
        {
            string? cls = classNames.Contains(a.ClassName) ? a.ClassName : null;   // "전사(남)" (not awakened) is no class here
            changed |= Add(a.Name, cls);
            if (settings.Homework.Find(a.Name) is not { } c) continue;
            if (cls is not null && c.ClassName != cls) (c.ClassName, changed) = (cls, true);
            if (c.FromCard) (c.FromCard, changed) = (false, true);
            int level = (int)Math.Floor(a.ItemLevel);
            if (level > 0 && level != c.ItemLevel) { SetLevel(c, level); changed = true; }
            if (profiles.FirstOrDefault(p => p.Name == a.Name)?.Subclass is { } sub && CombatPowerBook.KeyOf(sub) is { Length: > 0 } key && c.Subclass != key)
                (c.Subclass, changed) = (key, true);
        }
        if (!changed) return;
        settingsChanged();
        Updated?.Invoke();
    }

    /// <summary>Adds a character to the list, or fills its class when it had none; true when the list changed.</summary>
    bool Add(string name, string? className, bool fromCard = false, bool register = true)
    {
        if (settings.Homework.Find(name) is { } known)
        {
            if (known.ClassName is not null || className is null) return false;
            known.ClassName = className;
            return true;
        }
        if (!register) return false;   // without a key the menu adds no one
        var picks = settings.Homework.CustomRaids.Where(r => r.Auto).Select(Homework.AsRaid).Aggregate((IReadOnlyList<HomeworkPick>)[], Homework.WithRaid);
        settings.Homework.Characters.Add(new HomeworkCharacter { Name = name, ClassName = className, FromCard = fromCard, Picks = [.. picks] });
        if (fromCard) Trace.WriteLine($"[homework] {name}: listed from its card");
        return true;
    }

    /// <summary>A card's small name: listed once read alike twice when it is near no listed name (<see cref="Homework.NewFromCard"/>).</summary>
    bool FromCard(MenuCard card, string? current, bool owned)
    {
        var slot = (Math.Round(card.X, 3), Math.Round(card.Y, 3));
        var (vote, name) = _cardVotes.GetValueOrDefault(slot, NameVote.None).Read(owned ? null : Homework.NewFromCard(card, settings.Homework.Characters, current));
        _cardVotes[slot] = vote;
        return name is not null && Add(name, card.ClassName is { } cls ? CharacterName.Nearest(cls, classNames) : null, fromCard: true);
    }

    /// <summary>A big name read: a character listed from its card is confirmed when read as listed, or takes this name when its card's
    /// small name was a misread of it (<see cref="Homework.ConfirmedBy"/>).</summary>
    bool Confirm(string bigName, MenuRead read, bool played) =>
        Homework.ConfirmName(settings.Homework, bigName)
        || Homework.ConfirmedBy(bigName, settings.Homework.Characters, [.. read.Cards.Select(c => c.Name)], played) is { } c && Move(c.Name, bigName);

    /// <summary>A character renamed on 설정 → 숙제 (UI thread): cleaned, onto a listed name the two merge; false when the text isn't a
    /// name or nothing changed.</summary>
    public bool Rename(HomeworkCharacter c, string text)
    {
        if (CharacterName.Clean(text) is not { } name || !Move(c.Name, name)) return false;
        settingsChanged();
        Updated?.Invoke();
        return true;
    }

    /// <summary>The list, this week's clears, who is played and the saved fights (in the background) follow a rename.</summary>
    bool Move(string from, string to)
    {
        if (from == to || settings.Homework.Find(from) is null) return false;
        try { clears.Rename(from, to); }   // first: a file that can't be written leaves the list as it was, not split from its clears
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[homework] not renamed {from} → {to}: clears unwritable ({ex.Message})");
            return false;
        }
        Homework.Rename(settings.Homework, from, to);
        Trace.WriteLine($"[homework] renamed {from} → {to}");
        var w = _watch;
        if (w.Current == from || w.Pending == from) _watch = w with { Current = w.Current == from ? to : w.Current, Pending = w.Pending == from ? to : w.Pending };
        _levelVotes.Remove(from);
        foreach (var ((who, raid), clear) in _fallbackKept.Where(x => x.Key.Character == from).ToList())
        {
            _fallbackKept.Remove((who, raid));
            _fallbackKept[(to, raid)] = clear with { Character = to };
        }
        lock (_renamesGate) _renames = _renames.ContinueWith(_ => RenameFights(from, to), TaskScheduler.Default);   // in order: A→B then B→C
        return true;
    }

    readonly object _renamesGate = new();
    Task _renames = Task.CompletedTask;

    /// <summary>The saved fights of <paramref name="from"/> are <paramref name="to"/>'s; a file that fails is traced and the rest go on.
    /// A fight named before the rename and saved after this ran keeps the old name (rare: only while a fight is being saved).</summary>
    void RenameFights(string from, string to)
    {
        int n = 0, failed = 0;
        IReadOnlyList<(string File, FightRecord Record)> all;
        try { all = records.List(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[homework] fights not renamed: {ex.Message}"); return; }
        foreach (var (file, _) in all.Where(x => x.Record.Character == from))
            try { if (records.SetCharacter(file, to) is not null) n++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; Trace.WriteLine($"[homework] fight {file} not renamed: {ex.Message}"); }
        if (n > 0 || failed > 0) Trace.WriteLine($"[homework] {n} fight(s) of {from} renamed {to}{(failed > 0 ? $", {failed} failed" : "")}");
        if (n > 0) recordsChanged();
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
