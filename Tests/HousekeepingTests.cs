using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Things that must not pile up while the app runs or across versions (2026-11-04 leak check).</summary>
public class HousekeepingTests
{
    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void A_new_error_is_logged_in_full_and_its_repeats_once_a_minute()
    {
        var log = ErrorLog.None;
        (log, var first) = log.Seen("FileNotFound SharpGen", S(0));
        Assert.Equal(ErrorLog.Write.Full, first.Write);

        int summaries = 0, fulls = 0;
        for (double t = 0.5; t <= 120; t += 0.5)
        {
            (log, var again) = log.Seen("FileNotFound SharpGen", S(t));
            if (again.Write == ErrorLog.Write.Summary) summaries++;
            if (again.Write == ErrorLog.Write.Full) fulls++;
        }

        Assert.Equal(0, fulls);
        Assert.Equal(2, summaries);   // at 60 s and 120 s, not 240 traces
        Assert.Equal(ErrorLog.SlowPause, log.Pause);
    }

    [Fact]
    public void Another_error_or_a_good_step_logs_in_full_again()
    {
        var (log, _) = ErrorLog.None.Seen("A", S(0));
        (log, _) = log.Seen("A", S(1));

        Assert.Equal(ErrorLog.Write.Full, log.Seen("B", S(2)).Line.Write);
        Assert.Equal(ErrorLog.FastPause, ErrorLog.None.Pause);
    }

    [Fact]
    public void A_summary_says_how_many_were_left_out()
    {
        var log = ErrorLog.None;
        (log, _) = log.Seen("A", S(0));
        for (int t = 1; t < 60; t++) (log, _) = log.Seen("A", S(t));
        (_, var line) = log.Seen("A", S(60));

        Assert.Equal((ErrorLog.Write.Summary, 60), (line.Write, line.Repeats));
    }

    [Fact]
    public void Each_summary_counts_only_the_errors_since_the_last_line()
    {
        var log = ErrorLog.None;
        var counts = new List<int>();
        foreach (double t in new[] { 0, 30, 61, 62, 122 })
        {
            (log, var line) = log.Seen("A", S(t));
            if (line.Write == ErrorLog.Write.Summary) counts.Add(line.Repeats);
        }

        Assert.Equal([2, 2], counts);   // 30 + 61, then 62 + 122
    }

    [Fact]
    public void Undo_keeps_steps_within_a_total_size_as_well_as_a_count()
    {
        var history = new SettingsHistory(capacity: 100, maxChars: 25);
        foreach (var s in new[] { "aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc" }) history.Record(s);

        Assert.Equal("cccccccccc", history.Undo("now"));
        Assert.Equal("bbbbbbbbbb", history.Undo("x"));
        Assert.Null(history.Undo("y"));   // the oldest went to stay under 25 characters
    }

    [Fact]
    public void Undo_always_keeps_the_last_step_even_when_alone_it_is_over_the_size()
    {
        var history = new SettingsHistory(capacity: 100, maxChars: 5);
        history.Record("a big snapshot");

        Assert.Equal("a big snapshot", history.Undo("now"));
    }

    static readonly DateTime Now = new(2026, 11, 4, 15, 0, 0);
    const string Base = @"C:\Temp\.net\GaugeOverlay";

    [Fact]
    public void Old_extract_folders_go_but_never_the_one_in_use_or_a_new_one()
    {
        (string, DateTime)[] folders =
        [
            (Base + @"\old1", Now.AddDays(-3)),
            (Base + @"\mine", Now.AddDays(-3)),
            (Base + @"\fresh", Now.AddMinutes(-10)),   // another copy may be starting from it
        ];

        var stale = ExtractFolders.Stale(folders, Base + @"\mine;C:\Windows", Now);

        Assert.Equal([Base + @"\old1"], stale);
    }

    [Fact]
    public void Without_knowing_its_own_folder_nothing_is_deleted()
    {
        Assert.Empty(ExtractFolders.Stale([(Base + @"\old1", Now.AddDays(-3))], @"C:\Program Files\dotnet", Now));
        Assert.Empty(ExtractFolders.Stale([(Base + @"\old1", Now.AddDays(-3))], null, Now));
    }
}
