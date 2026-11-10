using System.Globalization;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>A 숙제 tag's colour: the game's difficulty colours (노말 white, 하드 orange, 나이트메어 purple).</summary>
public enum HomeworkColor { White, Orange, Purple }

/// <summary>Who decides 숙제's automatic clears: a recorded fight (the meter and 전투 기록 on), the boss bar alone (<see cref="HomeworkWatch"/>), or
/// nobody: no bar read, so only the buttons count and the page says so.</summary>
public enum HomeworkSource { RecordedFight, BossBar, None }

/// <summary>The three parts of a raid's state button on 설정 → 숙제.</summary>
public enum HomeworkPart { 미완, 클리어, 확인필요 }

/// <summary>One difficulty of a 숙제 raid (raids.json "homework"), in rank order: its default tag ("세하"), colour, and the
/// gate(s) whose 클리어 finishes the raid on it.</summary>
/// <remarks><paramref name="EndsHidden"/>: its real end is a ??? phase saved in the file of <paramref name="Last"/> (종막 하드 2-3 in 2-2's), reached or the raid isn't done; that file may carry the panel's name, its HP being in no data.</remarks>
public sealed record HomeworkLevel(string Difficulty, string Tag, HomeworkColor Color, IReadOnlyList<string> Last, int? Entry = null, bool EndsHidden = false);

/// <summary>A raid tracked weekly: its gates (raids.json ids) and difficulties, lowest first.</summary>
public sealed record HomeworkRaid(string Id, string Name, IReadOnlyList<string> Gates, IReadOnlyList<HomeworkLevel> Levels);

/// <summary>A raid the user added on 설정 → 숙제 (a missing or time-limited one, like 종막 EX): no gates or entry levels, so no fight
/// clears it and no level allocates it; its tag is clicked. <see cref="Auto"/>: put on every character, new ones too.</summary>
public sealed record HomeworkCustomRaid(string Id, string Name, IReadOnlyList<HomeworkLevel> Levels, bool Auto);

/// <summary>A raid a character picked to track, at the difficulty picked.</summary>
public sealed record HomeworkPick(string Raid, string Difficulty);

/// <summary>A raid cleared: by whom, on which difficulty, and when the fight started (its week). <paramref name="Unsure"/> (확인필요):
/// the fallback (<see cref="HomeworkWatch"/>) thinks so but can't tell; a file saved before it has no such state and means a clear.</summary>
public sealed record HomeworkClear(string Character, string Raid, string Difficulty, DateTime At, bool Unsure = false);

/// <summary>A pick as drawn: the text, its colour, crossed out when cleared this week, and ↓ (−1) / ↑ (+1) when cleared on a
/// lower / higher difficulty than picked. <paramref name="Unsure"/>: cleared perhaps (drawn with ! and a dotted cross).</summary>
public sealed record HomeworkTag(string Text, HomeworkColor Color, bool Done, int Arrow, bool Unsure = false);

/// <summary>숙제 (homework.plan.md): weekly raid clears per character. A raid's weekly entry is shared by its difficulties, so a
/// clear on any of them crosses the pick out; one above the pick raises it. Pure.</summary>
public static class Homework
{
    /// <summary>The start of the 숙제 week <paramref name="local"/> is in: the last Wednesday 06:00 at or before it.</summary>
    public static DateTime WeekStart(DateTime local) => BinSchedule.Last(local, DayOfWeek.Wednesday, 6);

    /// <summary>The clear a saved fight makes: 클리어 on its difficulty's last gate, by a known character; else null.</summary>
    public static HomeworkClear? ClearOf(FightRecord r, IReadOnlyList<RaidGate> gates, IReadOnlyList<HomeworkRaid> raids)
    {
        if (r.Cleared != true || string.IsNullOrWhiteSpace(r.Character) || r.Difficulty is not { } difficulty) return null;
        var gate = gates.FirstOrDefault(g => RaidData.Label(g) == r.Gate);
        foreach (var raid in raids)
            if (raid.Levels.FirstOrDefault(l => l.Difficulty == difficulty) is { } level && Finishes(r, level, gate, gates))
                return new HomeworkClear(r.Character, raid.Id, difficulty, r.StartedAt);
        return null;
    }

    static bool Finishes(FightRecord r, HomeworkLevel level, RaidGate? gate, IReadOnlyList<RaidGate> gates)
    {
        if (level.EndsHidden && !FightRegions.EndedHidden(r.Boss)) return false;
        if (gate is not null) return level.Last.Contains(gate.Id);
        return level.EndsHidden && r.Gate is { } name && gates.Any(g => level.Last.Contains(g.Id) && RaidData.IsRaidNamed(g, name));
    }

    /// <summary>The source of automatic clears; a recorded fight and the fallback never both decide. The meter's own read feeds the bar
    /// when it is on; with it off the bar needs 보스 체력 읽기. <paramref name="panelRead"/>: the 전분 panel is read or a fight is being
    /// recorded; with neither no record can come, so the bar decides (user, 2026-11-06).</summary>
    public static HomeworkSource SourceOf(bool meterOn, bool record, bool bossHpRead, bool panelRead = true) =>
        meterOn && record && panelRead ? HomeworkSource.RecordedFight : meterOn || bossHpRead ? HomeworkSource.BossBar : HomeworkSource.None;

    /// <summary>The part of the button that is lit for a tag.</summary>
    public static HomeworkPart PartOf(HomeworkTag? tag) => tag is not { Done: true } ? HomeworkPart.미완 : tag.Unsure ? HomeworkPart.확인필요 : HomeworkPart.클리어;

    /// <summary>The key of a renamed tag in <see cref="HomeworkSettings.Tags"/>.</summary>
    public static string TagKey(string raid, string difficulty) => $"{raid}|{difficulty}";

    /// <summary>The tag text: the user's name for it, else the default.</summary>
    public static string TagText(HomeworkRaid raid, HomeworkLevel level, IReadOnlyDictionary<string, string> names) =>
        names.TryGetValue(TagKey(raid.Id, level.Difficulty), out var name) && !string.IsNullOrWhiteSpace(name) ? name : level.Tag;

    /// <summary>A pick as drawn, given the character's clears; null when its raid or difficulty is no longer known.</summary>
    public static HomeworkTag? Tag(HomeworkPick pick, IEnumerable<HomeworkClear> clears, IReadOnlyList<HomeworkRaid> raids,
        IReadOnlyDictionary<string, string> names, DateTime weekStart)
    {
        if (raids.FirstOrDefault(r => r.Id == pick.Raid) is not { } raid) return null;
        int picked = Rank(raid, pick.Difficulty);
        if (picked < 0) return null;
        var clear = clears.Where(c => c.Raid == raid.Id && c.At >= weekStart && Rank(raid, c.Difficulty) >= 0).MaxBy(c => c.At);
        if (clear is null) return new HomeworkTag(TagText(raid, raid.Levels[picked], names), raid.Levels[picked].Color, false, 0);
        int done = Rank(raid, clear.Difficulty);
        return new HomeworkTag(TagText(raid, raid.Levels[done], names), raid.Levels[done].Color, true, Math.Sign(done - picked), clear.Unsure);
    }

    /// <summary>The picks after <paramref name="clear"/>: its raid's pick raised to the difficulty cleared when that is higher;
    /// the same list when nothing changes (a lower or equal clear never lowers a pick).</summary>
    public static IReadOnlyList<HomeworkPick> Raise(IReadOnlyList<HomeworkPick> picks, HomeworkClear clear, IReadOnlyList<HomeworkRaid> raids)
    {
        if (raids.FirstOrDefault(r => r.Id == clear.Raid) is not { } raid) return picks;
        int done = Rank(raid, clear.Difficulty);
        if (!picks.Any(p => p.Raid == raid.Id && Rank(raid, p.Difficulty) < done)) return picks;
        return [.. picks.Select(p => p.Raid == raid.Id && Rank(raid, p.Difficulty) < done ? p with { Difficulty = clear.Difficulty } : p)];
    }

    /// <summary>The raids a character gets by item level (user, 2026-11-04): each raid at the highest difficulty it can enter (of
    /// two at one entry level, the higher), the <see cref="AutoPicks"/> with the highest entry level, a tie going to the raid whose
    /// top difficulty is highest. A raid without entry levels is picked by hand only.</summary>
    public static IReadOnlyList<HomeworkPick> Allocate(int itemLevel, IReadOnlyList<HomeworkRaid> raids) =>
        [.. raids.Select(r => (Raid: r, Open: r.Levels.LastOrDefault(l => l.Entry <= itemLevel)))
            .Where(x => x.Open is not null)
            .OrderByDescending(x => x.Open!.Entry).ThenByDescending(x => x.Raid.Levels.Max(l => l.Entry))
            .Take(AutoPicks).Select(x => new HomeworkPick(x.Raid.Id, x.Open!.Difficulty))];

    /// <summary>The picks once a character's level goes from <paramref name="was"/> to <paramref name="now"/>. A first level gives
    /// a character without picks its raids. A new level redoes them while <paramref name="follow"/> (the one 자동 switch, for all
    /// characters) is on; with no raid open at that level the picks stay.</summary>
    public static IReadOnlyList<HomeworkPick> PicksAfter(IReadOnlyList<HomeworkPick> picks, int? was, int now, bool follow, IReadOnlyList<HomeworkRaid> raids)
    {
        var added = picks.Where(IsAdded).ToList();
        bool allocate = was is null ? picks.Count == added.Count : was != now && follow;
        return allocate && Allocate(now, raids) is { Count: > 0 } picked ? [.. picked.Concat(added).Take(HomeworkCharacter.MaxPicks)] : picks;
    }

    /// <summary>A pick of a raid the user added: it stays through a new level, on top of the automatic ones.</summary>
    public static bool IsAdded(HomeworkPick pick) => pick.Raid.StartsWith(CustomPrefix, StringComparison.Ordinal);

    /// <summary>An added raid from its form: rows without a difficulty or tag are left out, either one names the other when alone;
    /// null without a name or a difficulty.</summary>
    public static HomeworkCustomRaid? NewCustom(string id, string name, IEnumerable<(string Difficulty, string Tag, HomeworkColor Color)> rows, bool auto)
    {
        var levels = rows.Select(r => (Difficulty: r.Difficulty.Trim(), Tag: r.Tag.Trim(), r.Color))
            .Where(r => r.Difficulty.Length > 0 || r.Tag.Length > 0)
            .Select(r => new HomeworkLevel(r.Difficulty.Length > 0 ? r.Difficulty : r.Tag, r.Tag.Length > 0 ? r.Tag : r.Difficulty, r.Color, []))
            .DistinctBy(l => l.Difficulty).ToList();
        return string.IsNullOrWhiteSpace(name) || levels.Count == 0 ? null : new HomeworkCustomRaid(id, name.Trim(), levels, auto);
    }

    public static HomeworkRaid AsRaid(HomeworkCustomRaid custom) => new(custom.Id, custom.Name, [], custom.Levels);

    /// <summary>The shipped raids and the added ones that are whole (settings.json can be edited by hand).</summary>
    public static IReadOnlyList<HomeworkRaid> AllRaids(IReadOnlyList<HomeworkRaid> shipped, IEnumerable<HomeworkCustomRaid> added) =>
        [.. shipped, .. added.Where(c => c is { Id: not null, Name: not null, Levels.Count: > 0 } && c.Id.StartsWith(CustomPrefix, StringComparison.Ordinal)
            && c.Levels.All(l => l is { Difficulty: not null, Tag: not null, Last: not null })).Select(AsRaid)];

    /// <summary>The picks with <paramref name="raid"/> on, at its top difficulty; the same list when it is on already or they are full.</summary>
    public static IReadOnlyList<HomeworkPick> WithRaid(IReadOnlyList<HomeworkPick> picks, HomeworkRaid raid) =>
        picks.Count >= HomeworkCharacter.MaxPicks || picks.Any(p => p.Raid == raid.Id) ? picks : [.. picks, new HomeworkPick(raid.Id, raid.Levels[^1].Difficulty)];

    public const string CustomPrefix = "custom-";

    /// <summary>A level typed on 설정 → 숙제, whole part only: "1729.99" is 1729, never 1730, which it can't enter (user,
    /// 2026-11-08). Null for anything but digits with an optional ".digits", or outside 1000–2000.</summary>
    public static int? TypedLevel(string text)
    {
        string t = text.Trim();
        int dot = t.IndexOf('.');
        string whole = dot < 0 ? t : t[..dot], fraction = dot < 0 ? "" : t[(dot + 1)..];
        return whole.Length > 0 && whole.All(char.IsAsciiDigit) && fraction.All(char.IsAsciiDigit)
            && int.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out int level) && level is >= 1000 and <= 2000 ? level : null;
    }

    /// <summary>The level kept after a read: a level doesn't fall, nor rise more than <see cref="MaxLevelStep"/> at once (a misread
    /// that would stick; a real jump is typed on 설정 → 숙제).</summary>
    public static int LevelFrom(int? stored, int read) =>
        stored is not { } level ? read : read > level && read - level <= MaxLevelStep ? read : level;

    /// <summary>The grid's cards with whose they are (<see cref="CardOrder.Owners"/>: by name, the learned <paramref name="order"/>
    /// placing an unclear one; never the one played, whose card shows 접속 중), the played card to <paramref name="current"/>; only
    /// shown characters with picks.</summary>
    public static IReadOnlyList<(MenuCard Card, HomeworkCharacter Character)> OnGrid(MenuRead read, string? current, IReadOnlyList<HomeworkCharacter> characters,
        IReadOnlyList<string>? order = null)
    {
        HomeworkCharacter? Of(string? name) => characters.FirstOrDefault(c => c.Name == name);
        var cards = CardOrder.Owners(read, current, characters, order ?? []).Select(x => (card: x.Card, c: Of(x.Owner)));
        var playing = read.PlayingCard ?? CardOrder.PlayingCard(read, current, characters, order ?? []);   // 접속 중 not read: its place in the order
        if (playing is not null && current is not null) cards = cards.Append((playing, Of(current)));
        return [.. cards.Where(x => x.c is { Hidden: false, Picks.Count: > 0 }).Select(x => (x.card, x.c!))];
    }

    /// <summary>Whose a grid card is, of <paramref name="characters"/>: its name read exactly; else, of the names near enough
    /// (<see cref="CharacterName.Near"/>), the one whose saved level the card's fits (the same, then a rise of up to
    /// <see cref="MaxLevelStep"/>, all rises alike), then the nearest name. Rises don't rank by size: the look-alike who really
    /// leveled would lose to the other, whose level would then rise and stick (review, 2026-11-08). A level that fits no one (not known, or falling: a misread digit)
    /// doesn't steer: the name decides, as without a level. The small font reads two
    /// names a letter apart alike (both 너무아파요살살 and 너무아파용살살 as "너무Ot파용살살", user 2026-11-08): the level tells
    /// them apart. With no name near, the one character at the card's level whose class doesn't differ (<see cref="AtLevel"/>).
    /// Null when none is near or two fit alike.</summary>
    public static string? CardOwner(MenuCard card, IReadOnlyList<HomeworkCharacter> characters)
    {
        var near = CharacterName.Near(card.Name, characters.Select(c => c.Name));
        if (near.Any(x => x.Distance == 0)) return card.Name;
        var ranked = near.Select(x => (x.Name, Key: (Fit(characters.First(c => c.Name == x.Name).ItemLevel, card.ItemLevel), x.Distance)))
            .OrderBy(x => x.Key).ToList();
        if (ranked.Count == 0) return AtLevel(card, characters);
        return ranked.Count > 1 && ranked[1].Key == ranked[0].Key ? null : ranked[0].Name;
    }

    /// <summary>A card whose name the small font read past any listed one (검지관절브레이커 as "김시관절브래0P1", user 2026-11-08):
    /// the only character saved at its level, the card's class read and the same as the saved one or the saved one unknown. Two
    /// at the level (alts fresh at 1640), or no class read, is no one: a new character could share a level.</summary>
    static string? AtLevel(MenuCard card, IReadOnlyList<HomeworkCharacter> characters)
    {
        if (card.ItemLevel is not { } level || card.ClassName is not { } read) return null;
        var at = characters.Where(c => c.ItemLevel == level).ToList();
        if (at.Count != 1) return null;
        var c = at[0];
        return c.ClassName is null || CharacterName.Nearest(read, [c.ClassName]) is not null ? c.Name : null;
    }

    /// <summary>The card's owner (<see cref="CardOwner"/>) when sure enough to take what the card shows for good, such as its class:
    /// its name read exactly, or its level the owner's saved one with its name near. A near name alone may be a new character read
    /// a letter off; a level alone (<see cref="AtLevel"/>) another character at that level.</summary>
    public static string? SureOwner(MenuCard card, IReadOnlyList<HomeworkCharacter> characters) =>
        CardOwner(card, characters) is { } name
            && (card.Name == name || card.ItemLevel is { } level && characters.First(c => c.Name == name).ItemLevel == level
                && CharacterName.Near(card.Name, [name]).Count > 0) ? name : null;   // the level alone (AtLevel) isn't sure

    /// <summary>How well a card's level fits a saved one, best first: the same 0, a rise within <see cref="MaxLevelStep"/> 1;
    /// anything else 2. Without the card's level all fit alike.</summary>
    internal static int Fit(int? saved, int? card) => (saved, card) switch
    {
        (_, null) => 0,
        ({ } s, { } c) when c == s => 0,
        ({ } s, { } c) when c > s && c - s <= MaxLevelStep => 1,
        _ => NoFit,
    };

    /// <summary><see cref="Fit"/>: the card's level isn't the saved one's.</summary>
    internal const int NoFit = 2;

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/> on the list (homework-auto.plan.md). Onto a listed name the two
    /// merge into that one: its picks first, then the raids only the other had (up to <see cref="HomeworkCharacter.MaxPicks"/>), the
    /// higher level, a class or 직업각인 either had. Either way the character is confirmed. False when <paramref name="from"/> isn't
    /// listed or the names are the same. The caller cleans <paramref name="to"/> (<see cref="CharacterName.Clean"/>).</summary>
    public static bool Rename(HomeworkSettings hw, string from, string to)
    {
        if (from == to || hw.Find(from) is not { } c) return false;
        bool merged = hw.Order.Contains(to);
        hw.Order = [.. hw.Order.Select(n => n != from ? n : merged ? "" : to)];   // the card order keeps one place per name (CardOrder)
        if (hw.Find(to) is not { } target)
        {
            c.Name = to;
            c.FromCard = false;
            return true;
        }
        target.Picks = [.. target.Picks.Concat(c.Picks.Where(p => target.Picks.All(t => t.Raid != p.Raid))).Take(HomeworkCharacter.MaxPicks)];
        target.ItemLevel = c.ItemLevel is { } other && (target.ItemLevel is not { } mine || other > mine) ? other : target.ItemLevel;
        target.ClassName ??= c.ClassName;
        target.Subclass ??= c.Subclass;
        target.Hidden &= c.Hidden;   // shown when either was
        target.FromCard = false;
        hw.Characters.Remove(c);
        return true;
    }

    /// <summary>The clears after a rename: <paramref name="from"/>'s are <paramref name="to"/>'s; of <paramref name="to"/>'s clears of one
    /// raid only the latest is kept (a merge can bring two), so a later toggle can't uncover the older one.</summary>
    public static IReadOnlyList<HomeworkClear> RenameClears(IEnumerable<HomeworkClear> clears, string from, string to)
    {
        var renamed = clears.Select(c => c.Character == from ? c with { Character = to } : c).Distinct().ToList();
        var latest = renamed.Where(c => c.Character == to).GroupBy(c => c.Raid).Select(g => g.MaxBy(c => c.At)!).ToHashSet();
        return [.. renamed.Where(c => c.Character != to || latest.Contains(c))];
    }

    /// <summary>The character a big name is when that character was added from its card's small name under a misread: the nearest
    /// of those (<see cref="CharacterName.Nearest"/>), and only when the grid agrees it is that card. <paramref name="played"/>
    /// (the portrait's name): its card shows 접속 중, so the character's small name must be off the grid; else (the clicked card's
    /// header) the nearest card on the grid must be the character's. A near name with a card of its own is another character.
    /// Null when the big name is listed already or near none of them. Confirmed characters are never renamed this way.</summary>
    public static HomeworkCharacter? ConfirmedBy(string bigName, IReadOnlyList<HomeworkCharacter> characters, IReadOnlyList<string> cardNames, bool played)
    {
        if (characters.Any(c => c.Name == bigName)) return null;
        var fromCard = characters.Where(c => c.FromCard).ToList();
        if (CharacterName.Nearest(bigName, fromCard.Select(c => c.Name)) is not { } name) return null;
        bool gridAgrees = played ? !cardNames.Contains(name) : CharacterName.Nearest(bigName, cardNames) == name;
        return gridAgrees ? fromCard.First(c => c.Name == name) : null;
    }

    /// <summary>A big name read exactly as a character listed from its card: that name is right, so the character is confirmed (big names
    /// snap to it from now on). True when that changed it.</summary>
    public static bool ConfirmName(HomeworkSettings hw, string bigName)
    {
        if (hw.Find(bigName) is not { FromCard: true } c) return false;
        c.FromCard = false;
        return true;
    }

    /// <summary>A grid card's small name when it is a character not listed yet: a name (<see cref="CharacterName.Clean"/>) near no
    /// listed one (<see cref="CharacterName.Nearest"/>, which takes "밝기리" for a listed "밟키리"); else null. The caller votes it
    /// twice alike and lists it <see cref="HomeworkCharacter.FromCard"/>.</summary>
    /// <summary>A grid card's character when not listed yet: no listed character but <paramref name="current"/> owns the card
    /// (<see cref="CardOwner"/>: the played one's card shows 접속 중, not a name), and its name is near none.</summary>
    public static string? NewFromCard(MenuCard card, IReadOnlyList<HomeworkCharacter> characters, string? current) =>
        CardOwner(card, [.. characters.Where(c => c.Name != current)]) is null ? NewFromCard(card.Name, [.. characters.Select(c => c.Name)]) : null;

    /// <summary>The name alone (<see cref="NewFromCard(MenuCard, IReadOnlyList{HomeworkCharacter}, string?)"/> also asks whose card it is).</summary>
    public static string? NewFromCard(string read, IReadOnlyList<string> known) =>
        CharacterName.Clean(read) is { } name && CharacterName.Nearest(name, known) is null && CharacterName.Nearest(name, [PlayingMark]) is null ? name : null;

    /// <summary>What the played card shows where its name would be ("접속 중", spaces dropped): a misread of it is no character.</summary>
    const string PlayingMark = "접속중";

    public const int AutoPicks = 3, MaxLevelStep = 60;

    static int Rank(HomeworkRaid raid, string difficulty) => raid.Levels.ToList().FindIndex(l => l.Difficulty == difficulty);
}
