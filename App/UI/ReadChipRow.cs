using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The 기능 맵's top row (rail-split.plan.md 4): one round chip per reader on 화면 인식 설정
/// (<see cref="DetectPlace.Chips"/>), coloured by its 읽는 중 mark, grey while off. A click opens 화면 인식 설정 on that box;
/// the map's boxes keep their marks but no longer link there.</summary>
sealed class ReadChipRow : WrapPanel
{
    const double ChipRadius = 10;   // half the chip's height: a pill
    readonly Action<string> _open;
    string? _drawn;

    public ReadChipRow(Action<string> open)
    {
        _open = open;
        Margin = new(0, Tokens.Space.Gap, 0, 0);
    }

    /// <summary>The marks from the engine; the chips are rebuilt only when one changed.</summary>
    public void Set(IReadOnlyDictionary<string, Detection> board)
    {
        var drawn = string.Join(",", DetectPlace.Chips.Select(c => board.GetValueOrDefault(c.Id)?.Mark));
        if (drawn == _drawn) return;
        _drawn = drawn;
        Children.Clear();
        // Named, so the chips read as links to that page and not as map boxes (critique, 2026-11-17).
        Children.Add(UiBuild.Muted(new TextBlock { Text = "화면 인식 설정", FontSize = Tokens.Font.Hint, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, Tokens.Space.Gap, Tokens.Space.Tight) }));
        foreach (var (id, name) in DetectPlace.Chips) Children.Add(Chip(id, name, board.GetValueOrDefault(id)));
    }

    UIElement Chip(string id, string name, Detection? d)
    {
        // 디버프 줄 has no mark of its own: its chip only leads to the box.
        var (mark, colour, state) = id == DetectPlace.DebuffRow ? ("", FeatureMapView.Unmarked, "위치만 맞춤") : FeatureMapView.Look(d);
        var face = new Border
        {
            CornerRadius = new(ChipRadius),
            Padding = new(Tokens.Space.Gap, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair),
            Background = new SolidColorBrush(colour),
            Child = new TextBlock { Text = mark.Length > 0 ? $"{name} {mark}" : name, FontSize = Tokens.Font.Hint, Foreground = new SolidColorBrush(RaidBadgeView.InkOn(colour)) },
        };
        string says = $"{name}: {state}. 누르면 화면 인식 설정에서 엽니다.";
        var go = UiBuild.Bare<Button>(face, says, $"readchip:{id}");
        go.Margin = new(0, 0, Tokens.Space.Tight, Tokens.Space.Tight);
        go.ToolTip = says;
        go.Click += (_, _) => _open(id);
        return go;
    }
}
