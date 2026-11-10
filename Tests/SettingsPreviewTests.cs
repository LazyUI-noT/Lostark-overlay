using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class SettingsPreviewTests
{
    [Theory]
    [InlineData("스킬창", null, new[] { OverlayPart.Skills, OverlayPart.Gauge })]
    [InlineData("버프 줄", null, new[] { OverlayPart.Buffs })]
    [InlineData("단심", null, new[] { OverlayPart.Judgment })]
    [InlineData("암수", null, new[] { OverlayPart.Amsu })]
    [InlineData("DPS 미터기", null, new[] { OverlayPart.Meter })]
    [InlineData("레이드", null, new[] { OverlayPart.BarCount, OverlayPart.Debuffs })]
    [InlineData("파티 공유", "파티원 모양", new[] { OverlayPart.Party })]
    public void PartsOf_LookPages_ReturnTheWindowsTheyChange(string area, string? section, OverlayPart[] expected)
    {
        Assert.Equal(expected, SettingsPreview.PartsOf(area, section));
    }

    [Theory]
    [InlineData("파티 공유", null)]
    [InlineData("파티 공유", "연결")]
    [InlineData("직업 인식", null)]
    [InlineData("테마·업데이트", null)]
    [InlineData("", null)]
    public void PartsOf_PagesWithoutAWindowLook_ReturnNothing(string area, string? section)
    {
        Assert.Empty(SettingsPreview.PartsOf(area, section));
    }

    [Fact]
    public void Beside_WideEnoughForFormGapAndPreview_IsTrue()
    {
        double need = SettingsPreview.FormWidth + SettingsPreview.Gap + SettingsPreview.PreviewWidth;
        Assert.True(SettingsPreview.Beside(need));
        Assert.True(SettingsPreview.Beside(need + 300));
    }

    [Fact]
    public void Beside_NarrowerThanFormGapAndPreview_IsFalse()
    {
        double need = SettingsPreview.FormWidth + SettingsPreview.Gap + SettingsPreview.PreviewWidth;
        Assert.False(SettingsPreview.Beside(need - 1));
        Assert.False(SettingsPreview.Beside(0));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5)]
    public void Beside_InvalidWidth_IsFalse(double width)
    {
        Assert.False(SettingsPreview.Beside(width));
    }
}
