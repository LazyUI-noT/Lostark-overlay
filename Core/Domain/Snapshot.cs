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

    /// <summary>암흑수류탄 on the boss (amsu.design.md); null while its switch is off. The 암수 창 counts down from it (Amsu.View).</summary>
    public AmsuState? Amsu { get; init; }
    /// <summary>The 그림자 레이드 gimmick's boss HP (ShadowHp) while it is up and its raid's 기믹 체력 switch is on; null otherwise.</summary>
    public ShadowView? Shadow { get; init; }
    /// <summary>대난투 is up (its number read, ShadowRead), whatever the display switch: no boss bar or name then, which
    /// 전투 기록 and 숙제's fallback must not take for a gone bar.</summary>
    public bool Daenantu { get; init; }
    /// <summary>The boss died in 대난투 (ShadowHp.Dead, until a real bar is read again): 숙제's fallback counts it as a kill.</summary>
    public bool DaenantuKilled { get; init; }
    /// <summary>보스 체력 쉽게 보기: the boss's HP beside its name while its bar is read (BossHpLabel); null otherwise.</summary>
    public BossHpView? BossHp { get; init; }

    /// <summary>The 줄 수 창 (설정 → 레이드, yours only); null while it isn't shown.</summary>
    public RaidStatus? Raid { get; init; }

    /// <summary>감지 중: each reader's ✓ ? ! by its 표시 map box (<see cref="DetectBoard"/>); yours only, never sent.</summary>
    public System.Collections.Immutable.ImmutableDictionary<string, Detection> Detect { get; init; } = DetectBoard.Empty;
    /// <summary>Where each reader looks (screen pixels), for 감지; the game area is <see cref="DetectView"/>.</summary>
    public System.Collections.Immutable.ImmutableDictionary<string, PixelRect> DetectAreas { get; init; } = System.Collections.Immutable.ImmutableDictionary<string, PixelRect>.Empty;
    public Viewport DetectView { get; init; }
    /// <summary>Each reader's picture from the moment its mark turned ✓ or ! (in memory only).</summary>
    public System.Collections.Immutable.ImmutableDictionary<string, DetectPicture> DetectPictures { get; init; } = System.Collections.Immutable.ImmutableDictionary<string, DetectPicture>.Empty;
    /// <summary>The 감지 page's Live box, captured 2/s while it is on; null otherwise.</summary>
    public DetectPicture? DetectLive { get; init; }

    /// <summary>The DPS 미터기's own reads (yours; the party's figures travel as MeterSample); null while it is off.</summary>
    public MeterStatus? Meter { get; init; }

    /// <summary>Whether the 암수 창 counts down: the "N초" is read where it is 100% on screen (Amsu.Timed with the game area); null
    /// where no game is read (previews, demos), which then follow the 보스 디버프 크기 alone.</summary>
    public bool? AmsuTimed { get; init; }

    /// <summary>The dungeon the top-left panel last showed (read while the meter is on); null while unknown or the meter is off.</summary>
    public DungeonInfo? Dungeon { get; init; }

    /// <summary>Where the player is (the in-game meter, the boss bar: RaidPresence); local only, not sent to the party.</summary>
    public RaidPresence Presence { get; init; } = RaidPresence.None;

    /// <summary>What the meter window draws (built by App from mine and the party's, once a second); null while it is off.</summary>
    public MeterView? MeterView { get; init; }

    public SlotState this[SlotId slot] => Slots[(int)slot];

    public static Snapshot Empty(TimeSpan at) => new(
        at,
        [.. SlotInfo.All.Select(SlotState.Unknown)],
        false,
        null,
        [.. Enum.GetValues<SlotGroup>()],
        []);
}

/// <summary>The 줄 수 창: the gate whose alerts apply, the bar count shown (null: not read yet, or lost), whether an alert glows;
/// <paramref name="Text"/>, shown instead of the count: a ??? gate's estimated HP left ("≈62% 1.2조").</summary>
public sealed record RaidStatus(Data.RaidGate? Gate, int? Count, bool Glow, string? Text = null);

/// <summary>A snapshot with the name to show next to it (empty for yourself), and the box colours the member sent
/// (null until they arrive), used by 파티원 설정 따르기.</summary>
public sealed record Player(string Name, Snapshot Snapshot, Look? Look = null);
