using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>아이콘으로 표시 keeps each 직업각인's latest icon per slot in its own folder next to settings.json.</summary>
public sealed class IconStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "icons-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    static Image Icon(byte shade) { var img = new Image(4, 3); Array.Fill(img.Bgra, shade); return img; }

    [Fact]
    public void A_saved_icon_loads_back_by_slot_and_a_new_one_replaces_it()
    {
        var store = new IconStore(_dir);
        store.Save("바드", "절실한구원", SlotId.일반1, Icon(10));
        store.Save("바드", "절실한구원", SlotId.각성기, Icon(20));
        store.Save("바드", "절실한구원", SlotId.일반1, Icon(30));

        var icons = new IconStore(_dir).Load("바드", "절실한구원");
        Assert.Equal(2, icons.Count);
        Assert.Equal(Icon(30).Bgra, icons[SlotId.일반1].Bgra);
        Assert.Equal(Icon(20).Bgra, icons[SlotId.각성기].Bgra);
    }

    [Fact]
    public void Each_직업각인_keeps_its_own_icons()
    {
        var store = new IconStore(_dir);
        store.Save("바드", "절실한구원", SlotId.일반1, Icon(10));
        store.Save("바드", "진실된용맹", SlotId.일반2, Icon(20));
        Assert.Equal([SlotId.일반1], store.Load("바드", "절실한구원").Keys);
        Assert.Equal([SlotId.일반2], store.Load("바드", "진실된용맹").Keys);
    }

    [Fact]
    public void Clearing_empties_one_직업각인_only()
    {
        var store = new IconStore(_dir);
        store.Save("바드", "절실한구원", SlotId.일반1, Icon(10));
        store.Save("바드", "진실된용맹", SlotId.일반1, Icon(20));
        store.Clear("바드", "절실한구원");
        Assert.Empty(store.Load("바드", "절실한구원"));
        Assert.Single(store.Load("바드", "진실된용맹"));
    }

    [Fact]
    public void A_damaged_or_unknown_file_is_skipped()
    {
        var store = new IconStore(_dir);
        store.Save("바드", "절실한구원", SlotId.일반1, Icon(10));
        var folder = Path.Combine(_dir, "바드_절실한구원");
        File.WriteAllBytes(Path.Combine(folder, "일반2.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(folder, "notaslot.bin"), Icon(5).ToBytes());
        Assert.Equal([SlotId.일반1], store.Load("바드", "절실한구원").Keys);
    }

    [Fact]
    public void Nothing_saved_loads_as_empty() => Assert.Empty(new IconStore(_dir).Load("바드", "절실한구원"));
}
