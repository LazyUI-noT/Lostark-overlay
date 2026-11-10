namespace GaugeOverlay.Core.Data;

/// <summary>The game menu's title word as grey pixels (MenuMemory's title), one small file per screen size beside settings.json:
/// "menu-title-1920x1080.bin", one byte per pixel. Known from the first frame, the menu's open / closed is a pixel compare and
/// needs no OCR (perf-hotspots.plan.md step 2; user, 2026-11-08: save the title rather than read the ESC key). A file of
/// another length (another layout) or one that can't be read counts as none: the OCR learns the title again and saves it.</summary>
public sealed class MenuTitleStore(string dir)
{
    string PathOf(int width, int height) => Path.Combine(dir, $"menu-title-{width}x{height}.bin");

    /// <summary>The saved title for this screen size, if it holds exactly <paramref name="length"/> pixels.</summary>
    public byte[]? Load(int width, int height, int length)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathOf(width, height));
            return bytes.Length == length ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Saves the title unless the file already holds it (each relearn OCR calls this); throws on an IO error (the
    /// caller logs it: the title is only kept in memory then).</summary>
    public void Save(int width, int height, byte[] title)
    {
        var path = PathOf(width, height);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(title)) return;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, title);
    }
}
