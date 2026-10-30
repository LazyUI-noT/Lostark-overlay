namespace GaugeOverlay.App.Platform;

/// <summary>Picks the capture backend. Never one that draws a border (PLAN §7 item 1).</summary>
public static class CaptureFactory
{
    /// <summary>Borderless WGC on the window where Windows grants it (Windows 11), otherwise Desktop
    /// Duplication of the monitor the window is on (recreate the source when that changes).</summary>
    /// <param name="test">테스트 모드: WGC on the video's window even with Windows 10's border (it reads the window while covered).</param>
    public static ICaptureSource Create(IntPtr gameWindow, bool test = false) =>
        (ICaptureSource?)WgcCapture.TryCreate(gameWindow, allowBorder: test) ?? new DuplicationCapture(GameWindow.MonitorOf(gameWindow));
}
