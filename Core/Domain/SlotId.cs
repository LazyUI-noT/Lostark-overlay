namespace GaugeOverlay.Core.Domain;

/// <summary>Skill-bar slots by fixed position. Names match the IDs used in Profiles/*.json.</summary>
public enum SlotId
{
    각성기, 초각스,
    일반1, 일반2, 일반3, 일반4,
    일반5, 일반6, 일반7, 일반8,
    아덴1, 아덴2,
}

/// <summary>Overlay groups the user can show/hide, colour, and choose to send to the party.</summary>
public enum SlotGroup { 각성기, 초각스, 일반스킬, 아덴스킬 }

public static class SlotInfo
{
    public static readonly SlotId[] All = Enum.GetValues<SlotId>();

    /// <summary>Game default key per slot; pre-filled on first start.</summary>
    public static string DefaultKey(SlotId slot) => slot switch
    {
        SlotId.각성기 => "V",
        SlotId.초각스 => "T",
        SlotId.일반1 => "Q", SlotId.일반2 => "W", SlotId.일반3 => "E", SlotId.일반4 => "R",
        SlotId.일반5 => "A", SlotId.일반6 => "S", SlotId.일반7 => "D", SlotId.일반8 => "F",
        SlotId.아덴1 => "Z",
        SlotId.아덴2 => "X",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>Group of a slot. 아덴 slots belong to 아덴스킬 only if the class lists them in adenSlots;
    /// otherwise they are treated as normal skills.</summary>
    public static SlotGroup GroupOf(SlotId slot, IReadOnlyCollection<SlotId> adenSlots) => slot switch
    {
        SlotId.각성기 => SlotGroup.각성기,
        SlotId.초각스 => SlotGroup.초각스,
        SlotId.아덴1 or SlotId.아덴2 when adenSlots.Contains(slot) => SlotGroup.아덴스킬,
        _ => SlotGroup.일반스킬,
    };
}
