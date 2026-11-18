using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>reading-place.plan.md B: 읽는 자리's saved pictures.</summary>
public class ReadingShotsTests
{
    [Fact]
    public void Pictures_sit_in_a_folder_per_game_area_size()
    {
        Assert.Equal(Path.Combine("d", "2560x1080"), ReadingShots.FolderOf("d", new Viewport(0, 180, 2560, 1080)));
        Assert.Equal(Path.Combine("d", "1920x1080"), ReadingShots.FolderOf("d", new Viewport(0, 0, 1920, 1080)));
    }

    [Fact]
    public void The_list_is_the_select_picture_then_raids_newest_first()
    {
        var older = ReadingShots.RaidName(new DateTime(2026, 11, 17, 9, 5, 1));
        var newer = ReadingShots.RaidName(new DateTime(2026, 11, 17, 21, 0, 0));
        var list = ReadingShots.List([older, "notes.txt", newer, ReadingShots.SelectFile, "raid-x.png"]);

        Assert.Equal([ReadingShots.SelectFile, newer, older], list.Select(s => s.File));
        Assert.Equal(ReadingShotKind.Select, list[0].Kind);
        Assert.Null(list[0].At);
        Assert.Equal(ReadingShotKind.Raid, list[1].Kind);
        Assert.Equal(new DateTime(2026, 11, 17, 21, 0, 0), list[1].At);
    }

    [Fact]
    public void A_fourth_raid_picture_drops_the_oldest()
    {
        var names = Enumerable.Range(1, 4).Select(m => ReadingShots.RaidName(new DateTime(2026, 11, 17, 10, m, 0))).ToList();

        Assert.Equal([names[0]], ReadingShots.Dropped([.. names, ReadingShots.SelectFile]));
        Assert.Empty(ReadingShots.Dropped(names.Skip(1)));
    }

    [Fact]
    public void Each_picture_shows_its_own_boxes()
    {
        Assert.Contains("skill", ReadingShots.BoxesOf(ReadingShotKind.Select));
        Assert.Contains(DetectPlace.DebuffRow, ReadingShots.BoxesOf(ReadingShotKind.Select));
        Assert.DoesNotContain("g-jeonbun", ReadingShots.BoxesOf(ReadingShotKind.Select));
        Assert.Contains("g-jeonbun", ReadingShots.BoxesOf(ReadingShotKind.Raid));
        Assert.Contains("amsu", ReadingShots.BoxesOf(ReadingShotKind.Raid));
        Assert.DoesNotContain("skill", ReadingShots.BoxesOf(ReadingShotKind.Raid));
    }

    [Fact]
    public void Only_the_raid_readers_take_a_picture_when_they_fail()
    {
        Assert.True(ReadingShots.ShootsOnFail("g-jeonbun"));
        Assert.True(ReadingShots.ShootsOnFail("amsu"));
        Assert.False(ReadingShots.ShootsOnFail("skill"));
        Assert.False(ReadingShots.ShootsOnFail("boss"));
    }

    [Fact]
    public void A_shot_is_cut_to_the_game_area()
    {
        var shot = new Image(10, 8);
        shot.Bgra[(3 * shot.Width + 4) * 4] = 77;   // the game area's top-left pixel (4, 3) on screen at (104, 53)

        var game = ReadingShots.GameArea(shot, new PixelRect(100, 50, 10, 8), new Viewport(104, 53, 5, 4));

        Assert.NotNull(game);
        Assert.Equal((5, 4), (game.Width, game.Height));
        Assert.Equal(77, game.Bgra[0]);
    }

    [Fact]
    public void A_shot_missing_part_of_the_game_area_is_refused()
    {
        var shot = new Image(10, 8);
        Assert.Null(ReadingShots.GameArea(shot, new PixelRect(100, 50, 10, 8), new Viewport(104, 53, 8, 4)));   // past the right edge
        Assert.Null(ReadingShots.GameArea(shot, new PixelRect(100, 50, 10, 8), new Viewport(0, 0, 0, 0)));     // no game
    }
}
