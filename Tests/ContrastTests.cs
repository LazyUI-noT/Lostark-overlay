using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public sealed class ContrastTests
{
    [Theory]
    [InlineData(0x2F, 0x9E, 0x44, true)]    // 기능 맵 켜기 green: white 3.4, black 6.1
    [InlineData(0x86, 0x8E, 0x96, true)]    // 끄기 grey
    [InlineData(0xD6, 0xB3, 0x4C, true)]    // 2단계 yellow
    [InlineData(0x3B, 0x5B, 0xDB, false)]   // 표시 안 함 blue: white wins
    [InlineData(0x49, 0x50, 0x57, false)]   // 읽기 dark
    [InlineData(0x8F, 0x4F, 0xD6, false)]   // 나메 purple
    public void Text_on_a_fill_is_black_or_white_whichever_contrasts_more(byte r, byte g, byte b, bool black)
        => Assert.Equal(black, Contrast.BlackInk(r, g, b));

    [Theory]
    [InlineData(0x4F, 0xD1, 0xC5)]   // timeline compared cyan: 1.6:1 on white
    [InlineData(0xFF, 0x7A, 0x59)]
    [InlineData(0xD8, 0xA8, 0xFF)]   // 빛암
    [InlineData(0x5B, 0xD1, 0x8B)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void A_line_on_a_light_card_reaches_3_to_1_and_keeps_its_hue(byte r, byte g, byte b)
    {
        var (dr, dg, db) = Contrast.OnLight(r, g, b);
        Assert.True(Contrast.Ratio(dr, dg, db, Contrast.LightCard, Contrast.LightCard, Contrast.LightCard) >= 3);
        Assert.True(dr <= r && dg <= g && db <= b);   // only darker
        if (r > 0) Assert.InRange(dg / (double)dr, g / (double)r - 0.1, g / (double)r + 0.1);   // same hue, give or take rounding
    }

    [Fact]
    public void A_line_that_already_reads_on_a_light_card_stays_as_it_is()
        => Assert.Equal(((byte)0x2B, (byte)0x4F, (byte)0x9A), Contrast.OnLight(0x2B, 0x4F, 0x9A));
}
