using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class HpAnchorTests
{
    const long BellHp = 1_197_954_508_761;
    static readonly HpAnchor Bell = new("belltower-g2", BellHp);

    [Fact]
    public void The_same_max_hp_keeps_the_gate_without_a_name_read()
    {
        Assert.Equal("belltower-g2", HpAnchor.Match(Bell, BellHp));
        Assert.Null(HpAnchor.Match(Bell, 25_807_352_833));   // another boss
        Assert.Null(HpAnchor.Match(Bell, null));             // unreadable: the name read decides
        Assert.Null(HpAnchor.Match(null, BellHp));
    }

    [Fact]
    public void A_name_read_that_recognition_confirms_anchors_a_max_hp_read_twice()
    {
        Assert.Equal(Bell, HpAnchor.Next(null, recognised: "belltower-g2", ocrGate: "belltower-g2", maxHp: BellHp, lastMaxHp: BellHp));
        Assert.Null(HpAnchor.Next(null, "belltower-g2", "belltower-g2", maxHp: BellHp, lastMaxHp: BellHp + 1));   // one read, maybe a misread digit
        Assert.Null(HpAnchor.Next(null, recognised: null, "belltower-g2", BellHp, BellHp));        // one name read isn't a recognition
        Assert.Null(HpAnchor.Next(null, recognised: "act4-g1", "belltower-g2", BellHp, BellHp));   // disagrees with what's recognised
        Assert.Null(HpAnchor.Next(null, "belltower-g2", "belltower-g2", maxHp: null, lastMaxHp: null));
    }

    [Fact]
    public void A_matching_or_unreadable_hp_keeps_the_anchor_and_a_changed_one_drops_it()
    {
        Assert.Equal(Bell, HpAnchor.Next(Bell, "belltower-g2", ocrGate: null, maxHp: BellHp, lastMaxHp: BellHp));
        Assert.Equal(Bell, HpAnchor.Next(Bell, "belltower-g2", ocrGate: null, maxHp: null, lastMaxHp: BellHp));   // a moment without the bar text
        Assert.Null(HpAnchor.Next(Bell, "belltower-g2", ocrGate: null, maxHp: 500_000_000_000, lastMaxHp: BellHp));   // next phase or boss
        Assert.Equal(Bell with { MaxHp = 500_000_000_000 }, HpAnchor.Next(Bell, "belltower-g2", "belltower-g2", 500_000_000_000, 500_000_000_000));
    }

    [Fact]
    public void A_check_read_naming_another_gate_drops_the_anchor()
    {
        // Two misreads once anchored the wrong gate: the 30 s check read names the right one.
        Assert.Null(HpAnchor.Next(Bell, recognised: "belltower-g2", ocrGate: "act4-g1", maxHp: BellHp, lastMaxHp: BellHp));
        Assert.Null(HpAnchor.Next(Bell, recognised: "belltower-g2", ocrGate: "act4-g1", maxHp: null, lastMaxHp: null));
        Assert.Equal(Bell, HpAnchor.Next(Bell, "belltower-g2", ocrGate: "belltower-g2", maxHp: BellHp, lastMaxHp: BellHp));   // agrees
    }
}
