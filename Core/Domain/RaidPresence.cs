namespace GaugeOverlay.Core.Domain;

/// <summary>Where the player is, from what the game shows (meter-gating.plan.md): the in-game DPS meter (전분) shows from a raid's
/// or guardian raid's entry, at timer 0; the boss HP bar only after the first hit. Raid reads wait for these: the dungeon OCR
/// for <see cref="InDungeon"/>, the boss name, 줄 수 and 암수 for <see cref="BossUp"/>. A boss bar alone also counts as a
/// dungeon, for players who hide the meter. A hidden HUD inside a dungeon (a cutscene hides the HUD and the meter) holds it
/// however long (<see cref="Hidden"/>). Pure.</summary>
public sealed record RaidPresence(TimeSpan? MeterSeen, TimeSpan? BossSeen, TimeSpan? Since, TimeSpan? Kept = null, TimeSpan? OutSeen = null)
{
    /// <summary>How long after the channel box's gold arrow a meter or boss read starts no stay (channel-cue.plan.md): a read
    /// from the last frames inside, or a stale one, never brings the dungeon back.</summary>
    public static readonly TimeSpan OutBlock = TimeSpan.FromSeconds(2);
    /// <summary>How long a dungeon holds without the meter or a boss bar: a cutscene, the inventory over the meter, a loading.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(60);
    /// <summary>How long the boss counts as up after its bar was last read.</summary>
    public static readonly TimeSpan BossRecent = TimeSpan.FromSeconds(10);
    /// <summary>The longest a hidden HUD holds a dungeon after the meter or a boss bar was last seen: character select or a
    /// menu over the skill bar must not hold it forever.</summary>
    public static readonly TimeSpan MaxHidden = TimeSpan.FromMinutes(10);
    public static readonly RaidPresence None = new(null, null, null);

    public bool InDungeon(TimeSpan now) => Within(MeterSeen, Hold, now) || Within(BossSeen, Hold, now) || Within(Kept, Hold, now);

    public bool BossUp(TimeSpan now) => Within(BossSeen, BossRecent, now);

    /// <summary>After a step's reads: <paramref name="meter"/> the in-game meter seen, <paramref name="boss"/> a boss bar read.
    /// <see cref="Since"/> is when the current stay in a dungeon began.</summary>
    public RaidPresence Saw(bool meter, bool boss, TimeSpan t) =>
        !meter && !boss || Within(OutSeen, OutBlock, t) ? this : new(meter ? t : MeterSeen, boss ? t : BossSeen, InDungeon(t) ? Since : t, Kept, OutSeen);

    /// <summary>The channel box's gold arrow (two reads in a row): outside any dungeon, so the stay ends at once, with nothing
    /// held (channel-cue.plan.md).</summary>
    public RaidPresence Outside(TimeSpan t) => new(null, null, null, null, t);

    /// <summary>A step with the HUD hidden: inside a dungeon it stays (a cutscene, a loading between gates); outside, nothing.</summary>
    public RaidPresence Hidden(TimeSpan t) =>
        InDungeon(t) && (Within(MeterSeen, MaxHidden, t) || Within(BossSeen, MaxHidden, t)) ? this with { Kept = t } : this;

    /// <summary>A step with another app in front (nothing read, Engine's pause): a dungeon holds however long, and the stay goes on
    /// once the game is back (user, 2026-11-05: an alt-tab ends no fight).</summary>
    public RaidPresence Away(TimeSpan t) => InDungeon(t) ? this with { Kept = t } : this;

    static bool Within(TimeSpan? seen, TimeSpan span, TimeSpan now) => seen is { } s && now - s <= span;
}
