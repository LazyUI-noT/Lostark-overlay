using System.Collections.Immutable;

namespace GaugeOverlay.Core.Domain;

/// <summary>감지 중's mark (pyosi-map.plan.md): ✓ read for certain, ? nothing found yet, ! should have read but didn't.</summary>
public enum DetectMark { Waiting, Concrete, Failed }

/// <summary>One reader's 감지 중: its mark since <paramref name="At"/>, since when it has found without a break, since when it has
/// missed where it should read, and why it failed.</summary>
public sealed record Detection(DetectMark Mark, TimeSpan At, TimeSpan? FoundSince = null, TimeSpan? MissSince = null, string? Why = null, int Sightings = 0);

/// <summary>What the engine knows each step, from reads it already makes: no capture of its own.</summary>
public sealed record DetectInputs(bool Seen, bool HasFit, bool? HudVisible, bool AnyBuff, bool GaugeRead, bool InDungeon, bool BossUp,
    bool PanelFound, bool BossHpRead, bool DungeonKnown, bool AmsuReadable, bool AmsuFound, bool JudgmentSeen, bool CharacterNamed = false);

/// <summary>A picture of a reader's area plus a margin (<see cref="DetectBoard.Around"/>): kept from the moment its mark
/// turned (✓ or !), or Live. Shown once on the 감지 page, never compared as a reference (detection.md).</summary>
public sealed record DetectPicture(Image Picture, PixelRect Area, TimeSpan At);

/// <summary>Every reader's 감지 중, keyed by its 표시 map box (<see cref="FeatureMap.Boxes"/>). Pure: the engine steps it with
/// <see cref="DetectInputs"/> and puts it in the snapshot.</summary>
public static class DetectBoard
{
    /// <summary>How long a reader must keep finding before it is certain. Time, not steps: most inputs are held reads (boss HP
    /// 1.5 s, the dungeon name, buffs), so one read stays true for many steps and counting steps would trust one misread.</summary>
    public static readonly TimeSpan AgreeFor = TimeSpan.FromSeconds(1);
    /// <summary>Or this many separate sightings (found, lost, found again): short buffs like 공증 (0.2–0.4 s) never last a second.</summary>
    public const int SightingsNeeded = 2;
    /// <summary>How long a reader may miss where it should read before it turns red.</summary>
    public static readonly TimeSpan FailAfter = TimeSpan.FromSeconds(5);

    public static readonly ImmutableDictionary<string, Detection> Empty = ImmutableDictionary<string, Detection>.Empty;

    /// <summary>One step for every reader in <paramref name="on"/>; readers switched off drop out. While the game isn't seen
    /// (another app over it), marks hold.</summary>
    public static ImmutableDictionary<string, Detection> Step(ImmutableDictionary<string, Detection> board, DetectInputs i, IReadOnlySet<string> on, TimeSpan t)
    {
        var next = Empty.ToBuilder();
        foreach (var (id, seen) in Observe(i))
        {
            if (!on.Contains(id)) continue;
            var d = board.GetValueOrDefault(id) ?? new Detection(DetectMark.Waiting, t);
            next[id] = i.Seen ? Step(d, seen, t) : d;
        }
        return next.ToImmutable();
    }

    /// <summary>What each reader shows this step: found, whether it should have read, why not, and whether a miss fails at once.</summary>
    static IEnumerable<(string Id, (bool Found, bool Should, string? Why, bool AtOnce) Seen)> Observe(DetectInputs i)
    {
        yield return ("skill", i.HasFit ? (i.HudVisible == true, false, null, false) : (false, true, "스킬창 맞추기가 필요합니다.", true));
        yield return ("buff", (i.AnyBuff, false, null, false));
        // ponytail: aden counts any number read while the HUD shows (the reader keeps its last value); a reader stuck on a
        // wrong number stays ✓. Feed a "changed lately" signal if that ever shows up.
        yield return ("aden", (i.GaugeRead && i.HudVisible == true, false, null, false));
        yield return ("g-jeonbun", (i.PanelFound, i.InDungeon && i.BossUp, "보스와 싸우는데 전투 분석기가 안 보입니다. 게임에서 전투 분석기를 열어 두세요.", false));
        yield return ("boss", (i.BossHpRead, false, null, false));
        yield return ("dungeon", (i.DungeonKnown, false, null, false));
        yield return ("amsu", i.AmsuReadable ? (i.AmsuFound, false, null, false)
            : (false, true, "보스 디버프 아이콘이 너무 작아 읽을 수 없습니다. 게임의 타겟 UI 버프 크기를 키우세요.", true));
        yield return ("judg", (i.JudgmentSeen, false, null, false));
        yield return ("char", (i.CharacterNamed, false, null, false));   // only while the ESC menu is open: ✓ then stays
    }

    static Detection Step(Detection d, (bool Found, bool Should, string? Why, bool AtOnce) o, TimeSpan t)
    {
        if (o.Found)
        {
            var foundSince = d.FoundSince ?? t;
            int sightings = d.FoundSince is null ? d.Sightings + 1 : d.Sightings;
            var mark = t - foundSince >= AgreeFor || sightings >= SightingsNeeded || d.Mark == DetectMark.Concrete ? DetectMark.Concrete : DetectMark.Waiting;
            return d with { Mark = mark, At = mark != d.Mark ? t : d.At, FoundSince = foundSince, MissSince = null, Why = null, Sightings = sightings };
        }
        if (!o.Should)   // nothing to read here: ✓ stays, ! goes back to ? (it no longer should read)
            return d.Mark == DetectMark.Failed ? new Detection(DetectMark.Waiting, t) : d with { FoundSince = null, MissSince = null };
        var since = d.MissSince ?? t;
        bool fail = o.AtOnce || t - since >= FailAfter;
        return fail && d.Mark != DetectMark.Failed ? d with { Mark = DetectMark.Failed, At = t, FoundSince = null, MissSince = since, Why = o.Why }
            : d with { FoundSince = null, MissSince = since };
    }

    /// <summary>The margin round a reader's area in its pictures: a quarter of its size, at least <see cref="MinMargin"/> px.</summary>
    public const int MinMargin = 16;

    /// <summary><paramref name="area"/> grown by its margin, kept inside the game area; null when nothing of it is.</summary>
    public static PixelRect? Around(PixelRect area, Viewport vp)
    {
        int mx = Math.Max(MinMargin, area.Width / 4), my = Math.Max(MinMargin, area.Height / 4);
        return new PixelRect(area.X - mx, area.Y - my, area.Width + 2 * mx, area.Height + 2 * my).Intersect(new PixelRect(vp.X, vp.Y, vp.Width, vp.Height));
    }

    /// <summary>The readers whose picture to keep this step: their mark just turned ✓ or !.</summary>
    public static IEnumerable<string> Moments(IReadOnlyDictionary<string, Detection> before, IReadOnlyDictionary<string, Detection> after) =>
        after.Where(kv => kv.Value.Mark != DetectMark.Waiting && before.GetValueOrDefault(kv.Key)?.Mark != kv.Value.Mark).Select(kv => kv.Key);

    /// <summary>The map's lights on <paramref name="id"/>'s lines: from ✓ on, also with nothing to read now or 설정 in front of the game (user, 2026-11-06).</summary>
    public static bool Flowing(IReadOnlyDictionary<string, Detection> board, string id) => board.GetValueOrDefault(id)?.Mark == DetectMark.Concrete;
}
