using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>읽는 자리's saved pictures on disk (<see cref="ReadingShots"/>): PNG files in one folder per game-area size. Saves
/// through a temp file; a fourth raid picture drops the oldest. Errors come back as text for the page, never with the path
/// in a log (the pictures may show character names).</summary>
public sealed class ReadingShotStore(string dir)
{
    /// <summary>A picture was saved or deleted (any thread).</summary>
    public event Action? Changed;

    public IReadOnlyList<ReadingShot> List(Viewport vp)
    {
        var folder = ReadingShots.FolderOf(dir, vp);
        return Directory.Exists(folder) ? ReadingShots.List(Names(folder)) : [];
    }

    public string PathOf(Viewport vp, ReadingShot shot) => Path.Combine(ReadingShots.FolderOf(dir, vp), shot.File);

    /// <summary>캐릭터 선택's picture (replaces the last). Null: saved; else why not.</summary>
    public string? SaveSelect(Viewport vp, Image game)
    {
        var failed = Save(vp, ReadingShots.SelectFile, game);
        Changed?.Invoke();
        return failed;
    }

    /// <summary>A raid picture taken at <paramref name="at"/>; the oldest past <see cref="ReadingShots.RaidKept"/> goes.</summary>
    public string? SaveRaid(Viewport vp, Image game, DateTime at)
    {
        var failed = Save(vp, ReadingShots.RaidName(at), game);
        if (failed is null)
        {
            var folder = ReadingShots.FolderOf(dir, vp);
            try { foreach (var old in ReadingShots.Dropped(Names(folder))) File.Delete(Path.Combine(folder, old)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = "오래된 레이드 화면을 지우지 못했습니다."; }
        }
        Changed?.Invoke();   // once, after the oldest went: the picker never lists a fourth
        return failed;
    }

    public string? Delete(Viewport vp, ReadingShot shot)
    {
        try { File.Delete(PathOf(vp, shot)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "화면을 지우지 못했습니다. 이 그림을 연 프로그램을 닫고 다시 하세요."; }
        Changed?.Invoke();
        return null;
    }

    static IEnumerable<string> Names(string folder) => Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>();

    string? Save(Viewport vp, string name, Image game)
    {
        var folder = ReadingShots.FolderOf(dir, vp);
        var path = Path.Combine(folder, name);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";   // its own: a raid picture saved by itself may race a 레이드 화면 찍기
        try
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(game.Width, game.Height, 96, 96, PixelFormats.Bgra32, null, game.Bgra, game.Stride)));
            try
            {
                using (var stream = File.Create(temp)) encoder.Save(stream);
                File.Move(temp, path, overwrite: true);
            }
            finally { File.Delete(temp); }   // gone after the move; a half-written one after a failure
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException || IsImageError(ex))
        {
            return "화면을 저장하지 못했습니다. 디스크 공간과 폴더 권한을 확인하세요.";
        }
        return null;
    }

    /// <summary>What WIC throws for a picture it can't encode or decode (a corrupt or cut-off PNG).</summary>
    static bool IsImageError(Exception ex) =>
        ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or COMException or OverflowException;

    /// <summary>A saved picture for the page, and the same pixels as the reads see them (the green on a placed box); null when it
    /// can't be read or isn't this game area's size.</summary>
    public static (BitmapSource Picture, Image Pixels)? Load(string path, Viewport vp)
    {
        try
        {
            using var file = File.OpenRead(path);   // a stream, not a Uri: a '#' in the profile path would cut a Uri
            var frame = BitmapDecoder.Create(file, BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad).Frames[0];
            if (frame.PixelWidth != vp.Width || frame.PixelHeight != vp.Height) return null;
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var pixels = new Image(bgra.PixelWidth, bgra.PixelHeight);
            bgra.CopyPixels(pixels.Bgra, pixels.Stride, 0);
            bgra.Freeze();
            return (bgra, pixels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException || IsImageError(ex))
        {
            return null;
        }
    }
}
