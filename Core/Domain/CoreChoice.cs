using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>The cores of the game's build: 해 and 달 for a dealer, 별 for a supporter (<see cref="Role"/>).</summary>
public enum CoreType { 해, 달, 별 }

/// <summary>A 코어 빌드 as the user set it for one 직업각인: <see cref="Apply"/> (off by default; off keeps the numbers and
/// applies nothing), each core's level 1–3, and <see cref="Slots"/>, the slot the user keeps each trigger skill in (the app never
/// learns skill names). A value: change a copy with <c>with</c> / <see cref="WithSlot"/>, never <see cref="Slots"/> in place.
/// Equality compares the slot entries. Read from the settings file leniently (<see cref="CoreChoiceJson"/>).</summary>
[JsonConverter(typeof(CoreChoiceJson))]
public sealed record CoreChoice
{
    public const int MinLevel = 1, MaxLevel = 3;

    public bool Apply { get; init; }
    public int Sun { get; init; } = MinLevel;
    public int Moon { get; init; } = MinLevel;
    public int Star { get; init; } = MinLevel;
    /// <summary>Trigger skill name (as in Profiles/cores) → the slot it sits in.</summary>
    public IReadOnlyDictionary<string, SlotId> Slots { get; init; } = new Dictionary<string, SlotId>();

    public int Of(CoreType core) => core switch
    {
        CoreType.해 => Sun,
        CoreType.달 => Moon,
        CoreType.별 => Star,
        _ => throw new ArgumentOutOfRangeException(nameof(core)),
    };

    /// <summary>A copy with <paramref name="core"/> at <paramref name="level"/> (not clamped: <see cref="Settings.SetCore"/> does that).</summary>
    public CoreChoice With(CoreType core, int level) => core switch
    {
        CoreType.해 => this with { Sun = level },
        CoreType.달 => this with { Moon = level },
        CoreType.별 => this with { Star = level },
        _ => throw new ArgumentOutOfRangeException(nameof(core)),
    };

    /// <summary>A copy with <paramref name="trigger"/> in <paramref name="slot"/>, or with no slot for it when null.</summary>
    public CoreChoice WithSlot(string trigger, SlotId? slot)
    {
        var slots = new Dictionary<string, SlotId>(Slots);
        if (slot is { } s) slots[trigger] = s; else slots.Remove(trigger);
        return this with { Slots = slots };
    }

    /// <summary>The slots a trigger skill can be in: 일반1–8 (what 설정 offers; a hand-edited other slot is dropped).</summary>
    public static bool IsTriggerSlot(SlotId slot) => slot is >= SlotId.일반1 and <= SlotId.일반8;

    /// <summary>A copy with the levels inside 1–3 and only 일반 slots (<see cref="IsTriggerSlot"/>).</summary>
    public CoreChoice Clamped() => this with
    {
        Sun = Math.Clamp(Sun, MinLevel, MaxLevel),
        Moon = Math.Clamp(Moon, MinLevel, MaxLevel),
        Star = Math.Clamp(Star, MinLevel, MaxLevel),
        Slots = (Slots ?? new Dictionary<string, SlotId>()).Where(p => IsTriggerSlot(p.Value)).ToDictionary(p => p.Key, p => p.Value),
    };

    public bool Equals(CoreChoice? other) =>
        other is not null && (Apply, Sun, Moon, Star) == (other.Apply, other.Sun, other.Moon, other.Star)
        && Slots.Count == other.Slots.Count && Slots.All(p => other.Slots.TryGetValue(p.Key, out var v) && v == p.Value);

    public override int GetHashCode() =>
        Slots.Aggregate(HashCode.Combine(Apply, Sun, Moon, Star), (h, p) => h ^ HashCode.Combine(p.Key, p.Value));
}

/// <summary>Reads a <see cref="CoreChoice"/> from settings.json without letting one hand-edited value reset the whole file: a field of
/// the wrong type (a float or text level, a non-boolean Apply) keeps its default, a slot that is not an exact <see cref="SlotId"/> name
/// (or not text) is dropped, a Slots that is not an object is empty, and a choice that is not an object is read as null (the
/// loader drops it). Written in the same shape the default serialiser would give, with slot names as text.</summary>
internal sealed class CoreChoiceJson : JsonConverter<CoreChoice>
{
    public override CoreChoice? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);   // a document, not a node: a repeated key is not an error
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        int Level(string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : CoreChoice.MinLevel;
        var slots = new Dictionary<string, SlotId>();
        if (root.TryGetProperty("Slots", out var s) && s.ValueKind == JsonValueKind.Object)
            foreach (var p in s.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } name
                    && Enum.GetNames<SlotId>().Contains(name, StringComparer.Ordinal) && CoreChoice.IsTriggerSlot(Enum.Parse<SlotId>(name)))
                    slots[p.Name] = Enum.Parse<SlotId>(name);
        return new CoreChoice
        {
            Apply = root.TryGetProperty("Apply", out var a) && a.ValueKind == JsonValueKind.True,
            Sun = Level("Sun"), Moon = Level("Moon"), Star = Level("Star"),
            Slots = slots,
        };
    }

    public override void Write(Utf8JsonWriter writer, CoreChoice value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("Apply", value.Apply);
        writer.WriteNumber("Sun", value.Sun);
        writer.WriteNumber("Moon", value.Moon);
        writer.WriteNumber("Star", value.Star);
        writer.WriteStartObject("Slots");
        foreach (var (trigger, slot) in value.Slots.Where(p => CoreChoice.IsTriggerSlot(p.Value)))
            writer.WriteString(trigger, slot.ToString());
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
