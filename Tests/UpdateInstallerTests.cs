using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

/// <summary>Installing an update: the running exe is renamed aside (Windows allows that), the new exe and Profiles are put
/// in its place, and anything that fails puts the old files back.</summary>
public sealed class UpdateInstallerTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "update-" + Guid.NewGuid().ToString("N"));
    string App => Path.Combine(_root, "app");
    string New => Path.Combine(_root, "update", "new");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    public UpdateInstallerTests()
    {
        Write(App, "GaugeOverlay.exe", "old exe");
        Write(Path.Combine(App, "Profiles"), "classes.json", "old classes");
        Write(App, "settings-like.txt", "kept");
        Write(New, "GaugeOverlay.exe", "new exe");
        Write(Path.Combine(New, "Profiles", "aden"), "gauge.json", "new gauge");
        Write(Path.Combine(New, "Profiles"), "classes.json", "new classes");
    }

    static void Write(string dir, string name, string text)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), text);
    }

    string Read(params string[] parts) => File.ReadAllText(Path.Combine([App, .. parts]));

    [Fact]
    public void A_swap_puts_the_new_exe_and_Profiles_in_place_and_keeps_the_old_ones_aside()
    {
        Assert.Null(UpdateInstaller.Swap(App, New));
        Assert.Equal("new exe", Read("GaugeOverlay.exe"));
        Assert.Equal("new classes", Read("Profiles", "classes.json"));
        Assert.Equal("new gauge", Read("Profiles", "aden", "gauge.json"));
        Assert.Equal("old exe", Read("GaugeOverlay.exe.old"));
        Assert.Equal("old classes", Read("Profiles.old", "classes.json"));
        Assert.Equal("kept", Read("settings-like.txt"));
    }

    [Fact]
    public void An_update_without_an_exe_changes_nothing()
    {
        File.Delete(Path.Combine(New, "GaugeOverlay.exe"));
        Assert.NotNull(UpdateInstaller.Swap(App, New));
        Assert.Equal("old exe", Read("GaugeOverlay.exe"));
        Assert.Equal("old classes", Read("Profiles", "classes.json"));
        Assert.False(File.Exists(Path.Combine(App, "GaugeOverlay.exe.old")));
    }

    [Fact]
    public void A_swap_that_fails_halfway_puts_the_old_files_back()
    {
        string error;
        using (File.Open(Path.Combine(App, "Profiles", "classes.json"), FileMode.Open, FileAccess.Read, FileShare.None))   // Profiles can't be moved
            error = UpdateInstaller.Swap(App, New)!;

        Assert.NotNull(error);
        Assert.Equal("old exe", Read("GaugeOverlay.exe"));
        Assert.Equal("old classes", Read("Profiles", "classes.json"));
        Assert.False(File.Exists(Path.Combine(App, "GaugeOverlay.exe.old")));
        Assert.False(Directory.Exists(Path.Combine(App, "Profiles.old")));
    }

    [Fact]
    public void Cleanup_removes_what_the_last_update_left()
    {
        Assert.Null(UpdateInstaller.Swap(App, New));
        UpdateInstaller.Cleanup(App, Path.Combine(_root, "update"));
        Assert.False(File.Exists(Path.Combine(App, "GaugeOverlay.exe.old")));
        Assert.False(Directory.Exists(Path.Combine(App, "Profiles.old")));
        Assert.False(Directory.Exists(Path.Combine(_root, "update")));
        Assert.Equal("new exe", Read("GaugeOverlay.exe"));
    }

    [Fact]
    public void A_zip_with_only_the_exe_and_Profiles_is_accepted() =>
        Assert.Null(UpdateInstaller.ZipProblem([("GaugeOverlay.exe", 200_000_000), ("Profiles/classes.json", 9_000), ("Profiles/aden/", 0)]));

    [Theory]
    [InlineData("../evil.exe")]
    [InlineData("other.dll")]
    [InlineData("Profiles/../../x")]
    public void A_zip_with_anything_else_is_refused(string name) =>
        Assert.NotNull(UpdateInstaller.ZipProblem([("GaugeOverlay.exe", 1), (name, 1)]));

    [Fact]
    public void A_zip_without_the_exe_or_too_big_when_unpacked_is_refused()
    {
        Assert.NotNull(UpdateInstaller.ZipProblem([("Profiles/classes.json", 1)]));
        Assert.NotNull(UpdateInstaller.ZipProblem([("GaugeOverlay.exe", UpdateInstaller.MaxUnpacked + 1)]));
        Assert.NotNull(UpdateInstaller.ZipProblem([("GaugeOverlay.exe", 1), .. Enumerable.Range(0, UpdateInstaller.MaxEntries).Select(i => ($"Profiles/{i}.json", 1L))]));
    }

    [Fact]
    public void Cleanup_with_nothing_left_is_fine() => UpdateInstaller.Cleanup(App, Path.Combine(_root, "nothing"));
}
