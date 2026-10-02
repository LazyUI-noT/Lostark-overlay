using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Loads a raw BGRA crop sequence made by tools/make-fixtures.ps1.</summary>
public sealed record Fixture(string Name, string Slot, double Hud, int[] Rect, IReadOnlyList<Image> Frames, IReadOnlyList<TimeSpan> Pts)
{
    private sealed record Meta(string Source, string Slot, double Hud, int[] Rect, int Width, int Height, double[] Pts);

    public static Fixture Load(string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(Path.Combine(dir, name + ".json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        byte[] raw = File.ReadAllBytes(Path.Combine(dir, name + ".bgra"));
        int size = meta.Width * meta.Height * 4;
        var frames = Enumerable.Range(0, raw.Length / size)
            .Select(i => new Image(meta.Width, meta.Height, raw.AsSpan(i * size, size).ToArray()))
            .ToList();
        var pts = meta.Pts.Length == frames.Count
            ? meta.Pts.Select(TimeSpan.FromSeconds).ToList()
            : frames.Select((_, i) => TimeSpan.FromSeconds(i / 60.0)).ToList();
        return new Fixture(name, meta.Slot, meta.Hud, meta.Rect, frames, pts);
    }
}
