using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>전투력 per character × 직업각인, the highest any lookup gave (lostark-api.plan.md, user 2026-11-08: a trash-mob preset
/// lowers it, so the best is kept; each 직업각인 its own, so 발키리's dealer and healer builds don't mix). Saved as
/// <see cref="Settings.CombatPower"/>: character → 직업각인 without spaces ("" = none) → best. Every change returns a new book.</summary>
public static class CombatPowerBook
{
    /// <summary>The book with <paramref name="profile"/>'s 전투력 kept when it beats what its 직업각인 had.</summary>
    public static Dictionary<string, Dictionary<string, double>> Merge(Dictionary<string, Dictionary<string, double>> book, ApiProfile profile)
    {
        if (profile.CombatPower is not > 0 || profile.Name.Length == 0) return Copy(book);
        if (Of(book, profile.Name, profile.Subclass) >= profile.CombatPower) return Copy(book);
        var next = Copy(book);
        next[profile.Name] = new(next.GetValueOrDefault(profile.Name) ?? []) { [KeyOf(profile.Subclass)] = profile.CombatPower.Value };
        return next;
    }

    /// <summary>The book without one 직업각인's value (설정's 삭제); the character goes once it has none.</summary>
    public static Dictionary<string, Dictionary<string, double>> Delete(Dictionary<string, Dictionary<string, double>> book, string character, string? subclass)
    {
        var next = Copy(book);
        if (!next.TryGetValue(character, out var subs)) return next;
        var left = new Dictionary<string, double>(subs);
        left.Remove(KeyOf(subclass));
        if (left.Count == 0) next.Remove(character);
        else next[character] = left;
        return next;
    }

    /// <summary>One 직업각인's best, for a fight played on it; null when never looked up.</summary>
    public static double? Of(Dictionary<string, Dictionary<string, double>> book, string character, string? subclass) =>
        book.TryGetValue(character, out var subs) && subs.TryGetValue(KeyOf(subclass), out var v) ? v : null;

    /// <summary>What the 게임 메뉴 shows for a character: its highest 직업각인 value with that 직업각인's role (orange 딜러, green
    /// 서포터); null when it has none. <paramref name="roleOf"/> takes the 직업각인 without spaces ("" = none).</summary>
    public static (double Value, Role Role)? Shown(Dictionary<string, Dictionary<string, double>> book, string character, Func<string, Role> roleOf) =>
        book.TryGetValue(character, out var subs) && subs.Count > 0 && subs.MaxBy(s => s.Value) is var best
            ? (best.Value, roleOf(best.Key)) : null;

    /// <summary>The API writes 직업각인 with spaces ("빛의 기사"), classes.json without ("빛의기사").</summary>
    public static string KeyOf(string? subclass) => (subclass ?? "").Replace(" ", "");

    static Dictionary<string, Dictionary<string, double>> Copy(Dictionary<string, Dictionary<string, double>> book) =>
        book.ToDictionary(c => c.Key, c => new Dictionary<string, double>(c.Value));
}
