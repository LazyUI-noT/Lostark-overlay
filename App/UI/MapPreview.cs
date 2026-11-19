using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;
using Picture = System.Windows.Controls.Image;

namespace GaugeOverlay.App.UI;

/// <summary>The card a 표시 map box shows on hover: its window in each display style, side by side, labeled and in sync
/// (user, 2026-11-10), recorded on the sample screen (Assets/map, made by tools/make-map-gifs.ps1, listed in map.json), and the
/// reader's stored capture with its mark. A box shows whichever it has.</summary>
internal static class MapPreview
{
    /// <summary>A row wider than this wraps (four 막대-sized tiles fit; the meter's four go two by two).</summary>
    internal const double RowWidth = 880, CaptureHeight = 130, TileGap = 10;
    /// <summary>tools/make-map-gifs.ps1 records 10 frames a second.</summary>
    static readonly TimeSpan FrameTime = TimeSpan.FromMilliseconds(100);
    /// <summary>The last window's tiles decoded (each keeps its frames' changes, about 1–4 MB), so a second hover is instant.</summary>
    static (string Key, (string Label, GifTile Tile)[] Tiles)? _decoded;
    static Dictionary<string, (string Label, string File)[]>? _manifest;

    /// <returns>Null for a box with no GIF and no reader.</returns>
    /// <param name="gifs">map.json's key: the window's part, or Boss / Shadow for the labels over the game's own bar (recorded
    /// over footage, user 2026-11-10).</param>
    /// <param name="needs">What a switched-off box needs (<see cref="Needs"/>), above the GIF.</param>
    public static UIElement? Card(string title, string? gifs, Detection? mark, ImageSource? capture, TimeSpan now, UIElement? needs = null)
    {
        UIElement? gif = gifs is { } key ? Row(key) : null;
        if (gif is null && mark is null && needs is null) return null;
        var body = new StackPanel { MaxWidth = RowWidth };
        body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, Tokens.Space.Gap) });
        if (needs is not null) body.Children.Add(needs);
        if (gif is not null) body.Children.Add(gif);
        if (mark is { } d)
        {
            if (capture is not null) body.Children.Add(new Picture { Source = capture, MaxHeight = CaptureHeight, Margin = new(0, gif is null ? 0 : 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left });
            body.Children.Add(UiBuild.Muted(new TextBlock { Text = Caption(d, capture is not null, now), FontSize = Tokens.Font.Hint, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Tight, 0, 0) }));
        }
        return Frame(body);
    }

    /// <summary>The 감지 중 chip's card (user, 2026-11-10): no GIF, only the reader's area as the screen shows it now.
    /// <paramref name="picture"/> starts on the stored moment (if any) and the caller swaps in each Live frame.</summary>
    public static UIElement LiveCard(string title, Detection? mark, FrameworkElement place, TimeSpan now, bool backdrop = false)
    {
        var body = new StackPanel { MaxWidth = RowWidth };
        body.Children.Add(new TextBlock { Text = $"{title} · 읽는 자리", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, Tokens.Space.Gap) });
        place.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(place);
        string state = mark is { Mark: DetectMark.Waiting } ? "아직 찾는 중: 점선 칸은 게임 설정 크기로 잡은 읽을 자리입니다." : mark is { } d ? Caption(d, true, now) : "";
        if (backdrop) state += (state.Length > 0 ? "\n" : "") + "뒤 화면: 스킬창을 처음 읽은 순간입니다.";
        body.Children.Add(UiBuild.Muted(new TextBlock { Text = "Live: 지금 화면 (1초에 2번)" + (state.Length > 0 ? $"\n{state}" : "")
            + "\n누르면 화면 인식 설정으로 이동.", FontSize = Tokens.Font.Hint, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Tight, 0, 0) }));
        return Frame(body);
    }

    static readonly Color Met = Color.FromRgb(0x2F, 0x9E, 0x44);

    /// <summary>What switched-off box <paramref name="id"/> needs, section by section (<see cref="FeatureMap.Needs"/>): ✓ on, ✗ off,
    /// "또는" between alternatives, a box brought in by an off one indented under it. Null when the box is on or needs nothing.</summary>
    public static UIElement? Needs(Settings s, string id, bool hasKey)
    {
        if (FeatureMap.Boxes.First(b => b.Id == id).Column == 0 || FeatureMap.Satisfied(s, id, hasKey)) return null;
        var sections = FeatureMap.Needs(s, id, hasKey);
        if (sections.Count == 0) return null;
        bool ways = sections.Any(x => x.Role is MapNeedRole.Main or MapNeedRole.Fallback);
        var list = new StackPanel { Margin = new(0, 0, 0, Tokens.Space.Gap) };
        list.Children.Add(UiBuild.Muted(new TextBlock { Text = "꺼짐", FontSize = Tokens.Font.Hint }));
        foreach (var section in sections)
        {
            string head = section.Role switch
            {
                MapNeedRole.Common => ways ? "공통 · 어느 쪽이든 필요" : "필수",
                MapNeedRole.Main => "기본",
                MapNeedRole.Fallback => "대체 · 기본이 없을 때",
                _ => "선택 · 있으면 더 좋음",
            };
            if (section.Role != MapNeedRole.Optional) head += section.Met ? " ✓" : " ✗";
            list.Children.Add(new TextBlock { Text = head, FontWeight = FontWeights.SemiBold, Margin = new(0, Tokens.Space.Tight, 0, 0), Foreground = new SolidColorBrush(FeatureMapView.RoleColourOf(section.Role)) });
            foreach (var row in section.Rows)
            {
                var line = new TextBlock { Margin = new(row.Depth * 14, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
                if (row.Depth > 0) line.Inlines.Add(UiBuild.MutedRun(new System.Windows.Documents.Run("└ ")));
                for (int i = 0; i < row.Alternatives.Count; i++)
                {
                    if (i > 0) line.Inlines.Add(UiBuild.MutedRun(new System.Windows.Documents.Run(" 또는 ")));
                    bool on = FeatureMap.Satisfied(s, row.Alternatives[i], hasKey);
                    var mark = new System.Windows.Documents.Run(on ? "✓ " : "✗ ");
                    if (on) mark.Foreground = new SolidColorBrush(Met);
                    else mark.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, Themes.DangerBrushKey);
                    line.Inlines.Add(mark);
                    line.Inlines.Add(new System.Windows.Documents.Run(FeatureMap.NeedTitle(row.Alternatives[i])));
                }
                if (row.Optional && section.Role != MapNeedRole.Optional) line.Inlines.Add(UiBuild.MutedRun(new System.Windows.Documents.Run(" (선택)")));
                list.Children.Add(line);
            }
        }
        list.Children.Add(UiBuild.Muted(new TextBlock { Text = "칸을 펼쳐 스위치로 켜거나 ⚙에서 켭니다.", FontSize = Tokens.Font.Hint, Margin = new(0, Tokens.Space.Tight, 0, 0) }));
        return list;
    }

    static Border Frame(UIElement body)
    {
        var card = new Border { Child = body, Padding = new(Tokens.Space.Block), CornerRadius = new(Tokens.Radius.Card), BorderThickness = new(1) };
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

    /// <summary>The window's styles side by side, each labeled (a window with one style: no label), all moved to the same
    /// frame by one clock so they play in sync (Windows' animations off: their first frames). A tile that can't be read is
    /// left out; null when none can.</summary>
    static UIElement? Row(string key)
    {
        var tiles = Tiles(key);
        if (tiles.Length == 0) return null;
        var row = new WrapPanel { MaxWidth = RowWidth };
        foreach (var (label, tile) in tiles)
        {
            var cell = new StackPanel { Margin = new(0, 0, TileGap, TileGap) };
            cell.Children.Add(new Picture { Source = tile.Picture, Width = tile.Picture.PixelWidth, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
            if (label.Length > 0) cell.Children.Add(UiBuild.Muted(new TextBlock { Text = label, FontSize = Tokens.Font.Hint, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, Tokens.Space.Tight, 0, 0) }));
            row.Children.Add(cell);
        }
        foreach (var (_, tile) in tiles) tile.Show(0);
        if (!UiMotion.On) return row;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = FrameTime };
        timer.Tick += (_, _) =>
        {
            int frame = (int)(clock.Elapsed / FrameTime);
            foreach (var (_, tile) in tiles) tile.Show(frame);
        };
        row.Loaded += (_, _) => { clock.Restart(); timer.Start(); };
        row.Unloaded += (_, _) => timer.Stop();
        return row;
    }

    static (string Label, GifTile Tile)[] Tiles(string key)
    {
        if (_decoded is { } kept && kept.Key == key) return kept.Tiles;
        var tiles = new List<(string, GifTile)>();
        foreach (var (label, file) in Manifest().GetValueOrDefault(key, []))
            try
            {
                using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/map/{file}")).Stream;
                tiles.Add((label, new GifTile(stream)));
            }
            catch (Exception ex) when (ex is IOException or FileFormatException or NotSupportedException or ArgumentException or NullReferenceException or InvalidCastException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)   // a card without it beats a crash on hover
            {
                System.Diagnostics.Trace.WriteLine($"[map] no GIF {file} for {key}: {ex.Message}");
            }
        _decoded = (key, [.. tiles]);
        return _decoded.Value.Tiles;
    }

    /// <summary>map.json from tools/make-map-gifs.ps1: each window's styles as {Label, File}, in order. Empty if unreadable.</summary>
    static Dictionary<string, (string Label, string File)[]> Manifest()
    {
        if (_manifest is not null) return _manifest;
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/map/map.json")).Stream;
            var json = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MapTile[]>>(stream) ?? [];
            _manifest = json.ToDictionary(p => p.Key, p => p.Value.Select(t => (t.Label, t.File)).ToArray());
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or NullReferenceException)
        {
            System.Diagnostics.Trace.WriteLine($"[map] no map.json: {ex.Message}");
            return [];   // not kept: the next hover tries again
        }
        return _manifest;
    }

    sealed record MapTile(string Label, string File);
}
