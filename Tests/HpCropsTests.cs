using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

public class HpCropsTests
{
    [Fact]
    public void A_crop_is_named_by_gate_difficulty_and_hp()
    {
        Assert.Equal("act4-g1_하드_905014093444.png", HpCrops.FileName("act4-g1", "하드", 905_014_093_444));
    }

    [Fact]
    public void Characters_a_file_name_cannot_hold_are_replaced()
    {
        string name = HpCrops.FileName("a/b", "c:d", 1);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
    }

    [Fact]
    public void Only_crops_of_values_no_longer_listed_are_stale()
    {
        string kept = HpCrops.FileName("g", "하드", 10), gone = HpCrops.FileName("g", "하드", 9), other = "readme.txt";

        var stale = HpCrops.Stale([kept, gone, other], [("g", "하드", 10L)]);

        Assert.Equal([gone], stale);   // a file that isn't a crop is left alone
    }

    [Fact]
    public void Clean_deletes_the_crops_of_unlisted_values_and_keeps_the_rest()
    {
        string dir = Directory.CreateTempSubdirectory("hpcrops").FullName;
        try
        {
            string kept = HpCrops.FileName("g", "하드", 10), gone = HpCrops.FileName("g", "하드", 9);
            foreach (var f in new[] { kept, gone, "note.txt" }) File.WriteAllText(Path.Combine(dir, f), "");

            HpCrops.Clean([("g", "하드", 10L)], dir);

            Assert.Equal([kept, "note.txt"], Directory.EnumerateFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
