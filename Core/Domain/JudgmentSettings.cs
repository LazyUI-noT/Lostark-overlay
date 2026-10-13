namespace GaugeOverlay.Core.Domain;

/// <summary>감지 방식: what 단심 is read from. Both: either starts the cycle, and the icon's timer, when seen, dates it.</summary>
public enum JudgmentSource { Both, Icon, Text }

/// <summary>표시 방식: the bar (6 s, then 24 s), or the game's 심판 icon only while it is ready.</summary>
public enum JudgmentDisplay { Bar, ReadyIcon }

/// <summary>설정 → 기타 → 단심 (user, 2026-10-13): on per "클래스 · 직업각인" (off by default; nothing is read for it then),
/// how it is read, and its own window (<see cref="IWindowPlace"/>: NaN = next to the overlay until first placed).</summary>
public sealed class JudgmentSettings : IWindowPlace
{
    /// <summary>The characters (<see cref="Settings.CharacterKey"/>) it is on for; read through <see cref="On"/>.</summary>
    public HashSet<string> Characters { get; set; } = [];
    public JudgmentSource Source { get; set; } = JudgmentSource.Both;
    /// <summary>The game's 데미지 폰트 크기 (%), one of <see cref="FontSizes"/>: how tall the text is.</summary>
    public int FontSize { get; set; } = 100;
    public JudgmentDisplay Display { get; set; } = JudgmentDisplay.Bar;
    /// <summary>세로: the bar stands upright and drains downward, as the buff bars' 세로.</summary>
    public bool Side { get; set; }
    /// <summary>비었을 때 숨기기: the bar disappears while 심판 is ready (nothing to count).</summary>
    public bool HideWhenEmpty { get; set; }
    /// <summary>준비 아이콘: a dim icon with the seconds to ready through the cooldown, instead of nothing.</summary>
    public bool IconCooldown { get; set; }
    /// <summary>The bar's 심판 part, and its cooldown part unless <see cref="OneColor"/> (then a dim <see cref="Color"/>).</summary>
    public string Color { get; set; } = "#3FA9F5";
    public string CooldownColor { get; set; } = "#8A93A3";
    public bool OneColor { get; set; }
    /// <summary>시간 표시: seconds left on the bar (심판's, then to ready).</summary>
    public bool ShowLabel { get; set; } = true;
    /// <summary>불투명도, 0.2–1.</summary>
    public double Opacity { get; set; } = 1;

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    public static readonly int[] FontSizes = [75, 100, 150, 200, 300];
    public const double MinOpacity = 0.2;

    public bool On(string? characterKey) => characterKey is not null && Characters.Contains(characterKey);

    public void Set(string characterKey, bool on)
    {
        if (on) Characters.Add(characterKey); else Characters.Remove(characterKey);
    }
}
