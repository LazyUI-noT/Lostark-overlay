using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The box at the top left of 내 오버레이's canvas (user, 2026-11-01): the screen and every in-game size the reads
/// depend on, in one place: 해상도 and 비율 (ScreenSummary), 버프·디버프 크기 and 타겟 UI 버프 크기 (보스) (changed here), HUD 크기 (shown;
/// 맞추기 opens 화면·인식 so a refit isn't started by accident).</summary>
public partial class SettingsWindow
{
    readonly Border _screenBox = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(10),
        Padding = new(10, 6, 10, 8), CornerRadius = new(6), BorderThickness = new(1),
    };

    /// <summary>The canvas with the box over its top left corner. The Fluent themes leave the nav brush unset, so an
    /// opaque Fluent base sits under it: the map art never shows through the box (user, 2026-11-10).</summary>
    Grid WithScreenBox(UIElement canvas)
    {
        _screenBox.SetResourceReference(Border.BackgroundProperty, NavBrushKey);
        _screenBox.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        var solid = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = _screenBox.Margin,
            CornerRadius = _screenBox.CornerRadius, Child = _screenBox,
        };
        _screenBox.Margin = default;
        solid.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        FillScreenBox();
        return new Grid { Children = { canvas, solid } };
    }

    /// <summary>Refilled with 스킬창 맞추기's page (FillFit): a fit, 직접 입력, 버프 크기 or the game window changed.</summary>
    void FillScreenBox()
    {
        var facts = ScreenSummary.Of(_s, _gameRunning);
        var refit = new Button { Content = "맞추기", Margin = new(8, 0, 0, 0), Padding = new(6, 0, 6, 0), ToolTip = "화면·인식 → 스킬창 맞추기를 엽니다." };
        refit.Click += (_, _) => Go(AreaFit);
        var rows = new (string Name, FrameworkElement Value)[]
        {
            ("해상도", Label(facts.Resolution)),
            ("비율", Label(facts.Ratio)),
            ("버프·디버프 크기", BuffSizeCombo()),
            ("HUD 크기", new StackPanel { Orientation = Orientation.Horizontal, Children = { Label(facts.Hud), refit } }),
            ("타겟 UI 버프 크기 (보스)", BossSizeCombo()),
        };
        var grid = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = GridLength.Auto } } };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var name = Label(rows[i].Name, 0.7);
            (name.Margin, name.VerticalAlignment, rows[i].Value.VerticalAlignment) = (new(0, 2, 12, 2), VerticalAlignment.Center, VerticalAlignment.Center);
            Grid.SetRow(name, i);
            Grid.SetRow(rows[i].Value, i);
            Grid.SetColumn(rows[i].Value, 1);
            grid.Children.Add(name);
            grid.Children.Add(rows[i].Value);
        }
        _screenBox.Child = grid;
    }
}
