using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Shapes = System.Windows.Shapes;

namespace GaugeOverlay.App.UI;

/// <summary>새 캡처 (preview-images.plan.md §4): the button beside the image chips shows how many instant captures wait and glows
/// while any is unseen; its popup puts the new shots next to the 5 slots, compares a pick of each large (the shot shows the
/// slot's number, the slot a red dashed outline) and 바꾸기 puts the shot in the slot, 모두 버리기 empties the list.</summary>
public partial class SettingsWindow
{
    const int NewThumbWidth = 120, CompareWidth = 360;

    /// <summary>A countdown or capture under way (the 즉시 화면 캡처 key waits: the engine takes one screenshot request at a time).</summary>
    internal bool IsTaking => _taking;

    NewShots? _newShotList;
    readonly Button _newShotsButton = new() { Margin = new(0, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair), Content = "새 캡처", IsEnabled = false };

    /// <summary>The list the 즉시 화면 캡처 key fills (from App; null in the demo).</summary>
    public NewShots? NewShotList
    {
        set
        {
            if (_newShotList is not null) return;   // set once, from App
            _newShotList = value;
            if (value is not null) value.Changed += () => Dispatcher.BeginInvoke(RefreshNewShotsButton);   // raised off the UI thread
            RefreshNewShotsButton();
        }
    }

    /// <summary>The count on the button, and the glow while any shot is unseen.</summary>
    void RefreshNewShotsButton()
    {
        int count = _newShotList?.Entries.Count ?? 0, unseen = _newShotList?.Unseen ?? 0;
        _newShotsButton.Content = count == 0 ? "새 캡처" : $"새 캡처 {count}";
        _newShotsButton.IsEnabled = count > 0;
        _newShotsButton.ToolTip = count == 0
            ? "찍은 화면이 여기에 쌓입니다. 즉시 화면 캡처 키는 키·단축키에서 정합니다."
            : unseen > 0 ? $"아직 보지 않은 화면이 {unseen}장 있습니다." : "최근 20장까지 남깁니다.";
        if (unseen == 0)
        {
            _newShotsButton.Effect = null;
            return;
        }
        var glow = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 16, Opacity = 1, Color = (TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _newShotsButton.Effect = glow;
    }

    void ShowNewShotsPopup()
    {
        if (_newShotList is not { } list || list.Entries.Count == 0) return;
        var shots = list.Entries;
        list.MarkSeen();   // opened: nothing is unseen any more
        var popup = new Popup { PlacementTarget = _newShotsButton, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, VerticalOffset = -Tokens.Space.Gap };
        var accent = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
        var stroke = TryFindResource(StrokeBrushKey) as Brush ?? Brushes.Gray;
        var pick = new ReplacePick();

        var shotCells = new Border[shots.Count];
        var slotCells = new Border[OverlayProfileCount];
        var slotDash = new Shapes.Rectangle[OverlayProfileCount];
        var shotLarge = new System.Windows.Controls.Image { Width = CompareWidth, Height = CompareWidth * 9 / 16, Stretch = Stretch.Uniform };
        var slotLarge = new System.Windows.Controls.Image { Width = CompareWidth, Height = CompareWidth * 9 / 16, Stretch = Stretch.Uniform };
        var number = new TextBlock { FontSize = 72, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 8, Color = Colors.Black } };
        var shotNote = UiBuild.Muted(new TextBlock { Text = "새 캡처를 고르세요.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var slotNote = UiBuild.Muted(new TextBlock { Text = "바꿀 이미지를 고르세요.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var slotLargeDash = new Shapes.Rectangle { Stroke = Brushes.Red, StrokeThickness = 3, StrokeDashArray = [4, 3], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var replace = new Button { Content = "바꾸기", IsEnabled = false, Margin = new(0, 0, Tokens.Space.Gap, 0) };
        Themes.Primary(replace);
        var discard = new Button { Content = "모두 버리기", ToolTip = "새 캡처를 모두 지웁니다. 이미지 1–5는 그대로입니다." };

        void Outline(Border cell, bool on) { cell.BorderBrush = on ? accent : stroke; cell.BorderThickness = new(on ? 3 : 1); }
        void Refresh()
        {
            for (int i = 0; i < shotCells.Length; i++) Outline(shotCells[i], i == pick.Shot);
            for (int i = 0; i < slotCells.Length; i++) Outline(slotCells[i], i == pick.Slot);
            for (int i = 0; i < slotDash.Length; i++) slotDash[i].Visibility = i == pick.DashedSlot ? Visibility.Visible : Visibility.Collapsed;
            slotLargeDash.Visibility = pick.DashedSlot is null ? Visibility.Collapsed : Visibility.Visible;
            number.Text = pick.NumberOnShot?.ToString() ?? "";
            number.Visibility = pick.NumberOnShot is null ? Visibility.Collapsed : Visibility.Visible;
            replace.IsEnabled = pick.Ready;
            replace.Content = pick.Ready ? $"이미지 {pick.Slot + 1} 바꾸기" : "바꾸기";
        }

        var grid = new WrapPanel { MaxWidth = 5 * (NewThumbWidth + 2 * Tokens.Space.Gap + 4) };
        for (int i = 0; i < shots.Count; i++)
        {
            int index = i;
            var picture = Thumbnail(list.PathOf(shots[i]), NewThumbWidth * 2);
            var button = new Button { Content = new System.Windows.Controls.Image { Width = NewThumbWidth, Height = NewThumbWidth * 9 / 16, Stretch = Stretch.Uniform, Source = picture }, Padding = new(Tokens.Space.Hair), Background = Brushes.Transparent, BorderThickness = new(0) };
            System.Windows.Automation.AutomationProperties.SetName(button, $"새 캡처 {i + 1}");
            button.Click += (_, _) =>
            {
                pick = pick with { Shot = index };
                shotLarge.Source = Thumbnail(list.PathOf(shots[index]), CompareWidth * 2);
                shotNote.Visibility = Visibility.Collapsed;
                Refresh();
            };
            grid.Children.Add(shotCells[i] = new Border { Child = button, CornerRadius = new(Tokens.Radius.Card), Margin = new(0, 0, Tokens.Space.Gap, Tokens.Space.Gap) });
        }

        var slots = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < slotCells.Length; i++)
        {
            int slot = i;
            var path = ScreenShots.PathOf(ScreenShots.Dir, slot);
            bool filled = File.Exists(path);
            var image = new System.Windows.Controls.Image { Width = NewThumbWidth, Height = NewThumbWidth * 9 / 16, Stretch = Stretch.Uniform, Source = Thumbnail(path, NewThumbWidth * 2) };
            var label = new TextBlock { Text = $"이미지 {slot + 1}" + (filled ? "" : " · 비어 있음"), HorizontalAlignment = HorizontalAlignment.Center };
            slotDash[i] = new Shapes.Rectangle { Stroke = Brushes.Red, StrokeThickness = 2, StrokeDashArray = [4, 3], Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var content = new Grid { Children = { new StackPanel { Children = { image, label } }, slotDash[i] } };
            var button = new Button { Content = content, Padding = new(Tokens.Space.Hair), Background = Brushes.Transparent, BorderThickness = new(0) };
            System.Windows.Automation.AutomationProperties.SetName(button, $"이미지 {slot + 1}" + (filled ? "" : ", 비어 있음"));
            button.Click += (_, _) =>
            {
                pick = pick with { Slot = slot };
                slotLarge.Source = Thumbnail(path, CompareWidth * 2);
                slotNote.Text = $"이미지 {slot + 1}: 비어 있습니다.";
                slotNote.Visibility = filled ? Visibility.Collapsed : Visibility.Visible;
                Refresh();
            };
            slotCells[i] = new Border { Child = button, CornerRadius = new(Tokens.Radius.Card), Margin = new(0, 0, Tokens.Space.Gap, 0) };
            slots.Children.Add(slotCells[i]);
        }

        replace.Click += (_, _) =>
        {
            popup.IsOpen = false;
            if (pick is { Shot: { } s, Slot: { } t }) ReplaceWithShot(shots[s], t);
        };
        discard.Click += (_, _) =>
        {
            popup.IsOpen = false;
            if (MessageBox.Show(this, $"새 캡처 {shots.Count}장을 모두 버릴까요? 되돌릴 수 없습니다.", "새 캡처 모두 버리기", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) list.DiscardAll();
        };

        var compare = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new(0, Tokens.Space.Gap, 0, 0),
            Children =
            {
                new Grid { Margin = new(0, 0, Tokens.Space.Card, 0), Children = { shotNote, shotLarge, number } },
                new Grid { Children = { slotNote, slotLarge, slotLargeDash } },
            },
        };
        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "새 캡처", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, Tokens.Space.Gap) },
                new ScrollViewer { MaxHeight = 2 * (NewThumbWidth * 9 / 16 + 2 * Tokens.Space.Gap) + 8, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid },
                new TextBlock { Text = "바꿀 이미지", FontWeight = FontWeights.SemiBold, Margin = new(0, Tokens.Space.Gap, 0, Tokens.Space.Gap) },
                slots,
                compare,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, Tokens.Space.Gap, 0, 0), Children = { replace, discard } },
            },
        };
        popup.Child = new Border { Child = body, Padding = new(Tokens.Space.Card), CornerRadius = new(Tokens.Radius.Card), BorderThickness = new(1), BorderBrush = stroke, Background = TryFindResource(NavBrushKey) as Brush ?? Brushes.Black };
        Refresh();
        popup.IsOpen = true;
    }

    /// <summary>바꾸기: the shot becomes image <paramref name="slot"/> with its area; every profile on that slot shows it, so
    /// they are named first. A file is no undo step.</summary>
    void ReplaceWithShot(NewShot shot, int slot)
    {
        if (_newShotList is not { } list) return;
        var users = File.Exists(ScreenShots.PathOf(ScreenShots.Dir, slot)) ? _s.ProfilesOnSlot(slot).Select(p => p + 1).ToArray() : [];
        if (users.Length > 0 && MessageBox.Show(this, $"이미지 {slot + 1}을 쓰는 프로필: {string.Join(", ", users)}. 바꿀까요?", "이미지 바꾸기", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (!list.PutInSlot(shot, slot, ScreenShots.Dir, _s))
        {
            _canvasNote.Text = "새 캡처를 넣지 못했습니다. 파일이 없거나 저장할 수 없습니다.";
            return;
        }
        ResetBaseline();
        SettingsChanged?.Invoke();
        ShowCanvasShot();
        PreviewChanged();
    }
}
