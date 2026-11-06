using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class IconPrintTests
{
    /// <summary>A made-up icon: a few soft coloured blobs on a dark ground, the same for one seed at any size.</summary>
    static Image Art(int seed, int size)
    {
        var rng = new Random(seed);
        var blobs = Enumerable.Range(0, 9).Select(_ => (X: rng.NextDouble(), Y: rng.NextDouble(), R: 0.08 + rng.NextDouble() * 0.15,
            B: rng.Next(256), G: rng.Next(256), Rd: rng.Next(256))).ToArray();
        var img = new Image(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double b = 20, g = 20, r = 20, u = (x + 0.5) / size, v = (y + 0.5) / size;
                foreach (var o in blobs)
                {
                    double w = Math.Exp(-((u - o.X) * (u - o.X) + (v - o.Y) * (v - o.Y)) / (o.R * o.R));
                    b += o.B * w; g += o.G * w; r += o.Rd * w;
                }
                int i = y * img.Stride + x * 4;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2], img.Bgra[i + 3]) = (Clip(b), Clip(g), Clip(r), 255);
            }
        return img;
    }

    static byte Clip(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>What the game draws over a skill bar icon: a key letter in the bottom middle and a frame.</summary>
    static Image InGame(Image art)
    {
        var img = art.Copy();
        int s = img.Width;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                bool frame = x < s / 16 + 1 || y < s / 16 + 1 || x >= s - s / 16 - 1 || y >= s - s / 16 - 1;
                bool letter = y > s * 0.74 && y < s * 0.95 && x > s * 0.4 && x < s * 0.6;
                if (!frame && !letter) continue;
                int i = y * img.Stride + x * 4;
                byte c = letter ? (byte)250 : (byte)180;
                (img.Bgra[i], img.Bgra[i + 1], img.Bgra[i + 2]) = (c, c, c);
            }
        return img;
    }

    static Image Dim(Image img, double f)
    {
        var d = img.Copy();
        for (int i = 0; i < d.Bgra.Length; i += 4)
            for (int c = 0; c < 3; c++) d.Bgra[i + c] = Clip(d.Bgra[i + c] * f);
        return d;
    }

    [Fact]
    public void An_in_game_icon_with_its_key_letter_and_frame_matches_its_reference()
    {
        var reference = IconPrint.Of(Art(1, 64));
        Assert.True(reference.Similarity(IconPrint.Of(InGame(Art(1, 64)))) > 0.95);
    }

    [Fact]
    public void A_smaller_dimmer_capture_still_matches()
    {
        var reference = IconPrint.Of(Art(2, 64));
        Assert.True(reference.Similarity(IconPrint.Of(Dim(InGame(Art(2, 41)), 0.7))) > 0.9);
    }

    [Fact]
    public void Another_icon_does_not_match()
    {
        var reference = IconPrint.Of(Art(3, 64));
        foreach (int other in new[] { 4, 5, 6, 7, 8 })
            Assert.True(reference.Similarity(IconPrint.Of(InGame(Art(other, 64)))) < IconPrint.MinScore, $"seed {other}: {reference.Similarity(IconPrint.Of(InGame(Art(other, 64)))):0.000}");
    }

    [Fact]
    public void A_flat_picture_matches_nothing()
    {
        var flat = new Image(64, 64, Enumerable.Repeat((byte)90, 64 * 64 * 4).ToArray());
        Assert.Equal(0, IconPrint.Of(Art(1, 64)).Similarity(IconPrint.Of(flat)));
    }

    [Fact]
    public void A_print_survives_its_bytes()
    {
        var print = IconPrint.Of(Art(9, 64));
        Assert.Equal(1, print.Similarity(IconPrint.FromBytes(print.ToBytes())), 6);
    }

    [Fact]
    public void A_crop_too_small_has_no_framings_and_matches_nothing()
    {
        Assert.Empty(IconPrint.Framings(new Image(12, 40)));
        Assert.Empty(IconPrint.Framings(new Image(54, 10)));
        Assert.Null(IconPrint.Best([], [("a", IconPrint.Of(Art(1, 64)))]));
        Assert.Single(IconPrint.Framings(Art(1, 50), maxInset: 0));
    }

    [Fact]
    public void A_print_of_the_wrong_length_is_refused() =>
        Assert.Throws<ArgumentException>(() => IconPrint.FromBytes(new byte[10]));

    [Fact]
    public void Best_counts_the_runner_up_once_per_id_when_an_id_has_several_prints()
    {
        var a = IconPrint.Of(Art(20, 64));
        var b = IconPrint.Of(Art(21, 64));
        var capture = IconPrint.Framings(Art(20, 64), maxInset: 0);
        Assert.Equal("a", IconPrint.Best(capture, [("a", b), ("b", b), ("a", a)]));   // a's weak print doesn't count as a's rival
        Assert.Null(IconPrint.Best(capture, [("a", a), ("b", a)]));                  // two ids alike: unknown
    }

    [Fact]
    public void A_capture_cut_inside_a_framed_reference_matches_its_trimmed_print()
    {
        var framed = InGame(Art(14, 80));   // the frame stands in for 각성기's gold one
        for (int y = 0; y < 80; y++)
            for (int x = 0; x < 80; x++)
                if (x < 8 || y < 8 || x >= 72 || y >= 72) { int i = y * framed.Stride + x * 4; (framed.Bgra[i], framed.Bgra[i + 1], framed.Bgra[i + 2]) = (40, 170, 230); }
        var capture = InGame(Art(14, 80)).Crop(new PixelRect(8, 8, 64, 64));
        double whole = IconPrint.Framings(capture).Max(f => f.Similarity(IconPrint.Of(framed)));
        double trimmed = IconPrint.References(framed).Max(r => IconPrint.Framings(capture).Max(f => f.Similarity(r)));
        Assert.True(trimmed > whole && trimmed > 0.9, $"whole {whole:0.000}, trimmed {trimmed:0.000}");
    }

    [Fact]
    public void Best_picks_the_matching_icon_and_refuses_a_close_call()
    {
        var library = new[] { ("a", IconPrint.Of(Art(10, 64))), ("b", IconPrint.Of(Art(11, 64))), ("c", IconPrint.Of(Art(12, 64))) };
        Assert.Equal("b", IconPrint.Best(IconPrint.Framings(InGame(Art(11, 48))), library));
        Assert.Null(IconPrint.Best(IconPrint.Framings(InGame(Art(11, 48))), [("b", library[1].Item2), ("b2", library[1].Item2)]));
        Assert.Null(IconPrint.Best(IconPrint.Framings(InGame(Art(13, 48))), library));
    }
}
