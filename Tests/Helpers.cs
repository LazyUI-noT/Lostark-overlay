using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Shared by every test class (imported with a global using static in the project file).</summary>
internal static class Helpers
{
    public static readonly Viewport Fhd = new(0, 0, 1920, 1080);

    public static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    public static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(d.FullName, "GaugeOverlay.sln"))) d = d.Parent!;
        return d.FullName;
    }
}
