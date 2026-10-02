using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class EmblemMatcherTests
{
    private static readonly PixelRect Emblem = Geometry.RefEmblem;

    // emblem_* fixtures are cut at (904, 922) from their screenshot.
    private static Image Crop(string shot, int dx = 0, int dy = 0) =>
        VisionReplayTests.Crop(Fixture.Load("emblem_" + shot).Frames[0], Emblem.X - 904 + dx, Emblem.Y - 922 + dy, Emblem.Width, Emblem.Height);

    [Theory]
    [InlineData("cd")]
    [InlineData("mainfocus")]
    public void Same_class_emblem_matches_across_screenshots(string shot) =>
        Assert.InRange(EmblemMatcher.Similarity(Crop("Nocd"), Crop(shot)), 0.9, 1.0);

    [Fact]
    public void Shifted_crop_does_not_match() =>
        Assert.InRange(EmblemMatcher.Similarity(Crop("Nocd"), Crop("cd", 8, -8)), 0, 0.6);

    [Fact]
    public void Other_part_of_the_bar_does_not_match()
    {
        var other = VisionReplayTests.Crop(Fixture.Load("band_Nocd").Frames[0], 100, 20, Emblem.Width, Emblem.Height);
        Assert.InRange(EmblemMatcher.Similarity(Crop("Nocd"), other), 0, 0.6);
    }
}
