using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Records are named by the boss's max HP (one per gate and difficulty): learned the first time from the dungeon
/// panel, used from then on; a new HP (a nerf, another difficulty) is learned again.</summary>
public sealed class BossBookTests
{
    static FightRecord Fight(string? gate, string? difficulty, long? max) =>
        new(DateTime.Now, gate, 60, [], "나", 3, null, [], [], [new RecordBoss(1, 10, 5, max)], [], Difficulty: difficulty);

    [Fact]
    public void A_fight_named_by_hand_teaches_its_hp()
    {
        Assert.Equal((Belgardin, new BossBookEntry("벨가르딘", "하드")),
            BossBook.Taught(Fight(null, null, Belgardin), Fight("벨가르딘", "하드", Belgardin)));
        Assert.Equal((Belgardin, new BossBookEntry("벨가르딘", "하드")),
            BossBook.Taught(Fight("벨가르딘", "노말", Belgardin), Fight("벨가르딘", "하드", Belgardin)));
    }

    [Fact]
    public void An_edit_that_leaves_the_name_or_has_no_hp_or_name_teaches_nothing()
    {
        Assert.Null(BossBook.Taught(Fight("벨가르딘", "노말", Belgardin), Fight("벨가르딘", "노말", Belgardin) with { Cleared = true }));
        Assert.Null(BossBook.Taught(Fight(null, null, null), Fight("벨가르딘", "노말", null)));
        Assert.Null(BossBook.Taught(Fight("벨가르딘", "노말", Belgardin), Fight(" ", "노말", Belgardin)));
    }

    const long Belgardin = 1_950_577_229_952, Act1 = 7_752_830_141;
    static readonly Dictionary<long, BossBookEntry> Empty = [];
    static readonly DungeonInfo Panel = new(DungeonKind.Raid, "죽음의 계율자, 벨가르딘", "노말");

    [Fact]
    public void A_known_hp_names_the_fight_whatever_the_panel_read()
    {
        var book = new Dictionary<long, BossBookEntry> { [Belgardin] = new("죽음의 계율자, 벨가르딘", "노말") };
        Assert.Equal(("죽음의 계율자, 벨가르딘", "노말"), BossBook.Resolve(book, Belgardin, Panel with { Title = "죽음의 계율자, 벨가르닌", Difficulty = null }, null));
    }

    [Fact]
    public void An_unknown_hp_takes_the_known_gate_then_the_panel()
    {
        Assert.Equal(("3막 · 3관문", "싱글"), BossBook.Resolve(Empty, Act1, Panel with { Difficulty = "싱글" }, "3막 · 3관문"));
        Assert.Equal(("죽음의 계율자, 벨가르딘", "노말"), BossBook.Resolve(Empty, Act1, Panel, null));
        Assert.Equal((null, null), BossBook.Resolve(Empty, null, null, null));
    }

    [Fact]
    public void An_unknown_hp_is_learned_from_the_panel_and_a_known_one_is_kept()
    {
        Assert.Equal(new BossBookEntry("죽음의 계율자, 벨가르딘", "노말"), BossBook.ToLearn(Empty, Belgardin, Panel));
        var book = new Dictionary<long, BossBookEntry> { [Belgardin] = new("벨가르딘 (내 이름)", "노말") };
        Assert.Null(BossBook.ToLearn(book, Belgardin, Panel));                       // a name already there (maybe edited) stays
        Assert.Null(BossBook.ToLearn(Empty, Belgardin, Panel with { Title = null }));   // nothing read to learn
        Assert.Null(BossBook.ToLearn(Empty, null, Panel));                              // no HP read
    }

    [Fact]
    public void The_fights_hp_is_the_max_seen_most()
    {
        RecordBoss B(long? max) => new(1, 0, 1, max);
        Assert.Equal(Belgardin, BossBook.FightHp([B(Belgardin), B(Belgardin), B(Act1), B(null)]));
        Assert.Null(BossBook.FightHp([B(null)]));
    }

    [Fact]
    public void The_book_survives_a_save_and_a_load_and_a_blank_entry_is_dropped()
    {
        var s = new Settings();
        s.BossBook[Belgardin] = new("죽음의 계율자, 벨가르딘", "노말");
        s.BossBook[Act1] = new(" ", null);   // a hand edit with no name
        var back = SettingsStore.Normalize(JsonSerializer.Deserialize<Settings>(JsonSerializer.SerializeToUtf8Bytes(s, SettingsStore.Json), SettingsStore.Json)!);
        Assert.Equal(new BossBookEntry("죽음의 계율자, 벨가르딘", "노말"), Assert.Single(back.BossBook).Value);
    }
}
