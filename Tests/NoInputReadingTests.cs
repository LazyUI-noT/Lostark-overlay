namespace GaugeOverlay.Tests;

/// <summary>The app reads the screen and nothing else of the game: no keyboard or mouse input beyond its own registered
/// hotkeys, no input sent, no game process memory. This is what the operator was told, so a change must not quietly
/// break it. RegisterHotKey stays allowed: Windows hands the app only the combinations it registered.</summary>
public class NoInputReadingTests
{
    static readonly string[] Banned =
    [
        "RegisterRawInputDevices", "GetRawInputData", "SetWindowsHookEx", "GetAsyncKeyState", "GetKeyState", "GetKeyboardState",
        "SendInput", "keybd_event", "mouse_event", "ReadProcessMemory", "WriteProcessMemory", "OpenProcess", "CreateRemoteThread",
    ];

    static string AppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "App", "GaugeOverlay.App.csproj"))) return Path.Combine(dir.FullName, "App");
        throw new DirectoryNotFoundException("App folder not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void App_source_uses_no_input_reading_or_process_access_api()
    {
        var files = Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);   // a scan of nothing would pass

        var hits = files
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, n: i + 1, line)))
            .SelectMany(x => Banned.Where(b => x.line.Contains(b)).Select(b => $"{Path.GetFileName(x.f)}:{x.n} {b}"))
            .ToList();
        Assert.Empty(hits);
    }
}
