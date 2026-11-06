using System.Collections.Concurrent;
using System.Diagnostics;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App;

/// <summary>The fight's skill map (skill-icons.plan.md): the engine sends ready crops of the slots <see cref="FightSkills"/> still
/// wants (about one a second per unknown slot, one every 10 s per known one), one background thread matches them in order, and the
/// record takes the result when it is saved (<see cref="TakeSkills"/>), which starts the next fight's map.</summary>
public partial class App
{
    FightSkills _skills = FightSkills.For(null);   // replaced whole by the matching thread; read by the engine thread
    readonly ConcurrentDictionary<SlotId, byte> _skillQueued = new();
    readonly BlockingCollection<(SlotId Slot, Image Crop, TimeSpan T)> _skillWork = new(boundedCapacity: 32);
    volatile string? _playingClass;   // set on the UI thread (Draw), read by the engine and skills threads

    /// <summary>The class played now (null in 테스트 모드, where the crops vote one).</summary>
    void PlayingClass(string? cls) => _playingClass = cls;

    void WireSkills(Engine engine)
    {
        engine.SkillWanted = (slot, t) => !_skillQueued.ContainsKey(slot) && SkillMap().Wanted(slot, t);
        engine.SkillCropSeen += (slot, crop, t) =>
        {
            if (_skillQueued.TryAdd(slot, 0) && !_skillWork.TryAdd((slot, crop, t))) _skillQueued.TryRemove(slot, out _);
        };
        new Thread(SkillLoop) { IsBackground = true, Name = "skills", Priority = ThreadPriority.BelowNormal }.Start();
    }

    /// <summary>The map for the class played now (FightSkills.Current), swapped in when the class changed.</summary>
    FightSkills SkillMap()
    {
        var s = Volatile.Read(ref _skills);
        var now = s.Current(_playingClass);
        return ReferenceEquals(now, s) || Interlocked.CompareExchange(ref _skills, now, s) == s ? now : Volatile.Read(ref _skills);
    }

    void SkillLoop()
    {
        foreach (var (slot, crop, t) in _skillWork.GetConsumingEnumerable())
        {
            try
            {
                var before = SkillMap();
                var after = Perf.RunCpu("스킬 인식", () => before.Add(slot, IconPrint.Framings(crop), SkillLibrary.Shipped, t));
                // A map taken (saved) meanwhile is not brought back.
                if (Interlocked.CompareExchange(ref _skills, after, before) == before && after.Skill(slot) != before.Skill(slot))
                    Trace.WriteLine($"[skills] {slot} → {after.Skill(slot)?.Name ?? "unknown"}" + (after.Voted && !before.Voted ? $" (class {after.Class})" : ""));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Counted as a try, or the engine would send the slot again every frame.
                var s = SkillMap();
                Interlocked.CompareExchange(ref _skills, s.Failed(slot, t), s);
                Trace.WriteLine($"[skills] {slot}: {ex.Message}");
            }
            finally { _skillQueued.TryRemove(slot, out _); }
        }
    }

    /// <summary>The fight with its skill map, and a new map for the next fight.</summary>
    FightRecord TakeSkills(FightRecord fight)
    {
        var map = Interlocked.Exchange(ref _skills, FightSkills.For(_playingClass));
        var named = map.Apply(fight);
        Trace.WriteLine($"[skills] fight: {named.Skills?.Count ?? 0} known, {named.Unsure?.Count ?? 0} unsure" + (named.ProposedClass is { } p ? $", proposed {p}" : ""));
        return named;
    }
}
