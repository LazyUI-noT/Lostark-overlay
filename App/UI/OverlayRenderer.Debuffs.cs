using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>The debuff window (PLAN §4 디버프): three boxes in a row. Your sigil, as the game draws it, bordered in its colour; the swap box, in the colour of the
/// teammates to bring with their number in white (DebuffAdvice), shown only while the swap debuff is on; the lightning, flashing yellow and black with its seconds in the opposite colour while it is on you.</summary>
public sealed partial class OverlayRenderer
{
    static readonly Color DebuffRed = Color.FromRgb(0xE8, 0x26, 0x2B), DebuffBlue = Color.FromRgb(0x2F, 0x7B, 0xFF);
    static readonly Color LightningColor = Color.FromRgb(0xFF, 0xE6, 0x00), DebuffEmpty = Color.FromArgb(0x80, 0x30, 0x33, 0x3A);
    const double FlashMs = 500;          // 번개: yellow for the first half of each period, black for the second
    const double DebuffTextShare = 0.55; // text height, share of a box's height
    const double SwapOutline = 3;        // the swap box's outline in the other colour
    const double SigilOutline = 3;       // 내 색's border in its own colour, so it reads even when the icon is small
    // 내 색: the blue and red sigils as the game draws them (the reader's templates, 25 px, scaled to the box).
    static readonly ImageSource[] SigilIcons = [.. new[] { DebuffKind.파랑, DebuffKind.빨강 }.Select(k =>
    {
        var icon = DebuffReader.Icon(k);
        var bmp = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Bgr32, null, icon.Bgra, icon.Stride);
        bmp.Freeze();
        return (ImageSource)bmp;
    })];

    /// <summary>The debuff window: your raid debuffs alone (PLAN §4 디버프).</summary>
    public bool DebuffsOnly { get; set; }

    (double W, double H) DebuffBox => (BoxSize(Settings.Debuffs.Width), BoxSize(Settings.Debuffs.Height));

    /// <summary>The three boxes (내 색, 교체, 번개), each at its own size (DebuffSettings.Boxes), in window coordinates.</summary>
    Rect[] DebuffCells()
    {
        var (w, h) = DebuffBox;
        return [.. BarSizes.DebuffBoxes(Settings.Debuffs, w, h, Gap).Select(c => new Rect(Bleed + Math.Round(c.X), Bleed, Math.Round(c.W), Math.Round(c.H)))];
    }

    /// <summary>The debuff box (0 내 색, 1 교체, 2 번개) under <paramref name="at"/>, or null; for the 레이아웃 편집기.</summary>
    public int? DebuffAt(Point at)
    {
        var cells = DebuffCells();
        int i = Array.FindIndex(cells, c => c.Contains(at));
        return i < 0 ? null : i;
    }

    /// <summary>Where debuff box <paramref name="i"/> is drawn (this element's coordinates).</summary>
    public Rect DebuffRect(int i) => DebuffCells()[i];

    Size DebuffPanelSize()
    {
        var cells = DebuffCells();
        return new(cells.Max(c => c.Right) + Bleed, cells.Max(c => c.Bottom) + Bleed);
    }

    void DrawDebuffs(DrawingContext dc, TimeSpan now, double ms, bool reduced)
    {
        if (_player?.Snapshot.Debuffs is not { } d) return;
        var cells = DebuffCells();
        // Each box's text follows its own height.
        double Font(Rect r) => Math.Round(r.Height * DebuffTextShare / TimerFontStep) * TimerFontStep;
        Rect Cell(int i) => cells[i];

        var mine = d.Colour;
        if (mine is { } sigil)
        {
            bool red = sigil == DebuffKind.빨강;
            dc.DrawImage(SigilIcons[red ? 1 : 0], Cell(0));   // the game's own icon
            var edge = Cell(0);
            edge.Inflate(-SigilOutline / 2, -SigilOutline / 2);   // the stroke stays inside the box
            dc.DrawRectangle(null, new Pen(Solid(red ? DebuffRed : DebuffBlue), SigilOutline), edge);
        }
        else
        {
            dc.DrawRectangle(Solid(DebuffEmpty), null, Cell(0));
            DrawOutlined(dc, Cell(0), "?", Font(Cell(0)));
        }

        var swap = Cell(1);
        if (d.Swap is null) dc.DrawRectangle(Solid(DebuffEmpty), null, swap);   // the advice shows only while the swap is on you
        else if (mine is { } m && DebuffAdvice.Bring(m, d.BossRed, d.BossBlue) is { } b)
        {
            // The whole box in the colour to bring, outlined in the other colour, the count in white.
            bool red = b.Colour == DebuffKind.빨강;
            var inside = swap;
            inside.Inflate(-SwapOutline / 2, -SwapOutline / 2);   // the stroke stays inside the box
            dc.DrawRectangle(Solid(red ? DebuffRed : DebuffBlue), new Pen(Solid(red ? DebuffBlue : DebuffRed), SwapOutline), inside);
            DrawOutlined(dc, swap, b.Count.ToString(CultureInfo.InvariantCulture), Font(swap), Colors.White);
        }
        else
        {
            dc.DrawRectangle(Solid(DebuffEmpty), null, swap);
            DrawOutlined(dc, swap, "?", Font(swap));
        }

        var bolt = Cell(2);
        if (d.Lightning is { } l)
        {
            _animating |= !reduced;   // keeps the render loop running for the flash and the countdown
            // Yellow with a black number, then black with a yellow one; steady yellow under reduced motion.
            bool bright = reduced || ms % FlashMs < FlashMs / 2;
            dc.DrawRectangle(Solid(bright ? LightningColor : Colors.Black), null, bolt);
            DrawOutlined(dc, bolt, l.Seconds(now) is { } s ? s.ToString(CultureInfo.InvariantCulture) : "!", Font(bolt),
                bright ? Colors.Black : LightningColor);
        }
        else dc.DrawRectangle(Solid(DebuffEmpty), null, bolt);
    }
}
