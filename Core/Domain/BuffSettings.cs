namespace GaugeOverlay.Core.Domain;

/// <summary>The party buff indicators (PLAN §4 버프). Local only: never sent to the party.</summary>
public sealed class BuffSettings : IWindowPlace
{
    /// <summary>버프 막대 따로 두기: the bars leave the plate for a window of their own, placed and sized on its own
    /// (<see cref="Left"/> … <see cref="Height"/>; NaN = next to the overlay, until first placed).</summary>
    public bool Detached { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    /// <summary>The game's 버프 크기 setting (%), usually one of <see cref="Settings.BuffSizes"/> (위치 조정's ± sets any whole %): where the buff and debuff rows sit and
    /// how big their icons are. 스킬창 맞추기 doesn't detect it; 120 is the size the reads are measured best at.</summary>
    public int Size { get; set; } = 120;
    /// <summary>버프 읽을 칸 수: how many boxes of the buff row are read, counted from its fixed right end
    /// (<see cref="Vision.Geometry.MinBuffBoxCount"/>–<see cref="Vision.Geometry.MaxBuffBoxCount"/>). A buff further left than this is
    /// not seen; more boxes cost a little more reading, and boxes past the game's own "+N" icon read nothing.</summary>
    public int Count { get; set; } = Vision.Geometry.BuffBoxCount;
    /// <summary>세로: the bars stand upright side by side instead of lying flat one under another. In the fixed grid they stand
    /// left of the plate, outside it (flat: under the grid, two to a row); in their own window they fill it (flat: one to a
    /// row). A free layout keeps them where they were put.</summary>
    public bool Side { get; set; }
    /// <summary>버프 막대 줄어듦: the bar drains as the buff runs out; off, it stays full while the buff is on.</summary>
    public bool Drain { get; set; } = true;
    /// <summary>비었을 때 숨기기 (user, 2026-10-13): a bar disappears while its buff isn't on; the others keep their place.</summary>
    public bool HideWhenEmpty { get; set; }
    /// <summary>Only the indicators changed from <see cref="Default"/>; read through <see cref="Of"/>.</summary>
    public Dictionary<BuffKind, BuffIndicator> Indicators { get; set; } = [];

    public BuffIndicator Of(BuffKind kind) => Indicators.GetValueOrDefault(kind) ?? Default(kind);

    /// <summary>마지막 1초 깜빡임: whether the bar flashes in its drain direction through its last second; unset, on for
    /// 공증 and 아덴 피증.</summary>
    public bool EndFlash(BuffKind kind) => Of(kind).EndFlash ?? kind is BuffKind.공증 or BuffKind.아덴피증;

    /// <summary>The indicator to change, added from the default the first time.</summary>
    public BuffIndicator Edit(BuffKind kind) =>
        Indicators.TryGetValue(kind, out var i) ? i : Indicators[kind] = Default(kind);

    static BuffIndicator Default(BuffKind kind) => new()
    {
        Color = kind switch { BuffKind.공증 => "#E8262B", BuffKind.아덴피증 => "#FFE600", BuffKind.초각스피증 => "#1BA8E0", _ => "#A04FB0" },
    };
}


public sealed class BuffIndicator
{
    /// <summary>표시. With every indicator off the buff row isn't read at all.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>시간 표시: the "6s" label on the bar.</summary>
    public bool ShowLabel { get; set; } = true;
    public string Color { get; set; } = "";   // BuffSettings.Default sets it
    /// <summary>길이: share (0.1–1) of the bar's length in the layout, along which it drains (upright: its height, from
    /// the bottom; flat: its width, from the left). In a free layout, of its cell (BarSizes.Sized).</summary>
    public double Length { get; set; } = 1;
    /// <summary>두께: the layout's bar thickness × this (0.25–3).</summary>
    public double Thickness { get; set; } = 1;
    /// <summary>마지막 1초 깜빡임; null = the kind's default (<see cref="BuffSettings.EndFlash"/>).</summary>
    public bool? EndFlash { get; set; }
}
