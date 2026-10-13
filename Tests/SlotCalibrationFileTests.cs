using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>Where a slot's ready look is saved. 아덴 slots are framed per class (워로드's shield, 도화가's hexagon), so one
/// class's picture matched against another's reads Unreadable whenever no digits show: 워로드's lit C scored 0.01–0.04
/// against 도화가's (threshold 0.4), and a 해 3 reset of C stayed an estimated cooldown (user, 2026-10-13). So a 아덴 slot's
/// picture is the class's own; the other slots look alike on every class and keep one file.</summary>
public class SlotCalibrationFileTests
{
    [Fact]
    public void A_normal_slot_has_one_file_whatever_the_class()
    {
        Assert.Equal("일반1.bgra", SlotCalibration.FileName(SlotId.일반1, null, "워로드 · 고독한기사", adjusted: false));
        Assert.Equal("일반1.bgra", SlotCalibration.FileName(SlotId.일반1, null, "도화가 · 회귀", adjusted: false));
    }

    [Fact]
    public void A_아덴_slot_has_a_file_per_class_shared_by_its_직업각인()
    {
        Assert.Equal("아덴2_워로드.bgra", SlotCalibration.FileName(SlotId.아덴2, null, "워로드 · 고독한기사", adjusted: false));
        Assert.Equal("아덴2_워로드.bgra", SlotCalibration.FileName(SlotId.아덴2, null, "워로드 · 전투태세", adjusted: false));
        Assert.Equal("아덴2_도화가.bgra", SlotCalibration.FileName(SlotId.아덴2, null, "도화가 · 회귀", adjusted: false));
    }

    [Fact]
    public void A_moved_아덴_slot_keeps_its_place_in_the_name_and_수동_조정_its_suffix_last()
    {
        Assert.Equal("아덴1_870_960_발키리_adj.bgra", SlotCalibration.FileName(SlotId.아덴1, new PixelRect(870, 960, 40, 40), "발키리 · 해방자", adjusted: true));
        Assert.EndsWith(SlotCalibration.AdjustSuffix + ".bgra", SlotCalibration.FileName(SlotId.일반3, null, null, adjusted: true));
    }

    [Fact]
    public void With_no_character_known_a_아덴_slot_keeps_the_plain_name()
    {
        Assert.Equal("아덴1.bgra", SlotCalibration.FileName(SlotId.아덴1, null, null, adjusted: false));
    }
}
