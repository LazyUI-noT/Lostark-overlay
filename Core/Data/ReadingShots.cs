using System.Globalization;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Which game screen a saved 읽는 자리 picture is: 캐릭터 선택 (the ESC 게임 메뉴 open) or a raid.</summary>
public enum ReadingShotKind { Select, Raid }

/// <summary>One saved picture: its file name in its size's folder, its kind, and when a raid one was taken.</summary>
public sealed record ReadingShot(string File, ReadingShotKind Kind, DateTime? At);

/// <summary>읽는 자리's saved pictures (reading-place.plan.md B): the game area only, one folder per game-area size
/// (<c>reading-place\2560x1080\</c> beside settings.json; the size carries resolution and ratio, a forced 21:9 is a shorter
/// viewport), so boxes land on a picture by the viewport's corner. One 캐릭터 선택 picture, up to <see cref="RaidKept"/> raid ones.
/// Kept on this PC only: never in logs, reports or party messages (they may show character names).</summary>
public static class ReadingShots
{
    public const int RaidKept = 3;
    public const string SelectFile = "select.png";
    const string RaidPrefix = "raid-", RaidStamp = "yyyyMMdd-HHmmss", Png = ".png";

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GaugeOverlay", "reading-place");

    /// <summary>The folder for pictures of a game area this size.</summary>
    public static string FolderOf(string dir, Viewport vp) => Path.Combine(dir, $"{vp.Width}x{vp.Height}");

    /// <summary>A raid picture's file name: its time, so names sort oldest to newest.</summary>
    public static string RaidName(DateTime at) => RaidPrefix + at.ToString(RaidStamp, CultureInfo.InvariantCulture) + Png;

    /// <summary>The pictures among <paramref name="names"/>: 캐릭터 선택 first, then raids newest first; other files left out.</summary>
    public static IReadOnlyList<ReadingShot> List(IEnumerable<string> names)
    {
        var all = names.ToList();
        var select = all.Contains(SelectFile) ? [new ReadingShot(SelectFile, ReadingShotKind.Select, null)] : Array.Empty<ReadingShot>();
        return [.. select, .. Raids(all)];
    }

    /// <summary>The raid pictures past the newest <see cref="RaidKept"/>, to delete.</summary>
    public static IReadOnlyList<string> Dropped(IEnumerable<string> names) => [.. Raids(names).Skip(RaidKept).Select(s => s.File)];

    static IEnumerable<ReadingShot> Raids(IEnumerable<string> names) =>
        names.Select(n => TakenAt(n) is { } at ? new ReadingShot(n, ReadingShotKind.Raid, at) : null).OfType<ReadingShot>().OrderByDescending(s => s.At);

    static DateTime? TakenAt(string name) =>
        name.StartsWith(RaidPrefix, StringComparison.Ordinal) && name.EndsWith(Png, StringComparison.Ordinal)
        && DateTime.TryParseExact(name[RaidPrefix.Length..^Png.Length], RaidStamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;

    /// <summary>The 읽는 자리 boxes each picture shows: what is on screen at 캐릭터 선택 with the menu open, and in a raid.</summary>
    public static IReadOnlySet<string> BoxesOf(ReadingShotKind kind) => kind == ReadingShotKind.Select ? SelectBoxes : RaidBoxes;

    static readonly HashSet<string> SelectBoxes = ["skill", "buff", DetectPlace.DebuffRow, "aden", "judg", "char"];
    static readonly HashSet<string> RaidBoxes = ["g-jeonbun", "boss", "amsu", "dungeon"];

    /// <summary>The whole game area from the moment 스킬창 was first read, behind every 화면 인식 설정 chip's card on the 표시 page
    /// (user, 2026-11-19): a box not read yet still shows the game around it. Not one of the picker's pictures.</summary>
    public const string BackdropFile = "backdrop.png";

    /// <summary>스킬창 reads ✓ and no backdrop was taken on this game area yet: asked each step until one is taken, so a
    /// step whose game area was momentarily empty doesn't lose it for the run.</summary>
    public static bool TakesBackdrop(bool takenHere, Detection? skill) => !takenHere && skill is { Mark: DetectMark.Concrete };

    /// <summary>The raid readers whose ! saves a raid picture by itself (전분, 암수): a box can then be fixed from a fail.</summary>
    public static bool ShootsOnFail(string id) => id is "g-jeonbun" or "amsu";

    /// <summary>The game area of a screen shot that lies at <paramref name="shotArea"/>; null when the shot doesn't hold all of it.</summary>
    public static Image? GameArea(Image shot, PixelRect shotArea, Viewport vp)
    {
        var game = new PixelRect(vp.X, vp.Y, vp.Width, vp.Height);
        if (game.Width <= 0 || game.Height <= 0 || game.Intersect(shotArea) != game) return null;
        return shot.Crop(new PixelRect(game.X - shotArea.X, game.Y - shotArea.Y, game.Width, game.Height));
    }
}
