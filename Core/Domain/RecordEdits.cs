namespace GaugeOverlay.Core.Domain;

/// <summary>전투 기록 편집's unapplied check (user, 2026-11-08: 적용 lights up while there is a change not applied yet). Pure. Only the
/// fields the editor changes count: 적용 would store a different fight exactly when one of them differs from the saved fight.</summary>
public static class RecordEdits
{
    /// <param name="saved">The fight as stored.</param>
    /// <param name="edited">The fight as the editor's values would store it (what 적용 writes).</param>
    public static bool Unapplied(FightRecord saved, FightRecord edited) =>
        Name(saved.Gate) != Name(edited.Gate)
        || Name(saved.Difficulty) != Name(edited.Difficulty)
        || Name(saved.Character) != Name(edited.Character)
        || saved.Cleared != edited.Cleared
        || saved.MyClass != edited.MyClass || saved.MySubclass != edited.MySubclass || saved.MyClassName != edited.MyClassName
        || saved.ProposedClass != edited.ProposedClass
        || !saved.Members.SequenceEqual(edited.Members)
        || !SameSlots(saved.Skills, edited.Skills, (a, b) => a == b)
        || !SameSlots(saved.Unsure, edited.Unsure, (a, b) => a.SequenceEqual(b));

    /// <summary>A blank box is no name: 적용 stores it as none.</summary>
    static string? Name(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Two slot maps hold the same slots, each with the same value; a missing map is an empty one.</summary>
    static bool SameSlots<T>(IReadOnlyDictionary<SlotId, T>? a, IReadOnlyDictionary<SlotId, T>? b, Func<T, T, bool> same)
    {
        a ??= new Dictionary<SlotId, T>();
        b ??= new Dictionary<SlotId, T>();
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var other) && same(kv.Value, other));
    }
}
