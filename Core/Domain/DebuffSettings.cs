namespace GaugeOverlay.Core.Domain;

/// <summary>디버프 (PLAN §4): the raid whose debuffs are read, and the debuff window's own spot and size (NaN = next to the
/// overlay, until first placed). Your debuff row's size is the game's 버프 크기 (<see cref="BuffSettings.Size"/>); the boss's row
/// under its HP bar has its own (<see cref="BossIconSize"/>).</summary>
public sealed class DebuffSettings : IWindowPlace
{
    public Raid Raid { get; set; } = Raid.없음;
    /// <summary>The game's 보스 디버프 크기 setting (%): how big the boss's effect icons and "X n" counts are, apart from 버프 크기.</summary>
    public int BossIconSize { get; set; } = Vision.Geometry.RefBossSize;
    /// <summary>비었을 때 숨기기 (user, 2026-10-13): a box disappears while nothing is on it; the others keep their place.</summary>
    public bool HideWhenEmpty { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;

    public const int BoxCount = 3;
    /// <summary>Each box's own 너비 and 높이 (내 색, 교체, 번개), times the window's size; read through <see cref="Box"/>.</summary>
    public List<DebuffBox> Boxes { get; set; } = [];

    /// <summary>Box <paramref name="i"/>'s size, added at 1 × 1 the first time.</summary>
    public DebuffBox Box(int i)
    {
        while (Boxes.Count <= i) Boxes.Add(new());
        return Boxes[i];
    }
}

public sealed class DebuffBox
{
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 1;
}
