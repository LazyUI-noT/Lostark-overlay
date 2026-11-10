using System.Globalization;

namespace GaugeOverlay.Core.Domain;

/// <summary>A fight's 전투력 and item level (lostark-api.plan.md phase 5): set when it is saved, shown on the timeline's top bar.
/// Pure.</summary>
public static class RecordPower
{
    /// <summary>The fight with its character's best 전투력 for the 직업각인 played (<see cref="CombatPowerBook.Of"/>) and the item
    /// level the last lookup gave (<paramref name="levels"/>: character → level); null where nothing was looked up.</summary>
    public static FightRecord Stamp(FightRecord fight, Dictionary<string, Dictionary<string, double>> book, IReadOnlyDictionary<string, double> levels) =>
        fight.Character is not { } who ? fight
            : fight with { CombatPower = CombatPowerBook.Of(book, who, fight.MySubclass), ItemLevel = levels.TryGetValue(who, out var level) ? level : null };

    /// <summary>A fight as 편집 left it: its 전투력 and item level dropped once the character or the 직업각인 is another (they
    /// belonged to the one played; the colour would follow the new 직업각인's role).</summary>
    public static FightRecord Edited(FightRecord before, FightRecord after) =>
        after.Character == before.Character && CombatPowerBook.KeyOf(after.MySubclass) == CombatPowerBook.KeyOf(before.MySubclass)
            ? after : after with { CombatPower = null, ItemLevel = null };

    /// <summary>"1791.66 · 전투력 5,004" (either part alone when only it was saved); null for a record with neither.</summary>
    public static string? Label(FightRecord r)
    {
        string[] parts = [.. new[]
        {
            r.ItemLevel?.ToString("0.00", CultureInfo.InvariantCulture),
            r.CombatPower is { } power ? $"전투력 {MenuPower.Text(power)}" : null,
        }.OfType<string>()];
        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }
}
