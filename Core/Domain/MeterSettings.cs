using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>What a row's number is: 딜지분/찬조 %, 피해량 (억), or DPS (억).</summary>
public enum MeterValue { 지분, 피해량, DPS }

/// <summary>설정 → DPS 미터기's 표시 방식 (spec § C Display).</summary>
public enum MeterLook { 행, 세로막대, 선그래프, 한줄막대, 파티비교 }

/// <summary>설정 → DPS 미터기 (spec § C): on or off, 내 피해량 보내기 (also in 파티 → 보내기·받기), what is shown and how, colours
/// (null: the 파티원 colours), the window's place, and the 전분 panel's spot per game-area size (Geometry.ViewportKey).</summary>
public sealed class MeterSettings : IWindowPlace
{
    public bool On { get; set; }
    /// <summary>내 피해량 보내기: my figures go to the party once a second (off by default); off, the party's aren't shown
    /// either (only someone who shares sees the others).</summary>
    public bool Send { get; set; }
    public MeterScope Scope { get; set; } = MeterScope.파티;
    public MeterValue Value { get; set; } = MeterValue.지분;
    public MeterLook Look { get; set; } = MeterLook.선그래프;
    /// <summary>The meter window (off: the meter still reads, for 전투 기록 and 내 피해량 보내기).</summary>
    public bool Show { get; set; } = true;
    /// <summary>전투 기록: fights the meter follows are recorded.</summary>
    public bool Record { get; set; } = true;
    /// <summary>The DPS graph under the rows.</summary>
    public bool Graph { get; set; } = true;
    /// <summary>버프 적용률: how much of my damage was dealt under 공증 / 아덴 피증 / 초각스 피증, as three bars.</summary>
    public bool BuffBars { get; set; } = true;
    /// <summary>The 서포터 · 찬조 section.</summary>
    public bool Support { get; set; } = true;
    /// <summary>The 기타 row (boss damage no shown dealer covers).</summary>
    public bool Other { get; set; } = true;
    /// <summary>파티 비교 in % (else 피해량).</summary>
    public bool CompareAsShare { get; set; } = true;
    public string? MyColor { get; set; }
    public string? DealerColor { get; set; }
    public string? SupportColor { get; set; }
    public string? OtherColor { get; set; }
    public string Background { get; set; } = "#CC101418";
    public double Opacity { get; set; } = 1.0;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    /// <summary>The 전분 panel's spot on screen per game-area size ("W×H").</summary>
    public Dictionary<string, PixelRect> Panels { get; set; } = [];

    /// <summary>The look drawn: 파티 비교 needs both parties, so with 나 or 파티 it is 행.</summary>
    [JsonIgnore]
    public MeterLook ShownLook => Look == MeterLook.파티비교 && Scope is MeterScope.나 or MeterScope.파티 ? MeterLook.행 : Look;

    /// <summary>Load-time repairs (SettingsStore.Normalize): hand edits.</summary>
    internal void Normalize()
    {
        Panels ??= [];
        foreach (var (key, r) in Panels.ToList())
            if (r.Width <= 0 || r.Height <= 0) Panels.Remove(key);
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.1, 1) : 1;
    }
}
