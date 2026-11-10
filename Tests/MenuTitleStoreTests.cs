using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

public sealed class MenuTitleStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "menu-title-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_saved_title_loads_back_for_its_screen_size()
    {
        var store = new MenuTitleStore(_dir);
        byte[] title = [1, 2, 3, 4, 5, 6];
        store.Save(1920, 1080, title);
        Assert.Equal(title, store.Load(1920, 1080, title.Length));
    }

    [Fact]
    public void Another_screen_size_has_none()
    {
        var store = new MenuTitleStore(_dir);
        store.Save(1920, 1080, [1, 2, 3]);
        Assert.Null(store.Load(2560, 1440, 3));
    }

    [Fact]
    public void A_title_of_another_length_counts_as_none()
    {
        var store = new MenuTitleStore(_dir);
        store.Save(1920, 1080, [1, 2, 3]);
        Assert.Null(store.Load(1920, 1080, 4));
    }

    [Fact]
    public void The_same_title_again_is_not_written_and_another_replaces_it()
    {
        var store = new MenuTitleStore(_dir);
        store.Save(1920, 1080, [1, 2, 3]);
        var file = Directory.GetFiles(_dir).Single();
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, old);

        store.Save(1920, 1080, [1, 2, 3]);
        Assert.Equal(old, File.GetLastWriteTimeUtc(file));

        store.Save(1920, 1080, [4, 5, 6]);
        Assert.Equal([4, 5, 6], store.Load(1920, 1080, 3));
    }

    [Fact]
    public void A_missing_folder_counts_as_none()
    {
        Assert.Null(new MenuTitleStore(_dir).Load(1920, 1080, 3));
    }
}
