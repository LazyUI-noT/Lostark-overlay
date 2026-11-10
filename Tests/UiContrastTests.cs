using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class UiContrastTests
{
    [Fact]
    public void Black_on_white_is_the_full_range() =>
        Assert.Equal(21, UiContrast.Ratio("#000000", "#FFFFFF"), 1);

    [Theory]
    [InlineData("#AE3EC9", "#18191D")]   // 레이드 · 보스 on the dark rail: 3.6:1 as it stands
    [InlineData("#D6336C", "#18191D")]
    [InlineData("#E8590C", "#F3F3F3")]   // 전투기록 on a light rail
    public void A_colour_too_faint_for_its_background_is_moved_until_it_reads(string colour, string background) =>
        Assert.True(UiContrast.Ratio(UiContrast.Legible(colour, background), background) >= UiContrast.Readable);

    [Fact]
    public void A_colour_that_already_reads_stays_as_it_is() =>
        Assert.Equal("#2F9E44", UiContrast.Legible("#2F9E44", "#18191D"));

    [Fact]
    public void A_background_with_alpha_is_read_by_its_colour() =>
        Assert.Equal("#2F9E44", UiContrast.Legible("#2F9E44", "#FF18191D"));
}
