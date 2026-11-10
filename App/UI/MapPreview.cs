using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using Picture = System.Windows.Controls.Image;

namespace GaugeOverlay.App.UI;

/// <summary>The card a 표시 map box shows on hover: a recorded GIF of its window on the sample screen (Assets/map, made by
/// tools/make-map-gifs.ps1), and the reader's stored capture with its mark. A box shows whichever it has.</summary>
internal static class MapPreview
{
    const double CardWidth = 400, CaptureHeight = 130;
    /// <summary>tools/make-map-gifs.ps1 records 10 frames a second.</summary>
    static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(100);
    /// <summary>The last GIF decoded: about 20 MB of frames each, so not all nine are kept.</summary>
    static (OverlayPart Part, BitmapSource[] Frames)? _decoded;

    /// <returns>Null for a box with no window and no reader.</returns>
    public static UIElement? Card(string title, OverlayPart? window, Detection? mark, ImageSource? capture, TimeSpan now)
    {
        var gif = window is { } part ? Gif(part) : null;
        if (gif is null && mark is null) return null;
        var body = new StackPanel { Width = CardWidth };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 6) });
        if (gif is not null) body.Children.Add(gif);
        if (mark is { } d)
        {
            if (capture is not null) body.Children.Add(new Picture { Source = capture, MaxHeight = CaptureHeight, Margin = new(0, gif is null ? 0 : 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            body.Children.Add(new TextBlock { Text = Caption(d, capture is not null, now), FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 0) });
        }
        var card = new Border { Child = body, Padding = new(10), CornerRadius = new(8), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, Themes.BackgroundBrushKey);
        card.SetResourceReference(Border.BorderBrushProperty, Themes.StrokeBrushKey);
        return card;
    }

    static string Caption(Detection d, bool pictured, TimeSpan now) => d.Mark switch
    {
        DetectMark.Concrete => $"확정된 순간 · {FeatureMapView.Ago(now - d.At)}" + (pictured ? "" : " (화면은 아직 없음)"),
        DetectMark.Failed => $"못 찾은 순간 · {FeatureMapView.Ago(now - d.At)}" + (d.Why is { } why ? $"\n{why}" : ""),
        _ => "아직 찾는 중입니다. 찾으면 그 순간의 화면이 여기 나옵니다.",
    };

    /// <summary>The window's GIF playing (Windows' animations off: its first frame); null if the file can't be read.</summary>
    static Picture? Gif(OverlayPart part)
    {
        BitmapSource[] frames;
        try
        {
            if (_decoded is { } kept && kept.Part == part) frames = kept.Frames;
            else
            {
                using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/map/{part}.gif")).Stream;
                frames = Frames(stream);
                _decoded = (part, frames);
            }
        }
        catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or ArgumentException)   // a card without it beats a crash on hover
        {
            System.Diagnostics.Trace.WriteLine($"[map] no GIF for {part}: {ex.Message}");
            return null;
        }
        var image = new Picture { Source = frames[0], Width = CardWidth };
        if (!SystemParameters.ClientAreaAnimation) return image;
        var play = new ObjectAnimationUsingKeyFrames { Duration = FrameTime * frames.Length, RepeatBehavior = RepeatBehavior.Forever };
        for (int i = 0; i < frames.Length; i++) play.KeyFrames.Add(new DiscreteObjectKeyFrame(frames[i], KeyTime.FromTimeSpan(FrameTime * i)));
        image.BeginAnimation(Picture.SourceProperty, play);
        image.Unloaded += (_, _) => image.BeginAnimation(Picture.SourceProperty, null);
        return image;
    }

    /// <summary>WPF plays no GIF: each frame holds only what changed, at its offset, so they are laid over each other here.</summary>
    static BitmapSource[] Frames(Stream gif)
    {
        var decoder = new GifBitmapDecoder(gif, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight, stride = width * 4;
        var screen = new byte[stride * height];
        var frames = new BitmapSource[decoder.Frames.Count];
        for (int f = 0; f < frames.Length; f++)
        {
            var frame = decoder.Frames[f];
            var meta = (BitmapMetadata)frame.Metadata;
            int left = Convert.ToInt32(meta.GetQuery("/imgdesc/Left")), top = Convert.ToInt32(meta.GetQuery("/imgdesc/Top"));
            int w = frame.PixelWidth, h = frame.PixelHeight;
            var pixels = new byte[w * h * 4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, w * 4, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int from = (y * w + x) * 4, to = (top + y) * stride + (left + x) * 4;
                    if (pixels[from + 3] != 0) Array.Copy(pixels, from, screen, to, 4);
                }
            frames[f] = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, screen, stride);
            frames[f].Freeze();
        }
        return frames;
    }
}
