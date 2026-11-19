using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>리소스 검사 (Phase 6): Perf parts onto map boxes, the CPU figure as one core (all cores), the frame-gap check, the GPU figure.</summary>
public class ResourceCheckTests
{
    const string Cpu = ResourceCheck.CpuTag;
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Every_part_the_map_names_goes_to_a_real_box_or_a_shared_row()
    {
        var ids = FeatureMap.Boxes.Select(b => b.Id).Concat(ResourceCheck.SharedRows.Keys).ToHashSet();
        foreach (var (part, id) in ResourceCheck.PartRows) Assert.Contains(id, ids);
    }

    [Theory]
    [InlineData("그리기: 스킬 창", "skillview")]   // OverlayRenderer's names, one per window kind
    [InlineData("그리기: 파티 창", "peers")]
    [InlineData("그리기: 전투자원 창", "ring")]
    [InlineData("그리기: 보스 체력 창", "boss")]   // OverlayTextWindow's
    [InlineData("그리기: 대난투 체력 창", "shadow")]
    public void Every_overlay_window_draw_counts_in_its_box(string part, string box)
    {
        var report = ResourceCheck.Build(new Dictionary<string, double> { [part + Cpu] = 1.0 }, 10, 10, 12);
        Assert.Equal(10, report.Rows.First(r => r.Id == box).Percent, 6);
    }

    [Fact]
    public void A_box_adds_up_its_parts_in_percent_of_one_core()
    {
        var parts = new Dictionary<string, double> { ["버프" + Cpu] = 1.2, ["그리기: 버프 창" + Cpu] = 0.6, ["단심" + Cpu] = 0.3 };
        var report = ResourceCheck.Build(parts, 10, 10, 12);
        Assert.Equal(18, report.Rows.First(r => r.Id == "buff").Percent, 6);
        Assert.Equal(3, report.Rows.First(r => r.Id == "judg").Percent, 6);
    }

    [Fact]
    public void Rendering_and_runtime_is_the_process_minus_the_parts_measured_in_cpu()
    {
        var parts = new Dictionary<string, double> { ["버프" + Cpu] = 1.0, ["버프"] = 5.0 };   // the wall figure is not subtracted
        var report = ResourceCheck.Build(parts, 3.0, 10, 12);
        Assert.Equal(20, report.Rows.First(r => r.Id == ResourceCheck.Render).Percent, 6);
        Assert.Equal(30, report.Total, 6);
    }

    [Fact]
    public void Rendering_and_runtime_never_goes_below_zero()
    {
        var report = ResourceCheck.Build(new Dictionary<string, double> { ["버프" + Cpu] = 5.0 }, 1.0, 10, 12);
        Assert.Equal(0, report.Rows.First(r => r.Id == ResourceCheck.Render).Percent);
    }

    [Fact]
    public void A_part_inside_another_is_not_counted_twice()
    {
        var parts = new Dictionary<string, double> { ["화면 캡처 (새 프레임 대기 포함)" + Cpu] = 2.0, ["화면 캡처: 복사·읽기" + Cpu] = 1.5 };
        var report = ResourceCheck.Build(parts, 4, 10, 12);
        Assert.Equal(20, report.Rows.First(r => r.Id == ResourceCheck.Capture).Percent, 6);
        Assert.Equal(20, report.Rows.First(r => r.Id == ResourceCheck.Render).Percent, 6);
    }

    [Fact]
    public void A_part_with_only_wall_time_is_marked_approximate_and_stays_out_of_the_subtraction()
    {
        var parts = new Dictionary<string, double> { ["던전 이름 (Windows OCR)"] = 0.5 };
        var report = ResourceCheck.Build(parts, 2.0, 10, 12);
        var row = report.Rows.First(r => r.Id == "dungeon");
        Assert.True(row.Approx);
        Assert.Equal(5, row.Percent, 6);
        Assert.Equal(20, report.Rows.First(r => r.Id == ResourceCheck.Render).Percent, 6);
    }

    [Fact]
    public void A_part_nobody_mapped_still_shows_in_its_own_row()
    {
        var report = ResourceCheck.Build(new Dictionary<string, double> { ["새 일" + Cpu] = 1.0 }, 1.0, 10, 12);
        Assert.Equal(10, report.Rows.First(r => r.Id == ResourceCheck.Other).Percent, 6);
    }

    [Theory]
    [InlineData(2.4, 12, "2.4% (0.20%)")]
    [InlineData(0, 12, "0.0% (0.00%)")]
    [InlineData(100, 8, "100.0% (12.50%)")]
    public void The_label_gives_one_core_then_all_cores(double percent, int cores, string text) =>
        Assert.Equal(text, ResourceCheck.Label(percent, cores));

    [Fact]
    public void A_gap_over_the_hitch_limit_is_counted_with_the_parts_that_ran_in_it()
    {
        var gaps = new FrameGaps();
        gaps.Add(S(10.000), new Dictionary<string, double>());
        gaps.Add(S(10.016), new Dictionary<string, double> { ["버프"] = 2 });
        gaps.Add(S(10.250), new Dictionary<string, double> { ["암수"] = 40, ["버프"] = 3, ["화면 캡처 (새 프레임 대기 포함)"] = 200 });
        var sum = gaps.Summary();
        Assert.Equal(3, sum.Frames);
        Assert.Equal(1, sum.Count);
        Assert.Equal(S(0.234), sum.Worst);
        Assert.Equal(["암수", "버프"], sum.Top[0].Parts.Select(p => p.Part).ToArray());   // the frame wait is not a part that ran
    }

    [Fact]
    public void A_long_silence_is_the_capture_pausing_not_a_hitch()
    {
        var gaps = new FrameGaps();
        gaps.Add(S(10), new Dictionary<string, double>());
        gaps.Add(S(30), new Dictionary<string, double>());
        Assert.Equal(0, gaps.Summary().Count);
        Assert.Equal(TimeSpan.Zero, gaps.Summary().Worst);
    }

    [Fact]
    public void The_worst_gap_keeps_its_own_parts_when_a_milder_one_follows()
    {
        var gaps = new FrameGaps();
        gaps.Add(S(1), new Dictionary<string, double>());
        gaps.Add(S(1.5), new Dictionary<string, double> { ["스킬 쿨다운"] = 30 });
        gaps.Add(S(1.7), new Dictionary<string, double> { ["버프"] = 10 });
        var sum = gaps.Summary();
        Assert.Equal(2, sum.Count);
        Assert.Equal("스킬 쿨다운", sum.Top[0].Parts[0].Part);
    }

    [Fact]
    public void Only_the_five_longest_gaps_are_kept_longest_first_with_the_app_average()
    {
        var gaps = new FrameGaps();
        double t = 1;
        gaps.Add(S(t), new Dictionary<string, double>());
        foreach (var ms in new[] { 150, 400, 120, 300, 200, 250, 110 })
            gaps.Add(S(t += ms / 1000.0), new Dictionary<string, double> { ["암수"] = ms / 10.0 });
        var sum = gaps.Summary();
        Assert.Equal(7, sum.Count);
        Assert.Equal([400, 300, 250, 200, 150], sum.Top.Select(g => (int)Math.Round(g.Gap.TotalMilliseconds)).ToArray());
        Assert.Equal(26, sum.AverageAppMs, 6);
    }

    [Fact]
    public void The_gpu_figure_is_the_busiest_engine_type_of_this_process_only()
    {
        var counters = new (string, double)[]
        {
            ("pid_100_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 5), ("pid_100_luid_0x0_0x1_phys_0_eng_1_engtype_3D", 2),
            ("pid_100_luid_0x0_0x1_phys_0_eng_2_engtype_Copy", 4), ("pid_1000_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 90),
            ("pid_200_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 50),
        };
        Assert.Equal(7, GpuLoad.Percent(counters, 100));
        Assert.Equal(0, GpuLoad.Percent(counters, 7));
    }
}

/// <summary>리소스 검사 over each box's active time (user, 2026-11-18): 암수 in town doesn't water its figure down.</summary>
public class ResourceCheckActiveTests
{
    const string Cpu = ResourceCheck.CpuTag;

    [Fact]
    public void A_box_is_averaged_over_the_time_it_was_active()
    {
        var parts = new Dictionary<string, double> { ["암수" + Cpu] = 1.0 };
        var report = ResourceCheck.Build(parts, 10, 100, 12, new Dictionary<string, double> { ["amsu"] = 10 });
        Assert.Equal(10, report.Rows.First(r => r.Id == "amsu").Percent, 6);
    }

    [Fact]
    public void Shared_rows_and_the_total_stay_over_the_whole_time()
    {
        var parts = new Dictionary<string, double> { ["암수" + Cpu] = 1.0, ["화면 캡처 (새 프레임 대기 포함)" + Cpu] = 5.0 };
        var report = ResourceCheck.Build(parts, 20, 100, 12, new Dictionary<string, double> { ["amsu"] = 10, [ResourceCheck.Capture] = 10 });
        Assert.Equal(5, report.Rows.First(r => r.Id == ResourceCheck.Capture).Percent, 6);
        Assert.Equal(14, report.Rows.First(r => r.Id == ResourceCheck.Render).Percent, 6);
        Assert.Equal(20, report.Total, 6);
    }

    [Fact]
    public void A_box_that_never_ran_is_listed_as_not_run()
    {
        var report = ResourceCheck.Build(new Dictionary<string, double> { ["버프" + Cpu] = 1.0 }, 10, 100, 12,
            new Dictionary<string, double> { ["buff"] = 50 });
        var amsu = report.Rows.First(r => r.Id == "amsu");
        Assert.False(amsu.Ran);
        Assert.True(report.Rows.First(r => r.Id == "buff").Ran);
    }

    [Fact]
    public void A_box_with_time_but_no_active_record_falls_back_to_the_whole_time()
    {
        var report = ResourceCheck.Build(new Dictionary<string, double> { ["버프" + Cpu] = 1.0 }, 10, 100, 12, new Dictionary<string, double>());
        var buff = report.Rows.First(r => r.Id == "buff");
        Assert.True(buff.Ran);
        Assert.Equal(1, buff.Percent, 6);
    }

    [Fact]
    public void Active_time_is_never_taken_as_longer_than_the_window()
    {
        var report = ResourceCheck.Build(new Dictionary<string, double> { ["버프" + Cpu] = 1.0 }, 10, 10, 12,
            new Dictionary<string, double> { ["buff"] = 13 });   // the grace after the last run reaches past "now"
        Assert.Equal(10, report.Rows.First(r => r.Id == "buff").Percent, 6);
    }

    [Theory]
    [InlineData("암수", "amsu")]
    [InlineData("보스 이름 (Windows OCR)", "boss")]
    [InlineData("그리기: 암수 창", "amsu")]
    [InlineData("화면 캡처 (새 프레임 대기 포함)", ResourceCheck.Capture)]
    [InlineData("새 일", ResourceCheck.Other)]
    public void A_part_is_kept_active_under_its_row(string part, string row) =>
        Assert.Equal(row, ResourceCheck.RowOf(part));
}
