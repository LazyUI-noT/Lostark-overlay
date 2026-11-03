using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The bomb match at a slot (AmsuReader.Around): each box the tries look at is computed once (amsu-match, 2026-11-04).
/// It must answer exactly as the old one did: five tries at x−2…x+2, each its own ±2 px match.</summary>
public class AmsuMatchTests
{
    static readonly Viewport Wide = new(0, 0, 2560, 1080);
    static readonly Fit Hud100 = new(100);
    const int ClipRowY = 34;
    static double Scale => Geometry.BossScale(Wide, Hud100, 100);

    static Image Reference(string name)
    {
        var s = Geometry.BossRowSearch(Wide, Hud100, 100);
        var strip = Fixture.Load(name).Frames[0].Crop(new PixelRect(s.X - 1000, s.Y - 100, s.Width, s.Height));
        return DebuffReader.ToReference(strip, Scale);
    }

    /// <summary>The old match: <see cref="AmsuReader.Best"/> at x−2…x+2, the best score (the first on a tie).</summary>
    static (Bomb Kind, double Score, int X)? Old(Image r, int x, int y) =>
        Enumerable.Range(x - 2, 5)
            .Select(at => AmsuReader.Best(r, new PixelRect(at, y, DebuffReader.BossIconSize, DebuffReader.BossIconSize)) is { } h ? (h.Kind, h.Score, X: at) : ((Bomb Kind, double Score, int X)?)null)
            .OfType<(Bomb Kind, double Score, int X)>().DefaultIfEmpty().MaxBy(h => h.Score) is { Score: > 0 } hit ? hit : null;

    [Theory]
    [InlineData("amsu_15", true)]
    [InlineData("amsu_25", true)]
    [InlineData("amsu_35", false)]
    [InlineData("amsu_45", true)]
    [InlineData("amsu_55", true)]
    public void The_match_answers_as_the_old_one_at_every_place_on_the_row(string fixture, bool bomb)
    {
        var r = Reference(fixture);
        int y = (int)Math.Round(ClipRowY / Scale), hits = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int x = 0; x + DebuffReader.BossIconSize <= r.Width; x++)
            {
                var old = Old(r, x, y + dy);
                Assert.Equal(old, AmsuReader.Around(r, x, y + dy));
                if (old is not null) hits++;
            }
        Assert.Equal(bomb, hits > 0);   // where a bomb is (all but amsu_35), real matches were compared, not only empty answers
    }
}
