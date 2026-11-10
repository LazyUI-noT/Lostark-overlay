using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>A raid's difficulty as a capsule in its colour with white text, the raid's logo in front (RaidBadge; settings-reorg.plan.md
/// 11, 12). No logo: the capsule alone. No difficulty: the logo alone. An unknown one: a grey capsule with its name. As a control
/// (설정's rows) or drawn (TimelineView).</summary>
public static class RaidBadgeView
{
    /// <summary>The colours in use (the user's, 숙제's palette; defaults are <see cref="RaidCapsuleColors.DefaultOf"/>), set by
    /// the settings window. Read on every use, so a change or an undo shows at the next build or draw.</summary>
    public static Func<RaidCapsuleColors> Colors { get; set; } = () => new();
    static readonly Dictionary<string, Brush> FillBrushes = [];   // by "#RRGGBB"; UI thread only
    /// <summary>Corner radius per px of text: a rectangle with round corners, not a pill (user, 2026-11-10).</summary>
    const double CornerPerSize = 0.3;

    /// <summary>The capsule's fill for <paramref name="tier"/>.</summary>
    public static Brush Fill(DifficultyTier tier)
    {
        string hex = Colors().HexOf(tier);
        if (FillBrushes.TryGetValue(hex, out var b)) return b;
        return FillBrushes[hex] = Frozen(UiColor.Parse(hex, Color.FromRgb(0x8C, 0x8C, 0x8C)));
    }

    static readonly Dictionary<string, ImageSource?> Logos = [];
    static readonly Typeface Face = new(new FontFamily("Malgun Gothic"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Dictionary<(string, double, double), FormattedText> Texts = [];   // UI thread only, like Logos; a few difficulties × sizes

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>The logo's image (App/Assets/raids), null for a raid without one or an image that won't load.</summary>
    static ImageSource? Logo(string? raid)
    {
        if (RaidBadge.Logo(raid) is not { } key) return null;
        if (Logos.TryGetValue(key, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            var b = new BitmapImage(new Uri($"pack://application:,,,/Assets/raids/{key}.png"));
            b.Freeze();
            image = b;
        }
        catch (Exception e) when (e is System.IO.IOException or NotSupportedException or UriFormatException)
        {
            System.Diagnostics.Trace.WriteLine($"[ui] raid logo {key}: {e.Message}");   // a quiet fallback: the capsule alone
        }
        return Logos[key] = image;
    }

    /// <summary>The badge as a control, its text <paramref name="size"/> px.</summary>
    public static FrameworkElement Of(string? raid, string? difficulty, double size = 12)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (Logo(raid) is { } logo)
        {
            var image = new System.Windows.Controls.Image { Source = logo, Height = size * 1.7, Width = size * 1.7, Margin = new(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(image, raid);   // a screen reader says the raid (impeccable audit, 2026-11-09)
            row.Children.Add(image);
        }
        if (!string.IsNullOrWhiteSpace(difficulty))
            row.Children.Add(new Border
            {
                Background = Fill(RaidBadge.Tier(difficulty)), CornerRadius = new(size * CornerPerSize), Padding = new(size * 0.7, 1, size * 0.7, 1.5),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = RaidBadge.Label(difficulty), FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White },
            });
        return row;
    }

    /// <summary>The badge drawn at <paramref name="at"/> (its top left) in a line <paramref name="height"/> px high; its width.</summary>
    public static double Draw(DrawingContext dc, Point at, string? raid, string? difficulty, double size, double height, double pixelsPerDip)
    {
        double x = at.X;
        if (Logo(raid) is { } logo)
        {
            double side = Math.Min(height, size * 1.7);
            dc.DrawImage(logo, new Rect(x, at.Y + (height - side) / 2, side, side));
            x += side + 4;
        }
        if (string.IsNullOrWhiteSpace(difficulty)) return x - at.X;
        var key = (difficulty, size, pixelsPerDip);   // TimelineView redraws on every mouse move: measure each capsule once
        if (!Texts.TryGetValue(key, out var text))
            Texts[key] = text = new FormattedText(RaidBadge.Label(difficulty), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White, pixelsPerDip);
        double pad = size * 0.7, h = Math.Min(height, text.Height + 2);
        var box = new Rect(x, at.Y + (height - h) / 2, text.Width + pad * 2, h);
        dc.DrawRoundedRectangle(Fill(RaidBadge.Tier(difficulty)), null, box, size * CornerPerSize, size * CornerPerSize);
        dc.DrawText(text, new Point(x + pad, box.Y + (h - text.Height) / 2));
        return box.Right - at.X;
    }
}
