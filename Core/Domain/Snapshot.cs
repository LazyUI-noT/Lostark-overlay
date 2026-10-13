using System.Collections.Immutable;

namespace GaugeOverlay.Core.Domain;

/// <summary>Everything the overlay draws for one player at one moment. Yours and each party member's
/// use the same type, so one renderer draws both.</summary>
/// <param name="Slots">One state per slot, indexed by <see cref="SlotId"/>.</param>
/// <param name="AwakeningChanged">The "✳ C+V" 초각기 표시 state is showing.</param>
/// <param name="Gauge">아덴 게이지 0..1, or null when the character has none enabled.</param>
/// <param name="Groups">Groups this snapshot carries (a party member may send only some).</param>
/// <param name="AdenSlots">The player's class adenSlots (which of 아덴1/아덴2 form the 아덴스킬 column).</param>
/// <param name="ClassId">The player's class number (ClassInfo.Id), 0 = not set; drawn where a party member's class emblem goes.</param>
public sealed record Snapshot(
    TimeSpan At,
    ImmutableArray<SlotState> Slots,
    bool AwakeningChanged,
    double? Gauge,
    ImmutableHashSet<SlotGroup> Groups,
    ImmutableArray<SlotId> AdenSlots,
    int ClassId = 0)
{
    /// <summary>The buff indicators, indexed by <see cref="BuffKind"/>; yours only (never sent to the party), empty when
    /// the buff row isn't read.</summary>
    public ImmutableArray<BuffState> Buffs { get; init; } = [];

    /// <summary>Your debuffs and the boss's counts (PLAN §4 디버프); yours only, null when no raid is chosen.</summary>
    public DebuffStatus? Debuffs { get; init; }

    /// <summary>단심's cycle (yours only); null when it is off for the active character.</summary>
    public JudgmentState? Judgment { get; init; }

    public SlotState this[SlotId slot] => Slots[(int)slot];

    public static Snapshot Empty(TimeSpan at) => new(
        at,
        [.. SlotInfo.All.Select(SlotState.Unknown)],
        false,
        null,
        [.. Enum.GetValues<SlotGroup>()],
        []);
}

/// <summary>A snapshot with the name to show next to it (empty for yourself), and the box colours the member sent
/// (null until they arrive), used by 파티원 설정 따르기.</summary>
public sealed record Player(string Name, Snapshot Snapshot, Look? Look = null);
