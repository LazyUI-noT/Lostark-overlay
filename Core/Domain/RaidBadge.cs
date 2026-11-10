namespace GaugeOverlay.Core.Domain;

/// <summary>A difficulty's capsule colour (settings-reorg.plan.md 11). EX shares its base's. 매칭 and 1–3단계 have their own
/// (user, 2026-11-09); new tiers go at the end (saved by name).</summary>
public enum DifficultyTier { Normal, Single, Hard, Nightmare, Unknown, Matching, Step1, Step2, Step3 }

/// <summary>What 설정 shows in front of a raid's difficulty (settings-reorg.plan.md 11, 12): the capsule's colour and the raid's
/// logo, cut from the boss-title banners (App/Assets/raids/&lt;logo&gt;.png). Pure.</summary>
public static class RaidBadge
{
    /// <summary>The capsule colour for <paramref name="difficulty"/> (as the dungeon panel or raids.json writes it).</summary>
    public static DifficultyTier Tier(string? difficulty)
    {
        string d = (difficulty ?? "").Trim();
        if (d.StartsWith("EX ", StringComparison.OrdinalIgnoreCase)) d = d[3..];
        return d switch
        {
            "노말" => DifficultyTier.Normal,
            "매칭" => DifficultyTier.Matching,
            "싱글" => DifficultyTier.Single,
            "하드" => DifficultyTier.Hard,
            "나이트메어" => DifficultyTier.Nightmare,
            "1단계" => DifficultyTier.Step1,   // 구원의 종탑's 1–3단계
            "2단계" => DifficultyTier.Step2,
            "3단계" => DifficultyTier.Step3,
            _ when d.EndsWith("단계") => DifficultyTier.Normal,   // a stage the game adds later: as before
            _ => DifficultyTier.Unknown,
        };
    }

    /// <summary>A difficulty as 설정 shows it: 나이트메어 as 나메, so every capsule is about as long (user, 2026-11-10). Saved
    /// names, raids.json and the game's text keep the full word.</summary>
    public static string Label(string difficulty) => difficulty.Replace("나이트메어", "나메", StringComparison.Ordinal);

    static readonly (string Prefix, string Logo)[] Logos =
        [("벨가르딘", "belgardin"), ("1막", "act1"), ("2막", "act2"), ("3막", "act3"), ("4막", "act4"), ("종막", "final"),
         ("세르카", "serka"), ("고통의 마녀", "serka"),   // 숙제 names the raid, its gates the boss's title (raids.json)
         ("구원의 종탑", "belltower")];   // serka: a broom, belltower: a bell, act4: a halberd: the user's own set (2026-11-10), recoloured to the
         // shipped logos' orange and softened to their 48 px look (source and cut-out: reference/raid-logos-2026-11-10/)

    /// <summary>The logo of the raid <paramref name="raid"/> names (a raid, 숙제's raid name or a gate's label: "3막 · 3관문"); null
    /// for a raid without one.</summary>
    public static string? Logo(string? raid) =>
        raid is null ? null : Logos.FirstOrDefault(l => raid.TrimStart().StartsWith(l.Prefix, StringComparison.Ordinal)).Logo;
}
