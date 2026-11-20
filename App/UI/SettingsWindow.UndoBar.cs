using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The 되돌리기 / 다시 실행 buttons (preview-images.plan.md 1): pinned at the canvas's bottom right with the zoom %, and
/// under every 전투 오버레이 tab's preview. All call <see cref="Step"/>, so they share one history and one count.</summary>
public partial class SettingsWindow
{
    readonly List<(Button Undo, Button Redo)> _undoBars = [];

    /// <summary>The two buttons; with <paramref name="canvas"/>, its zoom % too, on a card over the canvas.</summary>
    FrameworkElement UndoBar(OverlayCanvas? canvas = null)
    {
        var undo = UndoButton("되돌리기", "Ctrl+Z", () => { if (!PlaceBack()) Step(undo: true); });
        var redo = UndoButton("다시 실행", "Ctrl+Y 또는 Ctrl+Shift+Z", () => { if (!_placeWanted) Step(undo: false); });
        _undoBars.Add((undo, redo));
        RefreshUndoBars();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { undo, redo } };
        if (canvas is null) return row;
        var zoom = Label("", 0.7);
        (zoom.Margin, zoom.VerticalAlignment, zoom.MinWidth, zoom.TextAlignment) = (new(0, 0, Tokens.Space.Gap, 0), VerticalAlignment.Center, 40, TextAlignment.Right);
        void ShowZoom() => zoom.Text = $"{canvas.Zoom * 100:0}%";
        canvas.ViewChanged += ShowZoom;
        ShowZoom();
        row.Children.Insert(0, zoom);
        var card = new Border { Padding = new(Tokens.Space.Gap, Tokens.Space.Tight, Tokens.Space.Tight, Tokens.Space.Tight), CornerRadius = new(Tokens.Radius.Card), BorderThickness = new(1), Child = row };
        card.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        card.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        return card;
    }

    static Button UndoButton(string name, string keys, Action click)
    {
        var b = new Button { Content = name, ToolTip = $"{name} ({keys})", MinWidth = 76, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new(0, 0, Tokens.Space.Tight, 0), Padding = new(Tokens.Space.Gap, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair), Tag = name };
        b.Click += (_, _) => click();
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }

    /// <summary>Each button shows how many steps it has and greys out at none.</summary>
    void RefreshUndoBars()
    {
        foreach (var (undo, redo) in _undoBars)
        {
            Show(undo, _history.UndoCount);
            Show(redo, _history.RedoCount);
        }

        static void Show(Button b, int count)
        {
            b.IsEnabled = count > 0;
            b.Content = count > 0 ? $"{b.Tag} {count}" : b.Tag;
            System.Windows.Automation.AutomationProperties.SetName(b, $"{b.Tag} {count}");
        }
    }
}
