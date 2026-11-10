using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>설정 → 초당 그리기 횟수: one redraw rate for every moving part (10–60, default 30); a slow fade alone at most 15.</summary>
public sealed class DrawStepsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "GaugeOverlayTests", Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "settings.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Theory]
    [InlineData("""{"RecordKeep":20}""", 30)]          // a file from before the setting
    [InlineData("""{"DrawsPerSecond":0}""", 10)]       // hand edits are brought back in range
    [InlineData("""{"DrawsPerSecond":999}""", 60)]
    [InlineData("""{"DrawsPerSecond":45}""", 45)]
    public void A_settings_file_loads_its_rate_kept_between_10_and_60(string json, int kept)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, json);
        Assert.Equal(kept, new SettingsStore(FilePath).Load(out _).DrawsPerSecond);
    }

    [Fact]
    public void Defaults_to_30_and_new_settings_start_there() =>
        Assert.Equal((30, 30), (DrawSteps.Default, new Settings().DrawsPerSecond));

    [Theory]
    [InlineData(1, 10)]
    [InlineData(45, 45)]
    [InlineData(240, 60)]
    public void Values_are_kept_between_10_and_60(int given, int kept) => Assert.Equal(kept, DrawSteps.Clamp(given));

    [Theory]
    [InlineData(30, false, 1000.0 / 30)]
    [InlineData(60, false, 1000.0 / 60)]
    [InlineData(500, false, 1000.0 / 60)]
    [InlineData(30, true, 1000.0 / 15)]
    [InlineData(10, true, 1000.0 / 10)]
    public void Frame_time_follows_the_rate_and_a_fade_alone_stays_at_15_or_less(int perSecond, bool fadeOnly, double ms) =>
        Assert.Equal(ms, DrawSteps.FrameMs(perSecond, fadeOnly), 6);
}
