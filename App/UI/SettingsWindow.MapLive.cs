using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>A 화면 인식 설정 chip's card on the 표시 page: the box where its reader reads, with its grid, on the stored moment and
/// then each Live frame, and a small game screen marking where that box sits. A box not read yet still shows its place from
/// the engine's areas (sized by the in-game settings), never an empty card, over the game screen saved when 스킬창 was first read
/// (<see cref="Core.Data.ReadingShots.BackdropFile"/>, user 2026-11-19).</summary>
public sealed partial class SettingsWindow
{
    /// <summary>The card's picture, swapped for each Live frame (DetectAreasChanged); null while no card shows.</summary>
    System.Windows.Controls.Image? _mapLivePicture;
    PixelRect _mapLiveView;
    (Viewport For, DateTime At, ImageSource? Picture)? _backdrop;   // decoded once per saved file; null = unreadable, not retried
    ImageSource? _backdropDemo;

    const double CardWidth = 520, LocatorHeight = 72;

    UIElement? MapLiveCard(string id)
    {
        var mark = _detect.GetValueOrDefault(id);
        var kept = mark is { Mark: not DetectMark.Waiting } ? _detectPictures.GetValueOrDefault(id) : null;   // a ? box's old moment would mislead
        _mapLivePicture = new System.Windows.Controls.Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        string title = LookOf(id)?.Title ?? id;
        var place = PlaceView(id, mark, kept, out bool backdrop);
        return MapPreview.LiveCard(title, mark, place, _detectAt, backdrop && kept is null);
    }

    void MapLive(string? id)
    {
        if (id is null) _mapLivePicture = null;
        SetLive(id);
    }

    /// <summary>The box and its surroundings, the picture placed under them; the bare picture when no place is known.</summary>
    FrameworkElement PlaceView(string id, Detection? mark, DetectPicture? kept, out bool backdrop)
    {
        var img = _mapLivePicture!;
        var behind = Backdrop();
        backdrop = false;
        if (!_detectAreas.TryGetValue(id, out var area) || area.Width <= 0 || area.Height <= 0 || LookOf(id) is not { } look)
        {
            if (kept is not null) img.Source = BitmapOf(kept);
            (img.MaxHeight, img.Stretch) = (MapPreview.CaptureHeight, Stretch.Uniform);
            return img;
        }
        var view = _mapLiveView = DetectPlace.CardView(area, _detectView);
        var colour = (Color)ColorConverter.ConvertFromString(look.Color);
        double k = Math.Min(MapPreview.CaptureHeight / view.Height, CardWidth / view.Width), stroke = 2 / k;
        var canvas = new Canvas { Width = view.Width, Height = view.Height, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F)) };
        if (behind is not null)
        {
            backdrop = true;
            canvas.Children.Add(PlacedImage(behind, new PixelRect(_detectView.X, _detectView.Y, _detectView.Width, _detectView.Height), view));
        }
        canvas.Children.Add(img);
        if (kept is not null) PutCardPicture(img, kept);
        AddGrid(id, area, colour, stroke / 2, new PixelRect(view.X, view.Y, 0, 0), GridOpacity, canvas);
        var frame = new Rectangle
        {
            Width = area.Width, Height = area.Height, Stroke = new SolidColorBrush(colour), StrokeThickness = stroke,
            StrokeDashArray = mark is null or { Mark: DetectMark.Waiting } ? [3, 2] : null, IsHitTestVisible = false,
        };
        Canvas.SetLeft(frame, area.X - view.X);
        Canvas.SetTop(frame, area.Y - view.Y);
        canvas.Children.Add(frame);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Viewbox { Child = canvas, Width = view.Width * k, Height = view.Height * k, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top });
        if (Locator(area, colour, behind) is { } locator) row.Children.Add(locator);
        return row;
    }

    /// <summary>The whole game screen, small, with the box filled in its colour: where on screen the reader looks.</summary>
    UIElement? Locator(PixelRect area, Color colour, ImageSource? behind)
    {
        if (_detectView.Width <= 0 || _detectView.Height <= 0) return null;
        var screen = new Canvas { Width = _detectView.Width, Height = _detectView.Height, Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F)) };
        double minimum = _detectView.Height / LocatorHeight * 3;   // a tiny box stays a visible dot
        var spot = new Rectangle { Width = Math.Max(minimum, area.Width), Height = Math.Max(minimum, area.Height), Fill = new SolidColorBrush(colour) };
        Canvas.SetLeft(spot, area.X - _detectView.X);
        Canvas.SetTop(spot, area.Y - _detectView.Y);
        if (behind is not null) screen.Children.Add(new System.Windows.Controls.Image { Source = behind, Width = screen.Width, Height = screen.Height, Opacity = 0.6 });
        screen.Children.Add(spot);
        return new Border
        {
            Child = new Viewbox { Child = screen, Height = LocatorHeight, Stretch = Stretch.Uniform }, Margin = new(Tokens.Space.Gap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, BorderThickness = new(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
        };
    }

    static System.Windows.Controls.Image PlacedImage(ImageSource source, PixelRect at, PixelRect view)
    {
        var img = new System.Windows.Controls.Image { Source = source, Width = at.Width, Height = at.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
        Canvas.SetLeft(img, at.X - view.X);
        Canvas.SetTop(img, at.Y - view.Y);
        return img;
    }

    /// <summary>The game screen saved when 스킬창 was first read on this game-area size; null when none is saved.</summary>
    ImageSource? Backdrop()
    {
        if (_backdropDemo is { } demo) return demo;
        if (_readingStore is not { } store || _detectView.Width <= 0 || _detectView.Height <= 0) return null;
        var path = store.BackdropPath(_detectView);
        if (!File.Exists(path)) return null;
        var at = File.GetLastWriteTimeUtc(path);
        if (_backdrop is { } kept && kept.For == _detectView && kept.At == at) return kept.Picture;
        var picture = ReadingShotStore.Load(path, _detectView) is var (loaded, _) ? loaded : null;   // unreadable or another size: no backdrop
        _backdrop = (_detectView, at, picture);
        return picture;
    }

    /// <summary>A picture at its own screen place inside the card's view (a Live frame can cover more or less than the box).</summary>
    void PutCardPicture(System.Windows.Controls.Image img, DetectPicture picture)
    {
        img.Source = BitmapOf(picture);
        if (img.Parent is not Canvas) return;   // no place known: the bare picture
        (img.Width, img.Height) = (picture.Area.Width, picture.Area.Height);
        Canvas.SetLeft(img, picture.Area.X - _mapLiveView.X);
        Canvas.SetTop(img, picture.Area.Y - _mapLiveView.Y);
    }
}
