namespace GaugeOverlay.Core.Domain;

/// <summary>Whether to search the whole game area for the 전분 panel (JeonbunFinder). With no spot yet: always, so a first
/// fight is found from its first second (waiting lost 1막's first 5 s). A spot that stopped reading for
/// <see cref="AfterMisses"/> meter reads (moved, or no panel shown): only in a dungeon (its panel read) or with a boss HP bar
/// read in the last <see cref="BossRecent"/>; without this, 지옥 and the open world searched every 3 s forever. The caller
/// paces it. Pure.</summary>
public static class PanelSearch
{
    public const int AfterMisses = 3;
    public static readonly TimeSpan BossRecent = TimeSpan.FromSeconds(10);

    public static bool Due(bool hasSpot, int misses, TimeSpan? bossSeen, bool inDungeon, TimeSpan now) =>
        !hasSpot || misses >= AfterMisses && (inDungeon || bossSeen is { } b && now - b <= BossRecent);
}
