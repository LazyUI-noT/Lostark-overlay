using System.Globalization;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>One 전투력 on the 게임 메뉴: drawn from <see cref="Line"/>'s left, centred on its height (1080p reference pixels,
/// placed on screen by <see cref="Geometry.MenuArea"/>), in its role's colour.</summary>
public sealed record PowerTag(PixelRect Line, string Text, Role Role);

/// <summary>전투력 on the 게임 메뉴 (lostark-api.plan.md phase 4): each grid card's owner and the played character (beside the
/// portrait) show their best 직업각인 value (<see cref="CombatPowerBook.Shown"/>), right of the item level on the same line.
/// Measured on reference/homework_project/tags/goal-tags.png at 1080p; the menu follows the game area's height and centre only
/// (<see cref="Geometry.MenuArea"/>), not HUD 크기 or the other in-game sizes. Pure.</summary>
public static class MenuPower
{
    /// <summary>A card's item level digits end this far right of the card (x 965 on a card at 846).</summary>
    const double CardLevelEnd = 119;
    const int Gap = 7;

    /// <summary>The tags for an open menu: one per card whose owner has a value, and the portrait's for <paramref name="current"/>.
    /// <paramref name="owners"/> is <see cref="CardOrder.Owners"/>; <paramref name="roleOf"/> takes a 직업각인 without spaces.</summary>
    public static IReadOnlyList<PowerTag> Tags(MenuRead read, string? current, IReadOnlyList<(MenuCard Card, string? Owner)> owners,
        Dictionary<string, Dictionary<string, double>> book, Func<string, Role> roleOf)
    {
        if (!read.Open) return [];
        var tags = new List<PowerTag>();
        foreach (var (card, owner) in owners)
        {
            if (owner is null || CombatPowerBook.Shown(book, owner, roleOf) is not { } shown) continue;
            var line = new PixelRect((int)Math.Round(card.X + CardLevelEnd) + Gap, (int)Math.Round(card.Y), 0,
                (int)Math.Round(card.Bottom - GameMenu.CardBelow - card.Y));
            tags.Add(new(line, Text(shown.Value), shown.Role));
        }
        if (current is not null && CombatPowerBook.Shown(book, current, roleOf) is { } mine)
        {
            var level = Geometry.RefMenuPortraitLevel;
            tags.Add(new(new(level.X + level.Width + Gap, level.Y, 0, level.Height), Text(mine.Value), mine.Role));
        }
        return tags;
    }

    /// <summary>The role of the class that has 직업각인 <paramref name="key"/> (no spaces); 딜러 when none has it.</summary>
    public static Role RoleIn(IReadOnlyList<ClassInfo> classes, string key) =>
        classes.FirstOrDefault(c => c.Subclasses.ContainsKey(key)) is { } c ? c.RoleOf(key) : Role.딜러;

    /// <summary>Whole points with thousands commas, cut not rounded ("5,004.50" → "5,004"): short enough for the card's line.</summary>
    public static string Text(double value) => Math.Floor(value).ToString("N0", CultureInfo.InvariantCulture);
}
