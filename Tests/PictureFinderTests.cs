using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>테스트 모드's picture finder: the video picture inside a player window. The windows are built here around real frames
/// (full_* fixtures: 1080p, 4K, 4K forced 21:9 and a Discord stream, each scaled to 480×270) with the chrome players draw:
/// title bars with text, control bars with icons, black side bars, a YouTube page. Synthetic until the user's screenshots
/// of real players are added (windowed-video.plan.md).</summary>
public class PictureFinderTests
{
    const int Noise = Letterbox.VideoNoise;
    static readonly (byte B, byte G, byte R) Black = (0, 0, 0), TitleBar = (0x20, 0x20, 0x20), Controls = (0x10, 0x10, 0x10),
        DarkPage = (0x0F, 0x0F, 0x0F), LightPage = (0xF9, 0xF9, 0xF9), Ink = (0xF1, 0xF1, 0xF1), DarkInk = (0x30, 0x30, 0x30);

    static Image Frame(string name) => Fixture.Load(name).Frames[0];

    static Image Window(int width, int height, (byte B, byte G, byte R) fill)
    {
        var img = new Image(width, height);
        Fill(img, new(0, 0, width, height), fill);
        return img;
    }

    static void Fill(Image img, PixelRect r, (byte B, byte G, byte R) c)
    {
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                int p = (y * img.Width + x) * 4;
                (img.Bgra[p], img.Bgra[p + 1], img.Bgra[p + 2], img.Bgra[p + 3]) = (c.B, c.G, c.R, 255);
            }
    }

    static void Paste(Image img, Image picture, int left, int top)
    {
        for (int y = 0; y < picture.Height; y++)
            picture.Row(y).CopyTo(img.Bgra.AsSpan(((top + y) * img.Width + left) * 4));
    }

    /// <summary>Text or icons: 4×6 blocks every <paramref name="pitch"/> px along a strip (a title, a time, play buttons).</summary>
    static void Marks(Image img, PixelRect strip, (byte B, byte G, byte R) c, int pitch = 9)
    {
        for (int x = strip.X; x + 4 <= strip.Right; x += pitch) Fill(img, new(x, strip.Y, 4, Math.Min(6, strip.Height)), c);
    }

    static void AssertNear(PixelRect expected, PixelRect actual, int tolerance = 2)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Right, expected.Right - tolerance, expected.Right + tolerance);
        Assert.InRange(actual.Bottom, expected.Bottom - tolerance, expected.Bottom + tolerance);
    }

    /// <summary>영화 및 TV / PotPlayer-like: a title bar with its title and buttons, the picture, a control bar with a seek line
    /// and buttons.</summary>
    static (Image Img, PixelRect Picture) PlayerWindow(Image frame, int sideBars = 0)
    {
        int w = frame.Width + 2 * sideBars, h = 32 + frame.Height + 40;
        var img = Window(w, h, Black);
        Fill(img, new(0, 0, w, 32), TitleBar);
        Marks(img, new(8, 12, 90, 8), Ink);                // the title
        Marks(img, new(w - 80, 12, 72, 8), Ink, 24);       // minimise, maximise, close
        Paste(img, frame, sideBars, 32);
        Fill(img, new(0, 32 + frame.Height, w, 40), Controls);
        Fill(img, new(8, 32 + frame.Height + 6, w - 16, 2), Ink);            // seek line
        Marks(img, new(w / 2 - 40, 32 + frame.Height + 20, 80, 12), Ink, 20); // play, back, forward
        return (img, new(sideBars, 32, frame.Width, frame.Height));
    }

    [Theory]
    [InlineData("full_1080")]
    [InlineData("full_4k")]
    public void A_fullscreen_16_9_frame_is_the_whole_picture(string name)
    {
        var frame = Frame(name);
        Assert.Equal(new PixelRect(0, 0, frame.Width, frame.Height), PictureFinder.Candidates(frame)[0]);
    }

    /// <summary>The FlatShare / FlatNoise margin: no line of a real game picture counts as chrome, so a fullscreen frame is one
    /// busy area, and a letterboxed one is busy between its bars.</summary>
    [Theory]
    [InlineData("full_1080", 0, 270)]
    [InlineData("full_4k", 0, 270)]
    [InlineData("full_discord", 32, 238)]   // 135 / 945 of 1080, ±1 for the scaled blur; the mic icons below stay out
    public void Every_line_of_a_game_picture_is_busy(string name, int top, int bottom)
    {
        var b = PictureFinder.Busy(Frame(name));
        Assert.NotNull(b);
        Assert.Equal((0, 480), (b.Value.X, b.Value.Right));
        Assert.InRange(b.Value.Y, top - 1, top + 2);
        Assert.InRange(b.Value.Bottom, bottom - 2, bottom + 1);
    }

    [Theory]
    [InlineData("full_4k219")]
    [InlineData("full_discord")]
    public void A_letterboxed_21_9_frame_matches_the_letterbox_rule(string name)
    {
        var frame = Frame(name);
        var client = new PixelRect(0, 0, frame.Width, frame.Height);
        var vp = Letterbox.Viewport(client, frame.Crop(Letterbox.Probe(client)), Noise);
        Assert.Equal(new PixelRect(vp.X, vp.Y, vp.Width, vp.Height), PictureFinder.Candidates(frame)[0]);
    }

    [Theory]
    [InlineData("full_1080", 0)]
    [InlineData("full_4k", 0)]
    [InlineData("full_1080", 60)]   // a window wider than the picture: black side bars
    [InlineData("full_4k219", 0)]   // a 21:9 recording in a 16:9 file: the bars are the file's
    public void A_player_window_gives_its_picture_first(string name, int sideBars)
    {
        var (img, picture) = PlayerWindow(Frame(name), sideBars);
        var expected = name == "full_4k219" ? Letterboxed(picture) : picture;
        AssertNear(expected, PictureFinder.Candidates(img)[0]);
    }

    /// <summary>The 21:9 area inside a 16:9 picture.</summary>
    static PixelRect Letterboxed(PixelRect picture)
    {
        int h = (int)Math.Round(picture.Width * 27.0 / 64);
        return new(picture.X, picture.Y + (picture.Height - h) / 2, picture.Width, h);
    }

    [Fact]
    public void A_dark_YouTube_page_with_text_under_the_video_gives_the_video_first()
    {
        var frame = Frame("full_1080");
        var img = Window(560, 420, DarkPage);
        Paste(img, frame, 24, 56);
        Marks(img, new(24, 20, 200, 10), Ink, 6);                       // the search bar row
        Marks(img, new(24, 56 + frame.Height + 12, 300, 10), Ink, 6);  // the video's title
        Marks(img, new(24, 56 + frame.Height + 40, 160, 10), Ink, 6);  // the channel
        AssertNear(new(24, 56, frame.Width, frame.Height), PictureFinder.Candidates(img)[0]);
    }

    [Fact]
    public void A_light_YouTube_page_with_thumbnails_beside_the_video_gives_the_video_first()
    {
        var frame = Frame("full_4k");
        var img = Window(640, 360, LightPage);
        Paste(img, frame, 24, 56);
        for (int y = 56; y + 50 <= 340; y += 60) Paste(img, frame.Crop(new(100, 100, 96, 50)), 520, y);   // thumbnails
        Marks(img, new(24, 56 + frame.Height + 12, 300, 10), DarkInk, 6);
        AssertNear(new(24, 56, frame.Width, frame.Height), PictureFinder.Candidates(img)[0]);
    }

    [Fact]
    public void The_hover_controls_over_the_picture_do_not_move_it()
    {
        var (img, picture) = PlayerWindow(Frame("full_1080"));
        // YouTube's bar on hover: a dark band over the picture's bottom tenth, with a red seek line and white buttons.
        var band = new PixelRect(picture.X, picture.Bottom - picture.Height / 10, picture.Width, picture.Height / 10);
        Fill(img, band, (0x18, 0x18, 0x18));
        Fill(img, new(band.X + 8, band.Y + 3, band.Width - 16, 2), (0x00, 0x00, 0xFF));
        Marks(img, new(band.X + 8, band.Y + 12, 120, 10), Ink, 18);
        AssertNear(picture, PictureFinder.Candidates(img)[0]);
    }

    [Fact]
    public void A_dark_scene_gives_the_picture_or_nothing_never_a_bigger_area()
    {
        var frame = Frame("full_1080").Copy();
        for (int i = 0; i < frame.Bgra.Length; i++) if (i % 4 != 3) frame.Bgra[i] = (byte)(frame.Bgra[i] / 10);   // a fade to black
        var (img, picture) = PlayerWindow(frame);
        var found = PictureFinder.Candidates(img);
        if (found.Count == 0) return;
        Assert.True(found[0].X >= picture.X - 2 && found[0].Y >= picture.Y - 2 && found[0].Right <= picture.Right + 2 && found[0].Bottom <= picture.Bottom + 2,
            $"first candidate {found[0]} spills out of the picture {picture}");
    }

    [Fact]
    public void A_blank_window_has_no_picture()
    {
        Assert.Empty(PictureFinder.Candidates(Window(480, 300, Black)));
        Assert.Empty(PictureFinder.Candidates(Window(480, 300, LightPage)));
    }

    [Fact]
    public void Every_candidate_lies_inside_the_window()
    {
        var (img, _) = PlayerWindow(Frame("full_1080"), 60);
        foreach (var r in PictureFinder.Candidates(img))
            Assert.True(r.X >= 0 && r.Y >= 0 && r.Right <= img.Width && r.Bottom <= img.Height && r.Width > 0 && r.Height > 0, r.ToString());
    }
}
