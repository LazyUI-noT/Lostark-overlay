namespace GaugeOverlay.Core.Domain;

/// <summary>What a cast draws on the 전투 기록 timeline: this PC's saved icon of the slot, the skill's icon downloaded from
/// Smilegate's CDN, a name chip, or the key letter.</summary>
public enum SkillFaceKind { Saved, Library, Chip, Key }

/// <summary>A cast's face (skill-icons.plan.md): a fight that knows the slot's skill draws the saved icon only when that shows the
/// same skill (<paramref name="savedName"/>, the saved icon identified), else the downloaded icon, else a chip with its name; a
/// slot it doesn't know (and fights from before 0.19) draws the saved icon as before, else the key. Pure.</summary>
public static class SkillFaces
{
    public static SkillFaceKind Of(FightRecord r, SlotId slot, bool hasSaved, string? savedName, bool downloaded)
    {
        if (r.Skills?.GetValueOrDefault(slot) is not { } skill) return hasSaved ? SkillFaceKind.Saved : SkillFaceKind.Key;
        if (hasSaved && savedName == skill.Name) return SkillFaceKind.Saved;
        return downloaded ? SkillFaceKind.Library : SkillFaceKind.Chip;
    }
}
