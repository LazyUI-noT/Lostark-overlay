namespace GaugeOverlay.Core.Domain;

/// <summary>Where the overlays stand and whether reads go on with another app in front.</summary>
public sealed class ScreenSettings
{
    /// <summary>표시 화면: "" = the windows stay where they were put, <see cref="DisplayMap.GameDisplay"/> = the game's display,
    /// else a device name.</summary>
    public string Display { get; set; } = "";
    /// <summary>창 밖에서도 읽기: reads and overlays go on with another app in front while the game is on screen
    /// (<see cref="BackgroundRead"/>). Off: nothing is read then (outside 테스트 모드), as a video alt-tabbed to would be.</summary>
    public bool ReadBehind { get; set; }
}
