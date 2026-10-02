using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>스킬 등록 keeps each 직업각인's pool of 일반 slot crops in its own folder next to settings.json.</summary>
public sealed class SkillPoolStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "skillpool-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    static List<Image> Slots(params int[] n) => [.. n.Select(i => Fixture.Load($"Nocd_일반{i}").Frames[0])];

    [Fact]
    public void A_submit_adds_new_skills_and_a_second_submit_only_the_ones_not_in_the_pool()
    {
        var store = new SkillPoolStore(_dir);
        Assert.Equal((4, 4), store.Add("워로드", "고독한기사", Slots(1, 2, 3, 4)));
        Assert.Equal((2, 6), store.Add("워로드", "고독한기사", Slots(3, 4, 5, 6)));
        Assert.Equal(6, new SkillPoolStore(_dir).Load("워로드", ["고독한기사", "전투태세"])["고독한기사"].Count);
        Assert.Empty(store.Load("워로드", ["고독한기사", "전투태세"])["전투태세"]);
    }

    [Fact]
    public void Clearing_empties_one_직업각인s_pool_only()
    {
        var store = new SkillPoolStore(_dir);
        store.Add("워로드", "고독한기사", Slots(1, 2));
        store.Add("워로드", "전투태세", Slots(3));
        store.Clear("워로드", "고독한기사");
        var pools = store.Load("워로드", ["고독한기사", "전투태세"]);
        Assert.Empty(pools["고독한기사"]);
        Assert.Single(pools["전투태세"]);
    }

    [Fact]
    public void A_damaged_file_is_skipped()
    {
        var store = new SkillPoolStore(_dir);
        store.Add("워로드", "고독한기사", Slots(1));
        File.WriteAllBytes(Path.Combine(_dir, "워로드_고독한기사", "broken.bin"), [1, 2, 3]);
        Assert.Single(store.Load("워로드", ["고독한기사"])["고독한기사"]);
    }
}
