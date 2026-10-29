using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class HexColorTests
{
    static Rgb P(string hex) => HexColor.TryParse(hex, out var c) ? c : throw new ArgumentException(hex);

    [Theory]
    [InlineData("#c9a472")]
    [InlineData("#C9A472")]
    [InlineData("  #c9a472 ")]
    [InlineData("#FFC9A472")]   // alpha ignored
    public void Reads_six_and_eight_digit_codes(string text) => Assert.Equal(new Rgb(201, 164, 114), P(text));

    [Fact]
    public void Reads_three_digit_codes() => Assert.Equal(new Rgb(0xAA, 0xBB, 0xCC), P("#abc"));

    [Theory]
    [InlineData("c9a472")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    [InlineData(null)]
    public void Refuses_what_is_not_a_colour_code(string? text) => Assert.False(HexColor.TryParse(text, out _));

    [Fact]
    public void Formats_as_upper_case_six_digits() => Assert.Equal("#C9A472", HexColor.Format(new Rgb(201, 164, 114)));

    [Fact]
    public void Contrast_follows_WCAG()
    {
        Assert.InRange(HexColor.Contrast(P("#F1EEE8"), P("#2A2C35")), 11.9, 12.1);
        Assert.True(HexColor.Contrast(P("#7D8B99"), P("#2A3A4C")) < 4.5);   // 딥 틸's old grey text: fails
        Assert.Equal(21, HexColor.Contrast(P("#FFFFFF"), P("#000000")), 2);
        Assert.Equal(1, HexColor.Contrast(P("#808080"), P("#808080")), 6);
        Assert.Equal(HexColor.Contrast(P("#000000"), P("#FFFFFF")), HexColor.Contrast(P("#FFFFFF"), P("#000000")));
    }
}
