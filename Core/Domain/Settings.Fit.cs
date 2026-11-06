using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 스킬창: 자동 (read off the HP bar), 직접 입력 (typed 해상도 and HUD), 수동 조정 (moved by hand in the debug window).</summary>
public enum FitMode { 자동, 직접입력, 수동조정 }

/// <summary>Where the skill bar's place comes from: 자동, 직접 입력, 수동 조정.</summary>
public sealed partial class Settings
{
    /// <summary>The HUD sizes the game's settings offer.</summary>
    public static readonly int[] HudSizes = [80, 90, 100, 105, 110];

    /// <summary>The 버프 크기 sizes the game's settings offer.</summary>
    public static readonly int[] BuffSizes = [80, 90, 100, 110, 120];

    /// <summary>The offered HUD size nearest <paramref name="hud"/> (a size read off the HP bar is a little off).</summary>
    public static int NearestHud(double hud) => HudSizes.MinBy(h => Math.Abs(h - hud));

    /// <summary>Turns 직접 입력 on from what the last 스킬창 맞추기 found: its 해상도, 21:9 and the HUD size of its fit (else 1920×1080
    /// and 100).</summary>
    public void StartManual()
    {
        var d = DetectedScreen ?? new ManualScreen(1920, 1080, false);
        ManualScreen = d;
        var area = Vision.Geometry.ManualViewport(new PixelRect(0, 0, d.Width, d.Height), d);
        ManualHud = Fits.TryGetValue(Vision.Geometry.ViewportKey(area), out var fit) ? NearestHud(fit.Hud) : 100;
    }

    /// <summary>값 직접 조정 on or off; off snaps the sizes set by 1% to the nearest preset.</summary>
    public void SetFineSizes(bool on)
    {
        FineSizes = on;
        if (on) return;
        if (ManualHud is { } hud) ManualHud = GameSizes.Snap(GameSize.Hud, (int)Math.Round(hud));
        Buffs.Size = GameSizes.Snap(GameSize.Buff, Buffs.Size);
        Debuffs.BossIconSize = GameSizes.Snap(GameSize.Boss, Debuffs.BossIconSize);
    }

    /// <summary>Where the skill bar's place comes from (설정 → 스킬창).</summary>
    [JsonIgnore]
    public FitMode FitMode => UseAdjusts ? FitMode.수동조정 : ManualHud is not null ? FitMode.직접입력 : FitMode.자동;

    public void SetFitMode(FitMode mode)
    {
        switch (mode)
        {
            case FitMode.직접입력: if (ManualHud is null) StartManual(); UseAdjusts = false; break;   // typed values stay
            // 직접 입력's values stay (the game area they give is the one the adjust was made on); otherwise the area is measured.
            case FitMode.수동조정: UseAdjusts = true; break;
            default: (ManualHud, ManualScreen, UseAdjusts) = (null, null, false); break;
        }
    }

    /// <summary>적용: <paramref name="fit"/> is the game area <paramref name="key"/>'s 수동 조정, and that mode is on.</summary>
    public void ApplyAdjust(string key, Fit fit)
    {
        Adjusts[key] = fit;
        SetFitMode(FitMode.수동조정);
    }

    /// <summary>스킬창 다시 맞추기 chose to drop <paramref name="key"/>'s 수동 조정: back to 자동.</summary>
    public void DiscardAdjust(string key)
    {
        Adjusts.Remove(key);
        SetFitMode(FitMode.자동);
    }

    /// <summary>The game area <paramref name="key"/> uses an applied 수동 조정 now.</summary>
    public bool AdjustActive(string? key) => UseAdjusts && key is not null && Adjusts.ContainsKey(key);

    /// <summary>What the engine tries before the fits: a copy of <see cref="Adjusts"/> in 수동 조정, else null.</summary>
    public IReadOnlyDictionary<string, Fit>? ActiveAdjusts() => UseAdjusts ? new Dictionary<string, Fit>(Adjusts) : null;
}
