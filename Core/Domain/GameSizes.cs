using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Domain;

/// <summary>The game's % sizes 설정 keeps: the HUD, 버프 크기, 타겟 UI 버프 크기 (보스) and 데미지 폰트 크기.</summary>
public enum GameSize { Hud, Buff, Boss, Font }

/// <summary>값 직접 조정 (user, 2026-11-05): a size picks from the game's presets; with it on, − + step by 1% within FitAdjust's
/// ranges, as a video may show any size. The font has presets only. Pure.</summary>
public static class GameSizes
{
    public static IReadOnlyList<int> Presets(GameSize kind) => kind switch
    {
        GameSize.Hud => Settings.HudSizes,
        GameSize.Font => JudgmentSettings.FontSizes,
        _ => Settings.BuffSizes,
    };

    /// <summary>The whole % a size may take with 값 직접 조정; null for the font.</summary>
    public static (int Min, int Max)? Range(GameSize kind) => kind switch
    {
        GameSize.Hud => ((int)FitAdjust.MinHud, (int)FitAdjust.MaxHud),
        GameSize.Buff => (FitAdjust.MinBuffSize, FitAdjust.MaxBuffSize),
        GameSize.Boss => (FitAdjust.MinBoss, FitAdjust.MaxBoss),
        _ => null,
    };

    /// <summary>One − (<paramref name="direction"/> -1) or + (1): 1% when <paramref name="fine"/> (and the size has a range), else
    /// the next preset on that side of <paramref name="value"/>; it stays put at the end.</summary>
    public static int Step(GameSize kind, int value, int direction, bool fine)
    {
        if (fine && Range(kind) is var (min, max)) return Math.Clamp(value + direction, min, max);
        var presets = Presets(kind);
        return direction > 0 ? presets.Where(p => p > value).DefaultIfEmpty(value).Min() : presets.Where(p => p < value).DefaultIfEmpty(value).Max();
    }

    /// <summary>The preset nearest <paramref name="value"/>, the smaller on a tie.</summary>
    public static int Snap(GameSize kind, int value) => Presets(kind).OrderBy(p => Math.Abs(p - value)).ThenBy(p => p).First();

    /// <summary>What a combo box lists: the presets, and the current value when it is none of them (set by 위치 조정 or 값 직접 조정).</summary>
    public static List<int> Offered(GameSize kind, int value) => [.. Presets(kind).Append(value).Distinct().Order()];
}
