using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Generated bar, ring and orb gauges: empty and full images build the shape, partial fills are read back.</summary>
public class GaugeReaderTests
{
    private static Image Draw(int w, int h, Func<int, int, bool> inGauge, Func<int, int, bool> filled)
    {
        var img = new Image(w, h);
        var rng = new Random(1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * img.Stride + x * 4;
                (byte b, byte g, byte r) = !inGauge(x, y) ? ((byte)rng.Next(40, 90), (byte)rng.Next(40, 90), (byte)rng.Next(40, 90))   // textured background
                    : filled(x, y) ? ((byte)(200 + x % 20), (byte)(60 + y % 30), (byte)40)                                                // blue-ish fill with a gradient
                    : ((byte)30, (byte)30, (byte)30);
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (b, g, r, 255);
            }
        return img;
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.62)]
    [InlineData(1.0)]
    public void Bar_fills_left_to_right(double fill)
    {
        bool In(int x, int y) => x >= 10 && x < 110 && y >= 5 && y < 13;
        var shape = GaugeReader.Build(Draw(120, 18, In, (_, _) => false), Draw(120, 18, In, (_, _) => true));
        var img = Draw(120, 18, In, (x, _) => x - 10 < fill * 100);
        Assert.Equal(fill, GaugeReader.ReadContinuous(img, shape), 0.02);
    }

    /// <summary>바드: four crystals filled one after another, left to right, each from the bottom up.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.125)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    public void Separate_bars_fill_one_after_another(double fill)
    {
        const int Bars = 4, H = 40;
        static bool In(int x, int y) => y >= 5 && y < 5 + H && x % 20 is >= 5 and < 13 && x < 20 * Bars;
        bool Filled(int x, int y) { double done = fill * Bars - x / 20; return done >= 1 || (done > 0 && 5 + H - y <= done * H); }
        var shape = GaugeReader.Piecewise(GaugeReader.Build(Draw(90, 50, In, (_, _) => false), Draw(90, 50, In, (_, _) => true)), 90);
        Assert.Equal(fill, GaugeReader.ReadContinuous(Draw(90, 50, In, Filled), shape), 0.03);
    }

    /// <summary>The part still filling is drawn in another colour (바드's gold before a crystal turns blue).</summary>
    [Fact]
    public void Another_colour_counts_as_filled_when_listed()
    {
        bool In(int x, int y) => x >= 10 && x < 110 && y >= 5 && y < 13;
        var shape = GaugeReader.Build(Draw(120, 18, In, (_, _) => false), Draw(120, 18, In, (_, _) => true));
        var img = Draw(120, 18, In, (x, _) => x - 10 < 30);
        for (int y = 5; y < 13; y++)
            for (int x = 40; x < 70; x++) (img.Bgra[y * img.Stride + x * 4], img.Bgra[y * img.Stride + x * 4 + 1], img.Bgra[y * img.Stride + x * 4 + 2]) = (30, 180, 220);

        Assert.Equal(0.3, GaugeReader.ReadContinuous(img, shape), 0.02);
        Assert.Equal(0.6, GaugeReader.ReadContinuous(img, shape with { Filling = [220 << 16 | 180 << 8 | 30] }), 0.02);
    }

    /// <summary>바드: gold means a crystal is filling, never full. A crystal gold to the top reads just short of its end, so
    /// it is not a full section (no glow, 색 단계 or spend); in its own colour (blue) it is.</summary>
    [Fact]
    public void A_piece_in_the_filling_colour_is_never_full()
    {
        const int Bars = 4, H = 40;
        static bool In(int x, int y) => y >= 5 && y < 5 + H && x % 20 is >= 5 and < 13 && x < 20 * Bars;
        var shape = GaugeReader.Piecewise(GaugeReader.Build(Draw(90, 50, In, (_, _) => false), Draw(90, 50, In, (_, _) => true)), 90)
            with { Filling = [220 << 16 | 180 << 8 | 30] };
        var own = Draw(90, 50, In, (x, _) => x < 20);
        var gold = own.Copy();
        for (int y = 5; y < 5 + H; y++)
            for (int x = 5; x < 13; x++) (gold.Bgra[y * gold.Stride + x * 4], gold.Bgra[y * gold.Stride + x * 4 + 1], gold.Bgra[y * gold.Stride + x * 4 + 2]) = (30, 180, 220);

        Assert.Equal(0.25, GaugeReader.ReadContinuous(own, shape), 1e-9);
        double v = GaugeReader.ReadContinuous(gold, shape);
        Assert.True(v is < 0.25 and > 0.2, $"{v}");
    }

    [Fact]
    public void Vertical_bar_fills_bottom_to_top()
    {
        bool In(int x, int y) => x >= 4 && x < 10 && y >= 2 && y < 62;
        var shape = GaugeReader.Linear(GaugeReader.Build(Draw(14, 64, In, (_, _) => false), Draw(14, 64, In, (_, _) => true)), 90);
        var img = Draw(14, 64, In, (_, y) => y >= 62 - 0.4 * 60);
        Assert.Equal(0.4, GaugeReader.ReadContinuous(img, shape), 0.03);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    public void Ring_fills_clockwise_from_the_top(double fill)
    {
        static double Angle(int x, int y) => ((90 - Math.Atan2(32 - y, x - 32) * 180 / Math.PI) % 360 + 360) % 360;   // clockwise from up
        static bool In(int x, int y) { double r = Math.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32)); return r >= 20 && r <= 28; }
        var shape = GaugeReader.Angular(GaugeReader.Build(Draw(64, 64, In, (_, _) => false), Draw(64, 64, In, (_, _) => true)), 32, 32, 90, true);
        var img = Draw(64, 64, In, (x, y) => Angle(x, y) < fill * 360);
        Assert.Equal(fill, GaugeReader.ReadContinuous(img, shape), 0.02);
    }

    /// <summary>도화가: three rings filled one after another, left to right, each clockwise from the bottom.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void Separate_rings_fill_one_after_another(double fill)
    {
        const int Rings = 3;
        static int Ring(int x, int y) { int k = x / 40; double r = Math.Sqrt((x - 20 - 40 * k) * (x - 20 - 40 * k) + (y - 20) * (y - 20)); return r is >= 10 and <= 16 && k < Rings ? k : -1; }
        static double Angle(int x, int y) => ((270 - Math.Atan2(20 - y, x % 40 - 20) * 180 / Math.PI) % 360 + 360) % 360;   // clockwise from down
        bool Filled(int x, int y) { double done = fill * Rings - x / 40; return done >= 1 || (done > 0 && Angle(x, y) < done * 360); }
        var shape = GaugeReader.Rings(GaugeReader.Build(Draw(120, 40, (x, y) => Ring(x, y) >= 0, (_, _) => false),
            Draw(120, 40, (x, y) => Ring(x, y) >= 0, (_, _) => true)), 270, true);
        Assert.Equal(fill, GaugeReader.ReadContinuous(Draw(120, 40, (x, y) => Ring(x, y) >= 0, Filled), shape), 0.03);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void Orbs_are_counted(int lit)
    {
        static int Orb(int x, int y)
        {
            for (int k = 0; k < 5; k++)
                if ((x - (10 + 20 * k)) * (x - (10 + 20 * k)) + (y - 10) * (y - 10) <= 36) return k;
            return -1;
        }
        var shape = GaugeReader.Build(Draw(100, 20, (x, y) => Orb(x, y) >= 0, (_, _) => false), Draw(100, 20, (x, y) => Orb(x, y) >= 0, (_, _) => true));
        Assert.Equal(5, shape.Pieces);
        var img = Draw(100, 20, (x, y) => Orb(x, y) >= 0, (x, y) => Orb(x, y) < lit);
        Assert.Equal(lit, GaugeReader.ReadPieces(img, shape));
    }
}
