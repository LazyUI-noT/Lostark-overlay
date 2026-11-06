using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Domain;
using Geometry = GaugeOverlay.Core.Vision.Geometry;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 화면·인식 → 감지 (under the screen): per slot the crop, the last <see cref="Reading"/> and the <see cref="SlotState"/>
/// (ARCHITECTURE.md §4.1), then the buff and debuff boxes and the boss's counts. Fed through <see cref="Update"/> and
/// <see cref="UpdateExtra"/> while it is shown.</summary>
public sealed class DiagnosticsPanel : Grid
{
    readonly Dictionary<SlotId, (System.Windows.Controls.Image Crop, TextBlock Reading, TextBlock State)> _rows = [];

    public DiagnosticsPanel()
    {
        foreach (var w in new[] { 70.0, 110, 150, 1 })
            ColumnDefinitions.Add(new ColumnDefinition { Width = w == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        AddRow(["칸", "화면", "읽은 값", "상태"], "");   // the header shows with any group
        foreach (var id in SlotInfo.All)
        {
            var crop = new System.Windows.Controls.Image { Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(crop, BitmapScalingMode.NearestNeighbor);
            TextBlock T() => new() { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas, Malgun Gothic"), TextWrapping = TextWrapping.Wrap };
            var row = (crop, T(), T());
            _rows[id] = row;
            AddRow([new TextBlock { Text = id.ToString(), VerticalAlignment = VerticalAlignment.Center }, crop, row.Item2, row.Item3], DetectPlace.Slots);
        }
        // The buff and debuff boxes and the boss's counts, fed by UpdateExtra while their reads run.
        foreach (var key in Enumerable.Range(1, Geometry.MaxBuffBoxCount).Select(i => $"버프 {i}")
                     .Concat(Enumerable.Range(1, Geometry.DebuffBoxCount).Select(i => $"디버프 {i}")).Append("보스"))
        {
            var crop = new System.Windows.Controls.Image { Height = key == "보스" ? 40 : 60, MaxWidth = 150, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(crop, BitmapScalingMode.NearestNeighbor);
            TextBlock T() => new() { Text = "—", VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas, Malgun Gothic"), TextWrapping = TextWrapping.Wrap };
            var row = (crop, T(), T());
            _extra[key] = row;
            AddRow([new TextBlock { Text = key, VerticalAlignment = VerticalAlignment.Center }, crop, row.Item2, row.Item3],
                key.StartsWith("디버프") ? DetectPlace.Debuffs : key.StartsWith("버프") ? DetectPlace.Buffs : DetectPlace.Boss);
        }
    }

    readonly Dictionary<string, (System.Windows.Controls.Image Crop, TextBlock Reading, TextBlock State)> _extra = [];

    /// <summary>Shows the latest read of a buff or debuff box ("버프 1", "디버프 3") or of the boss's counts ("보스").</summary>
    public void UpdateExtra(string key, Core.Domain.Image crop, string reading, string state)
    {
        if (!_extra.TryGetValue(key, out var r)) return;
        r.Crop.Source = crop.Width > 0 && crop.Height > 0
            ? BitmapSource.Create(crop.Width, crop.Height, 96, 96, PixelFormats.Bgr32, null, crop.Bgra, crop.Stride) : null;
        r.Reading.Text = reading;
        r.State.Text = state == "" ? "—" : state;
    }

    /// <summary>Shows the latest data for one slot; any argument may be null when not known yet.</summary>
    public void Update(SlotId slot, Core.Domain.Image? crop, Reading? reading, SlotState? state)
    {
        var r = _rows[slot];
        if (crop is not null)
            r.Crop.Source = BitmapSource.Create(crop.Width, crop.Height, 96, 96, PixelFormats.Bgr32, null, crop.Bgra, crop.Stride);
        r.Reading.Text = reading is { } x ? $"{x.Kind} {x.Value}\nt={x.At.TotalSeconds:0.000}" : "—";
        r.State.Text = state is { } s
            ? $"{s.Phase}{(s.Estimated ? " (추정)" : "")}" +
              (s.EndsAt is { } end ? $"\n끝 {end.TotalSeconds:0.000} / 전체 {s.Total?.TotalSeconds:0.0}s" : "") +
              (s.Phase == SlotPhase.LongCooldown ? $"\n{s.Minutes}m" : "")
            : "—";
    }

    readonly List<(string Group, UIElement Cell)> _cells = [];

    /// <summary>Shows only the rows of <paramref name="group"/> (<see cref="DetectPlace"/>'s groups), or nothing when null.</summary>
    public void ShowOnly(string? group)
    {
        foreach (var (g, cell) in _cells) cell.Visibility = group is not null && (g == "" || g == group) ? Visibility.Visible : Visibility.Collapsed;
        Visibility = group is null ? Visibility.Collapsed : Visibility.Visible;
    }

    void AddRow(object[] cells, string group)
    {
        bool header = group == "";
        int row = RowDefinitions.Count;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < cells.Length; c++)
        {
            var e = cells[c] as UIElement ?? new TextBlock { Text = (string)cells[c], FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal };
            if (e is FrameworkElement f) f.Margin = new(0, 4, 12, 4);
            _cells.Add((group, e));
            Grid.SetRow(e, row);
            Grid.SetColumn(e, c);
            Children.Add(e);
        }
    }
}
