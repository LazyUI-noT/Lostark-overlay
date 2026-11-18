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
        string? gameKey = GameKey(s, areaKey, testMode);
        var game = gameKey is null ? new FitRow("게임", "아직 맞춘 적이 없습니다", false)
            : new FitRow($"게임 {gameKey}", GameValue(s, gameKey), InUse: !testMode && areaKey is not null);
        var test = !testMode || areaKey is null ? new FitRow("테스트", "테스트 모드에서 영상마다 맞춥니다 (저장 안 함)", false)
            : new FitRow($"테스트 {areaKey}", testHud is { } hud ? $"HUD {hud:0.#}%" : "찾는 중", InUse: true);
        return [game, test];
    }

    public const string FirstFit = "스킬창 맞추기";
    public const string Refit = "스킬창 다시 맞추기";

    /// <summary>The fit button's name: 다시 only once the game row's screen has a fit or a 수동 조정, so a player who never
    /// fitted isn't told to fit "again" (ui-audit-2026-11-18). The notices that name the button use the same rule.</summary>
    public static string FitButton(Settings s, string? areaKey, bool testMode) => HasFit(s, areaKey, testMode) ? Refit : FirstFit;

    /// <summary>The game's screen has a saved fit or 수동 조정: the fit button says 다시, and 시작하기 opened again starts as
    /// fitted (its closing notice then skips how to fit later).</summary>
    public static bool HasFit(Settings s, string? areaKey, bool testMode) =>
        GameKey(s, areaKey, testMode) is { } key && (s.Fits.ContainsKey(key) || s.Adjusts.ContainsKey(key));

    static string? GameKey(Settings s, string? areaKey, bool testMode) =>
        !testMode && areaKey is not null ? areaKey : s.DetectedScreen is { } d ? ScreenSummary.AreaKey(d) : null;

    static string GameValue(Settings s, string key)
    {
        string? found = s.Fits.TryGetValue(key, out var fit) ? $"HUD {fit.Hud:0.#}%" : null;
        string? adjusted = s.Adjusts.TryGetValue(key, out var adj) ? $"수동 조정 HUD {adj.Hud:0.#}%" : null;
        return found is null && adjusted is null ? "아직 맞추지 않음" : string.Join(" · ", new[] { found, adjusted }.OfType<string>());
    }
}
