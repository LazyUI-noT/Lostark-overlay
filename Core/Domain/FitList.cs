namespace GaugeOverlay.Core.Domain;

/// <summary>A row of 스킬창 맞추기's list: which game area, its HUD, and whether the reads use it now.</summary>
public sealed record FitRow(string Label, string Value, bool InUse);

/// <summary>스킬창 맞추기's list (user, 2026-11-10): two rows, this PC's game and 테스트 모드, the one in use marked. Fits saved for
/// other game-area sizes (windowed, a cutscene's bars) stay in Settings.Fits for when that size comes back; the game row shows
/// whichever the game area is now. Pure.</summary>
public static class FitList
{
    /// <param name="areaKey">The engine's game area now (Engine.GameAreaKey): the game's, or the video's in 테스트 모드; null with no window.</param>
    /// <param name="testHud">The HUD 테스트 모드 found on the video (kept in memory only); null before it is found.</param>
    public static IReadOnlyList<FitRow> Rows(Settings s, string? areaKey, bool testMode, double? testHud)
    {
        string? gameKey = !testMode && areaKey is not null ? areaKey : s.DetectedScreen is { } d ? ScreenSummary.AreaKey(d) : null;
        var game = gameKey is null ? new FitRow("게임", "아직 맞춘 적이 없습니다", false)
            : new FitRow($"게임 {gameKey}", GameValue(s, gameKey), InUse: !testMode && areaKey is not null);
        var test = !testMode || areaKey is null ? new FitRow("테스트", "테스트 모드에서 영상마다 맞춥니다 (저장 안 함)", false)
            : new FitRow($"테스트 {areaKey}", testHud is { } hud ? $"HUD {hud:0.#}%" : "찾는 중", InUse: true);
        return [game, test];
    }

    static string GameValue(Settings s, string key)
    {
        string? found = s.Fits.TryGetValue(key, out var fit) ? $"HUD {fit.Hud:0.#}%" : null;
        string? adjusted = s.Adjusts.TryGetValue(key, out var adj) ? $"수동 조정 HUD {adj.Hud:0.#}%" : null;
        return found is null && adjusted is null ? "아직 맞추지 않음" : string.Join(" · ", new[] { found, adjusted }.OfType<string>());
    }
}
