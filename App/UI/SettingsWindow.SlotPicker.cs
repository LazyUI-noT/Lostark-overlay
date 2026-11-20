using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.App.UI;

/// <summary>The popup of the 5 image slots (preview-images.plan.md §2): thumbnails, the one under the pointer large below.
/// 이미지 선택 picks on a click; 덮어쓰기 shows the slot preselected and takes it with its own button.</summary>
public partial class SettingsWindow
{
    const int ThumbWidth = 168, LargeWidth = 504;

    /// <summary>Opens the picker over <paramref name="anchor"/>. <paramref name="confirmText"/> null: a click picks at once;
    /// else a click marks the slot and that button picks it.</summary>
    void ShowSlotPicker(UIElement anchor, string title, int preselect, string? confirmText, Action<int> picked)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -Tokens.Space.Gap };
        var accent = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
        var stroke = TryFindResource(StrokeBrushKey) as Brush ?? Brushes.Gray;
        int mark = preselect;
        var large = new System.Windows.Controls.Image { Width = LargeWidth, Height = LargeWidth * 9 / 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        var largeNote = UiBuild.Muted(new TextBlock { Text = "이미지 위에 포인터를 올리면 크게 보입니다.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var cells = new Border[OverlayProfileCount];
        var pictures = new BitmapSource?[OverlayProfileCount];
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var confirm = new Button { Content = confirmText, Margin = new(0, Tokens.Space.Gap, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Visibility = confirmText is null ? Visibility.Collapsed : Visibility.Visible };
        Themes.Primary(confirm);

        void Mark()
        {
            for (int i = 0; i < cells.Length; i++) cells[i].BorderBrush = i == mark && confirmText is not null ? accent : stroke;
            for (int i = 0; i < cells.Length; i++) cells[i].BorderThickness = new(i == mark && confirmText is not null ? 3 : 1);
            confirm.Content = confirmText is null ? null : $"이미지 {mark + 1}에 {confirmText}";
        }
        void Show(int slot)
        {
            large.Source = pictures[slot];
            largeNote.Text = pictures[slot] is null ? $"이미지 {slot + 1}: 비어 있습니다." : $"이미지 {slot + 1}";
        }

        for (int i = 0; i < cells.Length; i++)
        {
            int slot = i;
            pictures[i] = Thumbnail(ScreenShots.PathOf(ScreenShots.Dir, slot));
            var image = new System.Windows.Controls.Image { Width = ThumbWidth, Height = ThumbWidth * 9 / 16, Stretch = Stretch.Uniform, Source = pictures[i] };
            var users = _s.ProfilesOnSlot(slot);
            var label = new TextBlock { Text = $"이미지 {slot + 1}" + (pictures[i] is null ? " · 비어 있음" : ""), HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, Tokens.Space.Hair, 0, 0) };
            var button = new Button { Content = new StackPanel { Children = { image, label } }, Padding = new(Tokens.Space.Gap), Background = Brushes.Transparent, BorderThickness = new(0) };
            System.Windows.Automation.AutomationProperties.SetName(button, $"이미지 {slot + 1}" + (pictures[i] is null ? ", 비어 있음" : ""));
            if (users.Length > 0) button.ToolTip = "쓰는 프로필: " + string.Join(", ", users.Select(u => u + 1));
            button.MouseEnter += (_, _) => Show(slot);
            button.GotKeyboardFocus += (_, _) => Show(slot);
            button.Click += (_, _) =>
            {
                if (confirmText is null) { popup.IsOpen = false; picked(slot); return; }
                mark = slot;
                Mark();
            };
            cells[i] = new Border { Child = button, CornerRadius = new(Tokens.Radius.Card), Margin = new(0, 0, i < cells.Length - 1 ? Tokens.Space.Gap : 0, 0) };
            row.Children.Add(cells[i]);
        }
        confirm.Click += (_, _) => { popup.IsOpen = false; picked(mark); };
        Mark();
        Show(preselect);

        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, Tokens.Space.Gap) },
                row,
                new Grid { Margin = new(0, Tokens.Space.Gap, 0, 0), Children = { largeNote, large } },
                confirm,
            },
        };
        var panel = new Border { Child = body, Padding = new(Tokens.Space.Card), CornerRadius = new(Tokens.Radius.Card), BorderThickness = new(1), BorderBrush = stroke, Background = TryFindResource(NavBrushKey) as Brush ?? Brushes.Black };
        popup.Child = panel;
        popup.IsOpen = true;
    }

    const int OverlayProfileCount = Core.Domain.OverlayProfile.Count;

    /// <summary>A small copy of a slot's file for the popup; null when empty or unreadable.</summary>
    static BitmapSource? Thumbnail(string path, int decodeWidth = LargeWidth * 2)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            (image.UriSource, image.CacheOption, image.CreateOptions, image.DecodePixelWidth) = (new Uri(path), BitmapCacheOption.OnLoad, BitmapCreateOptions.IgnoreImageCache, decodeWidth);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
