using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

public sealed class StagingSlotsTests
{
    [Fact]
    public void A_region_reuses_a_texture_of_its_size_wherever_it_sits_in_the_list()
    {
        // The 암수 strip was second (after the boss's HP text) and is now first: same texture, nothing new on the GPU.
        var pool = new (int W, int H)[] { (300, 20), (576, 118) };
        Assert.Equal([1], StagingSlots.Assign(pool, [(576, 118)]));
        Assert.Equal([1, 0], StagingSlots.Assign(pool, [(576, 118), (300, 20)]));
    }

    [Fact]
    public void Two_regions_of_one_size_take_two_textures()
    {
        var pool = new (int W, int H)[] { (40, 12) };
        Assert.Equal([0, 1], StagingSlots.Assign(pool, [(40, 12), (40, 12)]));   // 1: a new one, after the pool
    }

    [Fact]
    public void A_new_size_gets_a_new_texture_and_the_others_stay_for_later()
    {
        var pool = new (int W, int H)[] { (100, 50), (200, 60) };
        Assert.Equal([2], StagingSlots.Assign(pool, [(120, 50)]));
        Assert.Equal([2, 3], StagingSlots.Assign(pool, [(1, 1), (2, 2)]));   // new ones numbered in order
    }

    [Fact]
    public void An_empty_request_takes_nothing() => Assert.Empty(StagingSlots.Assign([(1, 1)], []));
}
