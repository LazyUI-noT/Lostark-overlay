namespace GaugeOverlay.Core.Domain;

/// <summary>The meter's 스킬 사용 횟수 line: the name shown and the casts a minute; null when there is nothing to count from
/// (스킬창 읽기 off, the slot never read, the first seconds), which the window shows as "—", never a stale number.</summary>
public sealed record MeterSkillLine(string Name, double? PerMinute);

/// <summary>My casts in this 전분 record on its timer, for the meter's 스킬 사용 횟수 (apart from FightRecorder: the meter counts with
/// 전투 기록 off too). Only while the timer runs: a cast in a stop has no second of its own. A new record starts it over.</summary>
public sealed class CastLog
{
    /// <summary>Under this many seconds a rate says nothing: one early cast would read as a dozen a minute.</summary>
    public const int MinSeconds = 10;

    readonly List<(int T, SlotId Slot)> _casts = [];
    readonly HashSet<SlotId> _seen = [];
    IReadOnlyList<SlotPhase>? _was;
    int _fight;

    /// <summary>A slot going ready → cooldown is a cast.</summary>
    public static bool IsCast(SlotPhase was, SlotPhase now) => was == SlotPhase.Ready && now is SlotPhase.Cooldown or SlotPhase.LongCooldown;

    public void Observe(IReadOnlyList<SlotState> slots, FightClockState clock, TimeSpan now)
    {
        if (clock.Fight != _fight)
        {
            _fight = clock.Fight;
            _casts.Clear();
            _seen.Clear();
        }
        bool running = clock.Timer > 0 && !clock.Frozen && now - clock.MovedAt < FightClock.FrozenAfter;
        var phases = slots.Select(s => s.Phase).ToList();
        for (int i = 0; i < phases.Count; i++)
        {
            if (phases[i] != SlotPhase.Unknown) _seen.Add(slots[i].Slot);
            if (running && _was?.Count == phases.Count && IsCast(_was[i], phases[i])) _casts.Add((clock.Timer!.Value, slots[i].Slot));
        }
        _was = phases;
    }

    /// <summary>Casts a minute at <paramref name="timer"/>: over the whole record, or over the last minute
    /// (<paramref name="recent"/>; a shorter record over what there is). Null: see <see cref="MeterSkillLine"/>.</summary>
    public double? PerMinute(SlotId slot, int? timer, bool recent)
    {
        if (timer is not { } t || t < MinSeconds || !_seen.Contains(slot)) return null;
        double window = recent ? Math.Min(t, MeterHistory.RecentSpan.TotalSeconds) : t;
        return _casts.Count(c => c.Slot == slot && c.T > t - window) * 60 / window;
    }
}

/// <summary>What the meter window's 스킬 사용 횟수 line says. Pure.</summary>
public static class MeterSkill
{
    /// <summary>The line for <paramref name="key"/> (the character played); null with no skill picked for it.
    /// <paramref name="keyName"/>: the slot's key as the user set it, the name until they give one.</summary>
    public static MeterSkillLine? Line(Settings s, string? key, MeterHistory history, int? timer, Func<SlotId, string> keyName)
    {
        var w = ClassProfiles.WindowsOf(s, key);
        if (w.MeterSkill is not { } slot) return null;
        bool recent = s.Meter.SkillFollowsRange && s.Meter.Range == MeterRange.최근1분;
        return new(w.NameOf(slot) ?? keyName(slot), s.Overlay.SkillRead ? history.Casts.PerMinute(slot, timer, recent) : null);
    }
}
