using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public class IconGateTests
{
    static readonly (string Fixture, Viewport Vp, Fit Fit, int Buff, PixelRect Rect) Src =
        ("buffrow_hud80", Fhd, new Fit(80), 120, new(630, 925, 250, 60));

    static (Image Frame, PixelRect[] Icons) Row() =>
        (Fixture.Load(Src.Fixture).Frames[0], [.. BuffReaderTests.Boxes(Src).Take(4).Select(b => b.Icon)]);

    static Image Shifted(Image img, int by) => new(img.Width, img.Height,
        [.. img.Bgra.Select((v, i) => i % 4 == 3 ? v : (byte)Math.Clamp(v + by, 0, 255))]);

    [Fact]
    public void Gives_what_the_reader_gives_and_identifies_once_while_the_icon_stays()
    {
        var (frame, icons) = Row();
        var id = new IconGate<BuffKind?>(BuffReader.Classify);

        var first = icons.Select((r, i) => id.Classify(frame, i, r)).ToArray();
        var again = icons.Select((r, i) => id.Classify(frame, i, r)).ToArray();

        Assert.Equal(icons.Select(r => BuffReader.Classify(frame, r)), first);
        Assert.Equal(first, again);
        Assert.Equal(4, id.Identifies);
    }

    [Fact]
    public void Compression_sized_noise_keeps_the_kind_without_identifying()
    {
        var (frame, icons) = Row();
        var id = new IconGate<BuffKind?>(BuffReader.Classify);
        var kind = id.Classify(frame, 0, icons[0]);

        Assert.Equal(kind, id.Classify(Shifted(frame, 3), 0, icons[0]));
        Assert.Equal(1, id.Identifies);
    }

    /// <summary>A box swapping straight to another buff, or to a look-alike, must move the thumbnail past the threshold.</summary>
    [Fact]
    public void Every_two_buff_icons_and_each_look_alike_differ_by_more_than_the_threshold()
    {
        static float[] Thumb(string b64) => BuffReader.Thumb(Image.FromBytes(Convert.FromBase64String(b64))!);
        var icons = BuffTemplates.Icons.Select(Thumb).ToArray();

        var pairs = icons.SelectMany((a, i) => icons.Skip(i + 1).Select(b => IconGate.Change(a, b)))
            .Concat(BuffTemplates.LookAlikes.Select(l => IconGate.Change(Thumb(l.Icon), icons[(int)l.Of])))
            .ToArray();

        Assert.All(pairs, d => Assert.True(d > IconGate.Threshold, $"change {d:0.0}"));
    }

    [Fact]
    public void A_box_partly_outside_the_capture_is_identified_as_the_reader_does()
    {
        var (frame, icons) = Row();
        var off = icons[0] with { X = frame.Width - icons[0].Width / 2 };

        Assert.Equal(BuffReader.Classify(frame, off), new IconGate<BuffKind?>(BuffReader.Classify).Classify(frame, 0, off));
    }

    [Fact]
    public void A_new_icon_or_a_moved_box_is_identified_again()
    {
        var (frame, icons) = Row();
        var id = new IconGate<BuffKind?>(BuffReader.Classify);
        id.Classify(frame, 0, icons[0]);

        var other = id.Classify(frame, 0, icons[1]);   // the box moved (another 버프 크기): its rect differs
        var blank = id.Classify(new Image(frame.Width, frame.Height, new byte[frame.Bgra.Length]), 0, icons[1]);

        Assert.Equal(BuffReader.Classify(frame, icons[1]), other);
        Assert.Null(blank);
        Assert.Equal(3, id.Identifies);
    }

    [Fact]
    public void After_a_reset_every_box_is_identified_again()
    {
        var (frame, icons) = Row();
        var id = new IconGate<BuffKind?>(BuffReader.Classify);
        foreach (var (r, i) in icons.Select((r, i) => (r, i))) id.Classify(frame, i, r);

        id.Reset();
        foreach (var (r, i) in icons.Select((r, i) => (r, i))) id.Classify(frame, i, r);

        Assert.Equal(8, id.Identifies);
    }

    [Fact]
    public void After_a_reset_the_cache_identifies_every_icon_again()
    {
        var (frame, icons) = Row();
        var cache = new IconCache<BuffKind?>(BuffReader.Classify, 4);
        foreach (var r in icons) cache.Classify(frame, r);

        cache.Reset();
        foreach (var r in icons) cache.Classify(frame, r);

        Assert.Equal(8, cache.Identifies);
    }

    [Fact]
    public void The_cache_answers_an_icon_seen_in_another_box_without_identifying()
    {
        var (frame, icons) = Row();
        var cache = new IconCache<BuffKind?>(BuffReader.Classify, 4);
        var kinds = icons.Select(r => cache.Classify(frame, r)).ToArray();

        var again = icons.Reverse().Select(r => cache.Classify(frame, r)).ToArray();

        Assert.Equal(icons.Select(r => BuffReader.Classify(frame, r)), kinds);
        Assert.Equal(kinds.Reverse(), again);
        Assert.Equal(4, cache.Identifies);
    }

    [Fact]
    public void The_cache_forgets_the_oldest_icon_past_its_capacity()
    {
        var (frame, icons) = Row();
        var cache = new IconCache<BuffKind?>(BuffReader.Classify, 2);
        foreach (var r in icons.Take(3)) cache.Classify(frame, r);   // icons[0] forgotten

        cache.Classify(frame, icons[2]);
        int kept = cache.Identifies;
        cache.Classify(frame, icons[0]);

        Assert.Equal(3, kept);
        Assert.Equal(4, cache.Identifies);
    }

    [Fact]
    public void An_icon_seen_again_is_kept_over_older_ones()
    {
        var (frame, icons) = Row();
        var cache = new IconCache<BuffKind?>(BuffReader.Classify, 2);
        cache.Classify(frame, icons[0]);
        cache.Classify(frame, icons[1]);
        cache.Classify(frame, icons[0]);   // seen again: icons[1] is now the oldest

        cache.Classify(frame, icons[2]);
        cache.Classify(frame, icons[0]);

        Assert.Equal(3, cache.Identifies);
    }
}
