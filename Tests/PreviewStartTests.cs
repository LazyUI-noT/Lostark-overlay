using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>위치 조정 opens on the fit in use, or at HUD 100 when there is none yet (live or 테스트 모드), for the user to move.</summary>
public class PreviewStartTests
{
    [Fact]
    public void Preview_starts_from_HUD_100_without_a_fit() => Assert.Equal(new Fit(100), PreviewStart.Fit(null, asked: true));

    [Fact]
    public void Preview_keeps_a_found_fit() => Assert.Equal(new Fit(87, 2), PreviewStart.Fit(new Fit(87, 2), asked: true));

    [Fact]
    public void No_preview_no_fit() => Assert.Null(PreviewStart.Fit(null, asked: false));
}
