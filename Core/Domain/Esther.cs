namespace GaugeOverlay.Core.Domain;

/// <summary>One planned 에스더 use: its name (카단 3칸 …), damage to the boss (억), and when it goes in: at this bar count, or for
/// a ??? gate at this HP %.</summary>
public sealed record EstherUse(string Name, double Damage, int At);

/// <summary>An 에스더 plan (설정 → 레이드, per gate; 종막 EX for now, reusable for any raid): what has gone in by a point. A use
/// counts once the count or % is at or under its <see cref="EstherUse.At"/>. Pure.</summary>
public static class Esther
{
    /// <summary>The difficulty the skills' damage is written at; another difficulty of the same gate scales it by the HP ratio
    /// (the damage is a share of the boss's HP; a skill in another raid has its own share).</summary>
    public const string Basis = "EX 나이트메어";

    /// <summary>The damage (억) of the uses whose point <paramref name="now"/> (bar count or HP %) has reached.</summary>
    public static double Applied(IReadOnlyList<EstherUse> plan, double now) => plan.Where(u => u.Damage > 0 && now <= u.At).Sum(u => u.Damage);

    /// <summary>The whole plan's damage (억): a gate fought to the end used all of it.</summary>
    public static double Total(IReadOnlyList<EstherUse> plan) => plan.Where(u => u.Damage > 0).Sum(u => u.Damage);
}
