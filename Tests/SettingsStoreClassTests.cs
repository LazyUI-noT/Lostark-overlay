using System.Text.Json;
using System.Text.Json.Nodes;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>A 0.22 file opens unchanged and 0.22 still reads a 0.23 file (class-profiles.plan.md, "Migration + downgrade").
/// Fixtures/settings/settings-0.22.json was saved by the 0.22 code (commit 7490fcc): 프로필 1 active, playing 전투태세
/// (its own spot 500 × 1.4), 고독한기사 at 300, 바드 at 222 (개별 off since, so 0.22 put it back on at the next start), the
/// shared spot 100, 투명도 0.75 live (0.8 stored, stale); 프로필 2: 전투태세 900, 고독한기사 700, shared 650, 투명도 0.5,
/// gauge fill #00FF00. One 게이지 bar edit (워로드/전투태세 구분선 3).</summary>
public class SettingsStoreClassTests : IDisposable
{
    const string Lone = "워로드 · 고독한기사", Combat = "워로드 · 전투태세", Bard = "바드 · 절실한구원";
    static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings", "settings-0.22.json");
    readonly string _dir = Path.Combine(Path.GetTempPath(), "go-class-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    Settings Load022()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.Copy(Fixture, path);
        return new SettingsStore(path).Load(out _);
    }

    static void Go(Settings s, string? key)
    {
        s.ActiveCharacter = key;
        s.SyncClass();
    }

    // (profile, character, spot, width, 투명도) as 0.22 showed them
    public static TheoryData<int, string?, double, double, double> Shown022 => new()
    {
        { 0, Combat, 500, 1.4, 0.75 },
        { 0, Lone, 300, 1.0, 0.75 },
        { 0, Bard, 222, 1.0, 0.75 },
        { 0, null, 100, 1.0, 0.75 },
        { 1, Combat, 900, 1.0, 0.5 },
        { 1, Lone, 700, 1.0, 0.5 },
        { 1, null, 650, 1.0, 0.5 },
        { 1, Bard, 650, 1.0, 0.5 },   // never placed in 프로필 2: the shared spot
    };

    [Theory]
    [MemberData(nameof(Shown022))]
    public void Upgrade_022_file_shows_same_in_every_profile(int profile, string? character, double left, double width, double opacity)
    {
        var s = Load022();
        Assert.False(s.SyncClass());   // at start: still 전투태세, what was on screen
        Assert.Equal((500.0, 0.75), (s.Overlay.Left, s.Overlay.Opacity));

        s.SwitchProfile(profile);
        Go(s, character);

        Assert.Equal((left, width, opacity), (s.Overlay.Left, s.Overlay.Width, s.Overlay.Opacity));
        Assert.Equal(3, s.GaugeBars[GaugeBar.Key("워로드", "전투태세")].SplitThickness);   // one bar for all profiles, as before
        Assert.Equal(profile == 1 ? "#00FF00" : new Settings().Gauge.Fill, s.Gauge.Fill);
    }

    [Fact]
    public void SubclassPlaces_become_class_entries()
    {
        var s = Load022();

        var p = s.Profiles[1];
        Assert.Equal(new[] { Combat, Lone }.Order(), p.Classes!.Keys.Order());
        Assert.Equal(900, p.Classes[Combat].Overlay.Left);
        Assert.Equal(0.5, p.Classes[Combat].Opacity);   // the profile's look, as 0.22 showed it
        Assert.All(s.Profiles, x => Assert.NotNull(x.GaugeBars));
    }

    [Fact]
    public void Switching_to_a_migrated_profile_keeps_the_gauge_bars()
    {
        var s = Load022();

        Assert.All(s.Profiles, p => Assert.NotNull(p.GaugeBars));
        s.SwitchProfile(3);   // never visited in 0.22: its bars are the ones every profile had
        Assert.Equal(3, s.GaugeBars[GaugeBar.Key("워로드", "전투태세")].SplitThickness);
    }

    [Fact]
    public void A_profile_without_gauge_bars_keeps_the_live_ones()
    {
        var s = new Settings();
        GaugeBar.Edit(s, "워로드", "전투태세").SplitThickness = 3;
        s.Profiles[1].GaugeBars = null;   // only a hand-built Settings: a loaded file has every profile's

        s.SwitchProfile(1);

        Assert.Equal(3, s.GaugeBars[GaugeBar.Key("워로드", "전투태세")].SplitThickness);
    }

    [Fact]
    public void A_spot_equal_to_the_shared_one_needs_no_entry()
    {
        var s = Load022();

        Assert.Empty(s.Profiles[2].Classes!);
    }

    [Fact]
    public void Mirror_keeps_subclass_places()
    {
        var s = Load022();
        Go(s, Lone);
        s.Overlay.Left = 333;
        Go(s, Combat);

        var p = s.Profiles[0];
        Assert.All(p.Classes!, kv => Assert.Equal(kv.Value.Overlay.Left, p.SubclassPlaces[kv.Key].Left));
        Assert.Equal(333, p.SubclassPlaces[Lone].Left);
    }

    [Fact]
    public void Downgrade_022_reads_023_file()
    {
        var s = Load022();
        Go(s, Lone);
        s.Overlay.Opacity = 0.4;   // 고독한기사's own look now
        Go(s, Combat);
        var path = Path.Combine(_dir, "settings.json");
        new SettingsStore(path).Save(s);

        var saved = JsonNode.Parse(File.ReadAllText(path))!;
        var old = JsonNode.Parse(File.ReadAllText(Fixture))!;
        // every property 0.22 reads is still where it looks for it (it skips the new ones)
        Assert.All(((JsonObject)old["Profiles"]![0]!).Select(kv => kv.Key), k => Assert.True(((JsonObject)saved["Profiles"]![0]!).ContainsKey(k), k));
        Assert.All(((JsonObject)old).Select(kv => kv.Key), k => Assert.True(((JsonObject)saved).ContainsKey(k), k));
        // the live top level is the current class's, with its key
        Assert.Equal(Combat, (string?)saved["PlaceKey"]);
        Assert.Equal((500.0, 0.75), ((double)saved["Overlay"]!["Left"]!, (double)saved["Overlay"]!["Opacity"]!));
        // 개별 spots survive through the mirror
        Assert.Equal(300, (double)saved["Profiles"]![0]!["SubclassPlaces"]![Lone]!["Left"]!);
        Assert.Equal(700, (double)saved["Profiles"]![1]!["SubclassPlaces"]![Lone]!["Left"]!);
    }

    [Fact]
    public void A_null_class_entry_from_a_hand_edit_is_dropped()
    {
        var s = Load022();
        var json = JsonNode.Parse(JsonSerializer.Serialize(s, SettingsStore.Json))!;
        json["Profiles"]![0]!["Classes"]![Lone] = null;

        var back = SettingsStore.Normalize(json.Deserialize<Settings>(SettingsStore.Json)!);

        Assert.DoesNotContain(Lone, back.Profiles[0].Classes!.Keys);
    }
}
