using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>What the box at the top left of 설정's 내 오버레이 canvas says about the screen: the game window's 해상도, its ratio,
/// and the HUD the reads use with where it came from.</summary>
public sealed record ScreenFacts(string Resolution, string Ratio, string Hud);

/// <summary>The screen and HUD for the canvas box (user, 2026-11-01): 직접 입력's values, else what the last 스킬창 맞추기 found
/// (marked "(마지막)" while no game window is found). Pure.</summary>
public static class ScreenSummary
{
    const string None = "—";

    public static ScreenFacts Of(Settings s, bool gameRunning)
    {
        var screen = s.ManualHud is not null && s.ManualScreen is { } typed ? typed : s.DetectedScreen;
        if (screen is null) return new(None, None, Hud(s, null));
        return new($"{screen.Width}×{screen.Height}" + (gameRunning ? "" : " (마지막)"), Ratio(screen), Hud(s, screen));
    }

    /// <summary>What a folded 게임 설정 box of the 표시 map shows (pyosi-map.plan.md): 해상도 · 비율, the HUD with where it came from, or a
    /// size in %; null for any other box.</summary>
    public static string? Value(Settings s, bool gameRunning, string boxId)
    {
        var facts = Of(s, gameRunning);
        return boxId switch
        {
            "g-res" => facts.Resolution == None ? None : $"{facts.Resolution} · {facts.Ratio}",
            "g-hud" => facts.Hud,
            "g-buff" => $"{s.Buffs.Size}%",
            "g-debuff" => $"{s.Debuffs.BossIconSize}%",
            "g-font" => $"{s.Judgment.FontSize}%",
            _ => null,
        };
    }

    /// <summary>The ratio's usual name: the forced 21:9 when the game bars a narrower screen to 64:27, else the window's own.</summary>
    public static string Ratio(ManualScreen screen)
    {
        double r = (double)screen.Width / screen.Height;
        if (screen.Wide && r < Geometry.ForcedWide - 0.02) return "21:9 (강제)";   // as wide or wider: the game follows the screen
        return Math.Abs(r - 16.0 / 9) < 0.02 ? "16:9"
            : Math.Abs(r - 1.6) < 0.02 ? "16:10"
            : r is >= 2.3 and < 2.45 ? "21:9"   // 2560×1080 (2.37) and 3440×1440 (2.39) alike
            : r is >= 3.4 and < 3.7 ? "32:9"
            : $"{r:0.00}:1";
    }

    /// <summary>The game-area key (Fits, Adjusts) of a full-screen game on <paramref name="screen"/>: the forced 21:9's bars taken off.</summary>
    public static string AreaKey(ManualScreen screen) =>
        Geometry.ViewportKey(Geometry.ManualViewport(new PixelRect(0, 0, screen.Width, screen.Height), screen));

    static string Hud(Settings s, ManualScreen? screen)
    {
        // As the engine picks (FitMode): 수동 조정 first, which keeps 직접 입력's values, then 직접 입력, then the fit found.
        string? key = screen is null ? null : AreaKey(screen);
        if (s.UseAdjusts && key is not null && s.Adjusts.TryGetValue(key, out var adjusted)) return $"{adjusted.Hud:0.#}% (수동 조정)";
        if (s.ManualHud is { } typed) return $"{typed:0.#}% (직접)";
        if (key is null) return None;
        return s.Fits.TryGetValue(key, out var fit) ? $"{fit.Hud:0.#}% (자동)" : None;
    }
}
