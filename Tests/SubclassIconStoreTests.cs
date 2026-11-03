using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>직업각인 등록 keeps the user's own 초각스 icon per 직업각인, for classes shipped without one (Profiles/subclass).</summary>
public sealed class SubclassIconStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "subclass-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    static Image Icon(byte shade) { var img = new Image(4, 4); Array.Fill(img.Bgra, shade); return img; }

    [Fact]
    public void A_registered_icon_loads_back_and_a_new_one_replaces_it()
    {
        var store = new SubclassIconStore(_dir);
        store.Save("가디언나이트", "드레드로어", Icon(10));
        store.Save("가디언나이트", "드레드로어", Icon(30));

        var icons = new SubclassIconStore(_dir).Load("가디언나이트", ["드레드로어", "업화의계승자"]);
        Assert.Equal(["드레드로어"], icons.Keys);
        Assert.Equal(Icon(30).Bgra, icons["드레드로어"].Bgra);
    }

    [Fact]
    public void Nothing_registered_loads_nothing() =>
        Assert.Empty(new SubclassIconStore(_dir).Load("가디언나이트", ["드레드로어"]));
}
