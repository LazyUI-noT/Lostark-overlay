using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.UI;

/// <summary>The 암수 창 (amsu.design.md): the bomb on the boss with the most time left, its icon and one of three looks. 막대: a
/// bar beside the icon emptying toward <see cref="AmsuSettings.Direction"/>; 원: a disc around the icon eaten away clockwise; 숫자:
/// the seconds on or beside the icon. Faded while the icon isn't seen (counting on), red and blinking under the warning
/// seconds, gone when no bomb is up.</summary>
public sealed partial class OverlayRenderer
{
    const double AmsuBarLength = 3, AmsuBarThickness = 0.45;   // × the icon: the bar's length (× 너비) and thickness
    const double AmsuIconInDisc = 0.72, AmsuBeside = 1.2;       // 원: the icon's share of the disc; 숫자 옆: the text's width × the icon
    const double AmsuText = 0.55, AmsuUnseen = 0.55, AmsuGap = 2;
    const double BlinkHz = 2, BlinkLow = 0.6;
    static readonly ImageSource?[] BombIcons = [.. Enum.GetValues<Bomb>().Select(b => BombImage(AmsuReader.IconOf(b)))];

    static ImageSource? BombImage(Core.Domain.Image? icon)
    {
        if (icon is null) return null;
        var bmp = BitmapSource.Create(icon.Width, icon.Height, 96, 96, PixelFormats.Bgr32, null, icon.Bgra, icon.Stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The 암수 창: the bombs on the boss alone.</summary>
    public bool AmsuOnly { get; set; }

    /// <summary>Whether the window counts down: the "N초" is read only from 보스 디버프 크기 100% (Amsu.Timed); under it, the icon alone.</summary>
    bool AmsuTimed => Amsu.Timed(Settings.Debuffs.BossIconSize);

    /// <summary>The icon and the part beside it (막대's bar, 숫자's text; none for 원, 숫자 on the icon, or untimed).</summary>
    (Rect Icon, Rect? Side) AmsuCells()
    {
        var a = Settings.Amsu;
        double s = BoxSize(a.Height);
        var icon = new Rect(Bleed, Bleed, s, s);
        if (!AmsuTimed) return (icon, null);
        switch (a.Look)
        {
            case AmsuLook.막대 when a.Direction is BarDirection.위 or BarDirection.아래:
                double w = Math.Round(s * AmsuBarThickness);
                return (icon, new Rect(Bleed + (s - w) / 2, Bleed + s + AmsuGap, w, BoxSize(a.Width, AmsuBarLength)));
            case AmsuLook.막대:
                double h = Math.Round(s * AmsuBarThickness);
                return (icon, new Rect(Bleed + s + AmsuGap, Bleed + (s - h) / 2, BoxSize(a.Width, AmsuBarLength), h));
            case AmsuLook.숫자 when a.NumberBeside:
                return (icon, new Rect(Bleed + s, Bleed, Math.Round(s * AmsuBeside), s));
            default:
                return (icon, null);
        }
    }

    Size AmsuPanelSize()
    {
        var (icon, side) = AmsuCells();
        var all = side is { } r ? Rect.Union(icon, r) : icon;
        return new(all.Right + Bleed, all.Bottom + Bleed);
    }

    void DrawAmsu(DrawingContext dc, TimeSpan now, double ms, bool reduced)
    {
        if (_player?.Snapshot.Amsu is not { } state || Amsu.View(state, now) is not ({ } kind, var left, var seen)) return;
        var a = Settings.Amsu;
        bool timed = AmsuTimed, warn = timed && a.WarnSeconds > 0 && left < a.WarnSeconds;
        double blink = warn && !reduced ? BlinkLow + (1 - BlinkLow) * (0.5 + 0.5 * Math.Cos(2 * Math.PI * BlinkHz * ms / 1000)) : 1;
        var colour = Parse(warn ? a.WarnColor : a.Color);
        double share = Math.Clamp(left / Amsu.Duration(kind), 0, 1);
        var (icon, side) = AmsuCells();
        var image = BombIcons[(int)kind] ?? BombIcons[(int)Bomb.암수];   // 빛암 has no template yet

        dc.PushOpacity(Math.Clamp(a.Opacity, AmsuSettings.MinOpacity, 1) * (seen ? 1 : AmsuUnseen) * blink);
        switch (timed ? a.Look : (AmsuLook?)null)
        {
            case null:   // untimed: the bomb is up, no time to show
                if (image is not null) dc.DrawImage(image, icon);
                break;
            case AmsuLook.원:
                DrawDisc(dc, icon, share, colour, image);
                break;
            case AmsuLook.막대:
                if (image is not null) dc.DrawImage(image, icon);
                DrawAmsuBar(dc, side!.Value, share, colour, a.Direction);
                break;
            default:
                if (image is not null) dc.DrawImage(image, icon);
                var text = side ?? icon;
                DrawOutlined(dc, text, Math.Ceiling(left).ToString(CultureInfo.InvariantCulture),
                    Math.Round(text.Height * AmsuText / TimerFontStep) * TimerFontStep, warn || side is not null ? colour : null);
                break;
        }
        dc.Pop();
        if (_frozenAt is null) _moving = true;   // the countdown runs (and the blink)
    }

    /// <summary>The empty track, then the time left anchored on the side away from <paramref name="toward"/>.</summary>
    void DrawAmsuBar(DrawingContext dc, Rect r, double share, Color colour, BarDirection toward)
    {
        dc.DrawRectangle(Solid(DebuffEmpty), null, r);
        if (share <= 0) return;
        double w = r.Width * share, h = r.Height * share;
        dc.DrawRectangle(Solid(colour), null, toward switch
        {
            BarDirection.오른쪽 => new Rect(r.Right - w, r.Y, w, r.Height),
            BarDirection.왼쪽 => new Rect(r.X, r.Y, w, r.Height),
            BarDirection.위 => new Rect(r.X, r.Y, r.Width, h),
            _ => new Rect(r.X, r.Bottom - h, r.Width, h),
        });
    }

    /// <summary>A disc of the time left, eaten away clockwise from the top, with the icon in a circle on it.</summary>
    void DrawDisc(DrawingContext dc, Rect r, double share, Color colour, ImageSource? image)
    {
        var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        double radius = r.Width / 2;
        dc.DrawEllipse(Solid(DebuffEmpty), null, c, radius, radius);
        if (share >= 0.999) dc.DrawEllipse(Solid(colour), null, c, radius, radius);
        else if (share > 0)
        {
            double from = (1 - share) * 2 * Math.PI;   // the eaten part runs clockwise from the top to here
            var pie = new StreamGeometry();
            using (var g = pie.Open())
            {
                g.BeginFigure(c, true, true);
                g.LineTo(new Point(c.X + radius * Math.Sin(from), c.Y - radius * Math.Cos(from)), false, false);
                g.ArcTo(new Point(c.X, c.Y - radius), new Size(radius, radius), 0, share > 0.5, SweepDirection.Clockwise, false, false);
            }
            pie.Freeze();
            dc.DrawGeometry(Solid(colour), null, pie);
        }
        if (image is null) return;
        double inner = radius * AmsuIconInDisc;
        dc.PushClip(new EllipseGeometry(c, inner, inner));
        dc.DrawImage(image, new Rect(c.X - inner, c.Y - inner, 2 * inner, 2 * inner));
        dc.Pop();
    }
}
