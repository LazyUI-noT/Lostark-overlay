using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>리소스 검사 (Phase 6): a button above the 표시 map; while it is on, each map box shows its CPU and a table under the map
/// shows the same, the app's GPU and the gaps between the game's captured frames. Measured only while the 표시 page shows; kept
/// on this PC, never sent or written to a record.</summary>
public sealed partial class SettingsWindow
{
    /// <summary>Hands the engine the frame-gap tracker (null: stop).</summary>
    public Action<Core.Domain.FrameGaps?>? WatchFrames { get; set; }

    bool _checkOn;
    ResourceMeter? _check;
    ResourceSample? _checkSample;
    bool _checkDemo;
    readonly StackPanel _checkTable = new() { Margin = new(0, Tokens.Space.Gap, 0, 0), Visibility = Visibility.Collapsed };
    readonly DispatcherTimer _checkTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>The 리소스 검사 button, made with the map.</summary>
    ToggleButton CheckButton()
    {
        if (_checkTimer.Tag is null) { _checkTimer.Tag = true; _checkTimer.Tick += (_, _) => TakeCheck(); }
        var button = new ToggleButton { Content = "리소스 검사", Padding = new(Tokens.Space.Gap, Tokens.Space.Tight, Tokens.Space.Gap, Tokens.Space.Tight), Margin = new(Tokens.Space.Block, 0, 0, 0), IsChecked = _checkOn };
        button.Click += (_, _) =>
        {
            _checkOn = button.IsChecked == true;
            if (!_checkOn) _checkSample = null;
            CheckRun();
            Dispatcher.BeginInvoke(FillShow);   // the map is built with or without its CPU lines
        };
        return button;
    }

    /// <summary>Measuring runs only while the check is on and the 표시 page shows.</summary>
    void CheckRun()
    {
        bool run = _checkOn && _secShow.IsVisible && !_checkDemo;
        if (run && _check is null) { _check = new ResourceMeter(g => WatchFrames?.Invoke(g)); _checkTimer.Start(); }
        else if (!run && _check is not null) { _checkTimer.Stop(); _check.Dispose(); _check = null; }
    }

    void TakeCheck()
    {
        if (_check is null) return;
        _checkSample = _check.Sample();
        ShowCheck();
    }

    /// <summary>The map's CPU lines and the table from the latest sample (also after the page was refilled).</summary>
    void ShowCheck()
    {
        _checkTable.Visibility = _checkOn ? Visibility.Visible : Visibility.Collapsed;
        _checkTable.Children.Clear();
        if (!_checkOn) return;
        var note = (string text, double opacity) => opacity < 1 ? UiBuild.Muted(new TextBlock { Text = text, FontSize = Tokens.Font.Hint, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Hair, 0, Tokens.Space.Hair) }) : new TextBlock { Text = text, FontSize = Tokens.Font.Hint, TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Hair, 0, Tokens.Space.Hair) };
        if (_checkSample is not { } sample)
        {
            _checkTable.Children.Add(note("재는 중입니다. 2초마다 갱신됩니다.", 0.8));
            return;
        }
        var cores = sample.Report.Cores;
        string Value(ResourceRow r) => !r.Ran ? NotRun : (r.Approx ? "≤ " : "") + ResourceCheck.Label(r.Percent, cores);
        _map?.SetResources(sample.Report.Rows.ToDictionary(r => r.Id, Value));
        // Boxes over their own active time (user, 2026-11-18), the shared rows and the total over the whole check.
        _checkTable.Children.Add(note($"CPU 값은 기능 박스마다 표시합니다. 앱 전체 CPU {ResourceCheck.Label(sample.Report.Total, cores)}" + (sample.Gpu is { } gpu ? $", GPU {gpu:0.0}%." : ", GPU 측정할 수 없음."), 1));
        var gaps = sample.Gaps;
        if (gaps.Top.Count == 0)
        {
            _checkTable.Children.Add(note("프레임 지연 현상 관찰 결과: 없음.", 1));
            return;
        }
        _checkTable.Children.Add(new TextBlock { Text = $"프레임 지연 현상 관찰 결과 (상위 {gaps.Top.Count}건, 앱 처리 평균 {gaps.AverageAppMs:0} ms)", FontWeight = FontWeights.SemiBold, Margin = new(0, Tokens.Space.Tight, 0, Tokens.Space.Hair) });
        for (int i = 0; i < gaps.Top.Count; i++)
        {
            var g = gaps.Top[i];
            string did = g.Parts.Count == 0 ? "없음" : string.Join(", ", g.Parts.Select(p => $"{p.Part} {p.Ms:0} ms"));
            _checkTable.Children.Add(note($"{i + 1}: {g.Gap.TotalMilliseconds:0} ms, 앱이 처리한 것: {did}", 1));
        }
        _checkTable.Children.Add(note("프레임 드랍 중 앱이 실제 한 결과값을 표시합니다.", 0.7));
    }

    const string NotRun = "실행 안 됨";   // a box that never ran while the check was on, not 0%

    /// <summary>--demo --map-hover-test: logs what hovering over the map's boxes costs ([map] line in the trace).</summary>
    internal void DemoHoverTest() => Dispatcher.BeginInvoke(() => _map?.HoverCost(), DispatcherPriority.ApplicationIdle);

    /// <summary>--demo --resources: the check on with sample figures (the demo has no engine).</summary>
    internal void DemoResources()
    {
        _checkOn = _checkDemo = true;
        var parts = new Dictionary<string, double>
        {
            ["스킬 쿨다운" + ResourceCheck.CpuTag] = 24, ["각성기 표시" + ResourceCheck.CpuTag] = 1.2, ["버프" + ResourceCheck.CpuTag] = 9, ["암수" + ResourceCheck.CpuTag] = 3.5,
            ["DPS 미터기 (보스 HP · 전분)" + ResourceCheck.CpuTag] = 6, ["화면 캡처 (새 프레임 대기 포함)" + ResourceCheck.CpuTag] = 30, ["그리기: 스킬 창" + ResourceCheck.CpuTag] = 8,
            ["Live" + ResourceCheck.CpuTag] = 0.8, ["던전 이름 (Windows OCR)"] = 0.4, ["지도 선 강조" + ResourceCheck.CpuTag] = 0.1,
        };
        var gaps = new Core.Domain.FrameGaps();
        gaps.Add(TimeSpan.FromSeconds(1), new Dictionary<string, double>());
        gaps.Add(TimeSpan.FromSeconds(1.31), new Dictionary<string, double> { ["스킬 쿨다운"] = 22, ["암수"] = 31, ["화면 캡처 (새 프레임 대기 포함)"] = 250 });
        gaps.Add(TimeSpan.FromSeconds(1.45), new Dictionary<string, double> { ["버프"] = 4 });
        var active = new Dictionary<string, double> { ["skill"] = 100, ["buff"] = 100, ["amsu"] = 35, ["meter"] = 40, ["skillview"] = 100, ["dungeon"] = 12, ["live"] = 100, ["map"] = 100 };
        _checkSample = new ResourceSample(ResourceCheck.Build(parts, 100, 100, 12, active), 6.4, gaps.Summary(), 100);
        FillShow();
        Dispatcher.BeginInvoke(() => _checkTable.BringIntoView(), DispatcherPriority.ApplicationIdle);   // under the map: scroll to it for a screenshot
    }
}
