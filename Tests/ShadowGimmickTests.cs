using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Tests;

/// <summary>The 그림자 레이드 gimmick (세르카, 벨가르딘): the boss bar goes and a fill bar with the damage dealt in it shows
/// ("134,046,661,186"). Fixtures shadow_number (every 2 s from 9:03 of "Lost Ark 2026.11.03 - 21.46.27.02.mp4", 1080p HUD 110;
/// frames 2–4 caught the number popping bigger as it jumps) and shadow_bossbar (the normal bar just before, same area).</summary>
public class ShadowGimmickTests
{
    static readonly long?[] Shown =
    [
        null, 161_252_862, 21_643_927_603, 31_575_410_844, 40_812_997_885, 72_000_567_057, 84_460_927_882, 95_398_267_131,
        98_119_684_193, 101_122_012_611, 103_938_369_895, 134_046_661_186, 166_122_959_334, 199_744_020_906, 210_082_561_219,
        238_620_583_824, 239_674_092_807, 248_039_915_861, 249_292_970_381, 249_755_521_722,
    ];

    [Fact]
    public void The_gimmicks_number_reads_in_every_frame_also_while_it_pops()
    {
        var frames = Fixture.Load("shadow_number").Frames;
        Assert.Equal(Shown.Length, frames.Count);
        for (int i = 0; i < frames.Count; i++)
            Assert.True(BossDigits.ReadNumber(frames[i], 1) == Shown[i], $"frame {i}: {BossDigits.ReadNumber(frames[i], 1)}");
    }

    [Fact]
    public void The_normal_boss_bars_hp_text_is_no_number() =>
        Assert.All(Fixture.Load("shadow_bossbar").Frames, f => Assert.Null(BossDigits.ReadNumber(f, 1)));
}
