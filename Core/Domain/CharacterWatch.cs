namespace GaugeOverlay.Core.Domain;

/// <summary>The character being played became <paramref name="Name"/> (null: 캐릭터 불명). <paramref name="BackfillFrom"/>: the
/// 불명 fights since then were this character's (it was being played all along, just not known yet).</summary>
public sealed record CharacterChange(string? Name, DateTime? BackfillFrom);

/// <summary>Who is being played (homework.plan.md). 캐릭터 불명 at start (first login, the app started mid-game) until the game
/// menu names the character. A switch from the 다른 캐릭터 접속 grid is the picked card's name with "잠시만 기다려주세요.", then a
/// loading screen within <see cref="SwitchWait"/>. Pure: each step returns the next watch and what changed.</summary>
public sealed record CharacterWatch(string? Current, DateTime UnknownSince, string? Pending, TimeSpan PendingAt)
{
    public static readonly TimeSpan SwitchWait = TimeSpan.FromSeconds(60);

    public static CharacterWatch Start(DateTime wall) => new(null, wall, null, TimeSpan.MinValue);

    /// <summary>A menu read: the name under the portrait is who is played; a picked card with the waiting text is a switch
    /// about to happen.</summary>
    public (CharacterWatch Watch, CharacterChange? Change) Menu(MenuRead read, IReadOnlyList<string> known, TimeSpan t)
    {
        var watch = this;
        CharacterChange? change = null;
        if (read.Current is { } seen && CharacterName.Snap(seen, known) is var name && name != Current)
        {
            change = new CharacterChange(name, Current is null ? UnknownSince : null);
            watch = watch with { Current = name };
        }
        if (read.Waiting && read.Picked is { } picked && CharacterName.Snap(picked, known) is var next && next != watch.Current)
            watch = watch with { Pending = next, PendingAt = t };
        return (watch, change);
    }

    /// <summary>A loading screen ended: the switch waiting for it happens; any other pick is dropped.</summary>
    public (CharacterWatch Watch, CharacterChange? Change) Loaded(TimeSpan t, DateTime wall)
    {
        if (Pending is { } name && t - PendingAt <= SwitchWait)
            return (new CharacterWatch(name, wall, null, TimeSpan.MinValue), new CharacterChange(name, null));
        return (this with { Pending = null }, null);
    }

    /// <summary>The app's class and 직업각인 now. Away from the current character's class: 캐릭터 불명 (a switch the menu didn't
    /// show). While 불명: the one character on the list with this class and 직업각인, when no other could be it.</summary>
    public (CharacterWatch Watch, CharacterChange? Change) Playing(string? className, string? subclass, IReadOnlyList<HomeworkCharacter> roster, DateTime wall)
    {
        if (Current is { } current)
        {
            bool away = className is not null && roster.FirstOrDefault(c => c.Name == current)?.ClassName is { } known && known != className;
            return away ? (new CharacterWatch(null, wall, null, TimeSpan.MinValue), new CharacterChange(null, null)) : (this, null);
        }
        if (className is null || subclass is null) return (this, null);
        if (roster.Any(c => c.ClassName is null)) return (this, null);   // one not learned yet could be it
        var sameClass = roster.Where(c => c.ClassName == className).ToList();
        if (sameClass.Any(c => c.Subclass is null)) return (this, null);   // likewise
        var match = sameClass.Where(c => c.Subclass == subclass).ToList();
        if (match.Count != 1) return (this, null);
        return (this with { Current = match[0].Name }, new CharacterChange(match[0].Name, UnknownSince));
    }
}
