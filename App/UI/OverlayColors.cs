using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Colours, brushes, pens and shapes the drawing uses (cached where they are made each frame).</summary>
public sealed partial class OverlayRenderer
{
    /// <summary>cubic-bezier(0.23, 1, 0.32, 1), solved for x by bisection.</summary>
    static double EaseOut(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        static double B(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double lo = 0, hi = 1, t = x;
        for (int i = 0; i < 24; i++)
        {
            t = (lo + hi) / 2;
            if (B(t, 0.23, 0.32) < x) lo = t; else hi = t;
        }
        return B(t, 1, 1);
    }

    /// <summary>Clockwise pie from 12 o'clock covering <paramref name="p"/> of the box (radius reaches the corners).</summary>
    static Geometry Pie(Rect r, double p)
    {
        if (p >= 0.9999) return new RectangleGeometry(r);
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double rad = Math.Sqrt(r.Width * r.Width + r.Height * r.Height) / 2 + 1;
        double ang = p * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true, true);
            ctx.LineTo(new Point(c.X, c.Y - rad), false, false);
            ctx.ArcTo(new Point(c.X + rad * Math.Sin(ang), c.Y - rad * Math.Cos(ang)), new Size(rad, rad), 0,
                ang > Math.PI, SweepDirection.Clockwise, false, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary><paramref name="c"/> at <paramref name="k"/> of its perceived lightness (CIE L*), chromaticity kept,
    /// so 30% / 55% are the same visible step for every hue (scaling sRGB bytes dims dark hues far more).</summary>
    /// <summary>A cooling box's colour at lightness <paramref name="k"/>: with a chosen cooldown colour
    /// (<paramref name="dim"/>), that colour at <see cref="CooldownLevel"/> blending into <paramref name="c"/> at 1;
    /// otherwise <see cref="Level"/>.</summary>
    static Color Shade(Color c, Color? dim, double k)
    {
        if (dim is not { } d || k >= 1) return Level(c, k);
        double t = Math.Clamp((k - CooldownLevel) / (1 - CooldownLevel), 0, 1);
        byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
        return Color.FromRgb(Mix(d.R, c.R), Mix(d.G, c.G), Mix(d.B, c.B));
    }

    internal static Color Level(Color c, double k)
    {
        if (k >= 1) return c;
        double y = Luminance(c);
        if (y <= 0) return Color.FromRgb(0, 0, 0);
        double f = LightnessToY(k * Lightness(y)) / y;
        return Color.FromRgb(Encode(Linear(c.R) * f), Encode(Linear(c.G) * f), Encode(Linear(c.B) * f));
    }

    Color Parse(string hex)
    {
        if (_colors.TryGetValue(hex, out var c)) return c;
        if (_colors.Count > 1024) _colors.Clear();   // colours a party member sent are new keys: keep the cache bounded
        return _colors[hex] = UiColor.Parse(hex, Colors.Gray);
    }

    SolidColorBrush Solid(Color c)
    {
        uint k = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
        if (_brushes.TryGetValue(k, out var b)) return b;
        if (_brushes.Count > 1024) _brushes.Clear();
        return _brushes[k] = Frozen(new SolidColorBrush(c));
    }

    FormattedText NameText(string s, double size)
    {
        if (_names.TryGetValue((s, size), out var t)) return t;
        if (_names.Count > 64) _names.Clear();
        return _names[(s, size)] = Text(s, size);
    }

    Geometry BoxClip(Rect r)
    {
        if (!_clips.TryGetValue(r, out var g)) _clips[r] = g = Frozen(new RectangleGeometry(r, BoxRadius, BoxRadius));
        return g;
    }

    Geometry TextGeometry(string s, double size)
    {
        if (_texts.TryGetValue((s, size), out var g)) return g;
        if (_texts.Count > 512) _texts.Clear();
        // Segoe UI's default figures are tabular, so digits don't shift as the timer counts.
        g = Text(s, size).BuildGeometry(new Point(0, 0));
        g.Freeze();
        return _texts[(s, size)] = g;
    }

    /// <summary>The "unknown" hatch, tiled at a size proportional to <paramref name="unit"/> so it scales with the
    /// box instead of going heavy at small scale and faint at large scale.</summary>
    /// <summary>Slashes for a box of this size: white (Unknown) or dark (a slashed fill, over full colour).</summary>
    Brush HatchFor(double unit, bool dark = false)
    {
        if (_hatches.TryGetValue((unit, dark), out var b)) return b;
        if (_hatches.Count > 16) _hatches.Clear();
        return _hatches[(unit, dark)] = MakeHatch(6 * unit / Box, dark ? Color.FromArgb(0x80, 0, 0, 0) : Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
    }

    Pen LinePen(double width)
    {
        if (!_linePens.TryGetValue(width, out var p)) _linePens[width] = p = Frozen(new Pen(Brushes.White, width));
        return p;
    }

    Pen GlowPenOf(Color c)
    {
        if (_glowPens.TryGetValue(c, out var p)) return p;
        if (_glowPens.Count > MaxGlowPens) _glowPens.Clear();   // each colour tried in 빛 색 adds one: keep it bounded
        return _glowPens[c] = Frozen(new Pen(Solid(c), ReadyOutline));
    }

    /// <summary><paramref name="c"/> without its hue, at the same perceived lightness.</summary>
    static Color Grey(Color c)
    {
        byte v = Encode(Luminance(c));
        return Color.FromRgb(v, v, v);
    }

    static Brush MakeHatch(double tile, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.2 * tile / 6);
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, tile), new Point(tile, 0)));
        lines.Children.Add(new LineGeometry(new Point(-tile / 6, tile / 6), new Point(tile / 6, -tile / 6)));
        lines.Children.Add(new LineGeometry(new Point(tile * 5 / 6, tile * 7 / 6), new Point(tile * 7 / 6, tile * 5 / 6)));
        return Frozen(new DrawingBrush(new GeometryDrawing(null, pen, lines))
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            Viewport = new Rect(0, 0, tile, tile), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, tile, tile), ViewboxUnits = BrushMappingMode.Absolute,
        });
    }
}
