namespace GaugeOverlay.App.Platform;

/// <summary>Picks the capture backend. Never one that draws a border (PLAN §7 item 1).</summary>
public static class CaptureFactory
{
    /// <summary>Borderless WGC on the window where Windows grants it (Windows 11), otherwise Desktop
    /// Duplication of the monitor the window is on (recreate the source when that changes).</summary>
    public static ICaptureSource Create(IntPtr gameWindow) =>
        (ICaptureSource?)WgcCapture.TryCreate(gameWindow) ?? new DuplicationCapture(GameWindow.MonitorOf(gameWindow));
}
