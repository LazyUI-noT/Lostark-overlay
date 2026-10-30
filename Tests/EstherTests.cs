using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The 에스더 plan (종막 EX for now): each use is a damage (억) going in at a bar count or an HP %.</summary>
public sealed class EstherTests
{
    [Fact]
    public void Uses_count_once_their_point_is_reached()
    {
        IReadOnlyList<EstherUse> plan = [new("공아만 3칸", 1870, 60), new("공아만 1칸", 1480, 30), new("히든 아만 3칸", 3779, 10)];
        Assert.Equal(0, Esther.Applied(plan, 70));
        Assert.Equal(1870, Esther.Applied(plan, 60));
        Assert.Equal(1870 + 1480 + 3779, Esther.Applied(plan, 10));
        Assert.Equal(1870 + 1480 + 3779, Esther.Total(plan));
    }

    [Fact]
    public void An_unset_or_empty_use_counts_nothing()
    {
        Assert.Equal(0, Esther.Applied([new("x", 0, 50)], 10));
        Assert.Equal(0, Esther.Total([]));
    }
}
