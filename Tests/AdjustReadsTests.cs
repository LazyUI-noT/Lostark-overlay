using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>What the adjust layer draws green: a part that reads well now. Nothing seen is not wrong (a row with no buffs up).</summary>
public class AdjustReadsTests
{
    static readonly TimeSpan T = TimeSpan.FromSeconds(10);
    static BuffState[] Buffs(bool anyActive) =>
        [.. Enum.GetValues<BuffKind>().Select((k, i) => i == 0 && anyActive ? BuffState.Absent(k, T) with { Active = true } : BuffState.Absent(k, T))];
    static readonly DebuffMark Mark = new(T, null);

    [Fact]
    public void Reads_null_rows_when_not_read()
    {
        var r = AdjustReads.From(2.0, null, null);
        Assert.True(r.Bar);
        Assert.Null(r.Buff);
        Assert.Null(r.Debuff);
        Assert.Null(r.Boss);
    }

    [Fact]
    public void The_panel_reads_as_told_and_is_unread_by_default()
    {
        Assert.True(AdjustReads.From(0, null, null, panel: true).Of(AdjustPart.전분));
        Assert.Null(AdjustReads.From(0, null, null).Of(AdjustPart.전분));
    }

    [Fact]
    public void Reads_the_bar_from_its_score() => Assert.False(AdjustReads.From(0.5, null, null).Bar);

    [Fact]
    public void Reads_buff_when_any_is_active()
    {
        Assert.True(AdjustReads.From(0, Buffs(true), null).Buff);
        Assert.False(AdjustReads.From(0, Buffs(false), null).Buff);
    }

    /// <summary>A buff carried on its estimate over missed reads isn't being read: the row may be off its icons.</summary>
    [Fact]
    public void An_estimated_buff_does_not_read_well()
    {
        BuffState[] estimated = [.. Buffs(true).Select(b => b.Active ? b with { Estimated = true } : b)];
        Assert.False(AdjustReads.From(0, estimated, null).Buff);
    }

    [Fact]
    public void Reads_debuff_when_any_mark_is_seen()
    {
        Assert.False(AdjustReads.From(0, null, DebuffStatus.None).Debuff);
        Assert.True(AdjustReads.From(0, null, DebuffStatus.None with { Lightning = Mark }).Debuff);
        Assert.True(AdjustReads.From(0, null, DebuffStatus.None with { Duelist = new DuelMark(T) }).Debuff);
    }

    [Fact]
    public void Reads_boss_from_its_counts()
    {
        Assert.False(AdjustReads.From(0, null, DebuffStatus.None).Boss);
        Assert.True(AdjustReads.From(0, null, DebuffStatus.None with { BossBlue = 3 }).Boss);
    }
}
