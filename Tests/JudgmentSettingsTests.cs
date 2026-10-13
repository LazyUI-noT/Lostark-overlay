using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>단심's settings (user, 2026-10-13): on per 직업각인, off by default; how it is read and shown.</summary>
public class JudgmentSettingsTests
{
    [Fact]
    public void Off_by_default_and_on_per_직업각인()
    {
        var s = new Settings();
        Assert.False(s.Judgment.On("워로드 · 고독한 기사"));
        s.Judgment.Set("워로드 · 고독한 기사", true);
        Assert.True(s.Judgment.On("워로드 · 고독한 기사"));
        Assert.False(s.Judgment.On("워로드 · 전투 태세"));
        Assert.False(s.Judgment.On(null));
        s.Judgment.Set("워로드 · 고독한 기사", false);
        Assert.False(s.Judgment.On("워로드 · 고독한 기사"));
    }

    [Fact]
    public void Its_settings_survive_a_restart_and_an_old_file_loads_with_it_off()
    {
        var path = Path.Combine(Path.GetTempPath(), $"judg-{Guid.NewGuid():N}.json");
        try
        {
            var s = new Settings();
            s.Judgment.Set("워로드 · 고독한 기사", true);
            s.Judgment.Source = JudgmentSource.Icon;
            s.Judgment.FontSize = 150;
            s.Judgment.Display = JudgmentDisplay.ReadyIcon;
            s.Judgment.OneColor = true;
            new SettingsStore(path).Save(s);
            var back = new SettingsStore(path).Load(out _).Judgment;
            Assert.True(back.On("워로드 · 고독한 기사"));
            Assert.Equal((JudgmentSource.Icon, 150, JudgmentDisplay.ReadyIcon, true), (back.Source, back.FontSize, back.Display, back.OneColor));

            File.WriteAllText(path, """{ "FirstRunDone": true, "Judgment": { "FontSize": 120, "Characters": null } }""");
            var old = new SettingsStore(path).Load(out bool reset).Judgment;
            Assert.False(reset);
            Assert.Equal(100, old.FontSize);   // not one of the game's sizes
            Assert.False(old.On("워로드 · 고독한 기사"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_reset_keeps_where_it_is_on_the_game_size_and_the_window()
    {
        var s = new Settings();
        s.Judgment.Set("워로드 · 고독한 기사", true);
        (s.Judgment.FontSize, s.Judgment.Left, s.Judgment.Width) = (200, 50, 1.5);
        (s.Judgment.Source, s.Judgment.Display, s.Judgment.Color, s.Judgment.Opacity) = (JudgmentSource.Text, JudgmentDisplay.ReadyIcon, "#FF0000", 0.5);
        SettingsResets.Judgment(s);
        var j = s.Judgment;
        Assert.Equal((true, 200, 50.0, 1.5), (j.On("워로드 · 고독한 기사"), j.FontSize, j.Left, j.Width));
        Assert.Equal((JudgmentSource.Both, JudgmentDisplay.Bar, new JudgmentSettings().Color, 1.0), (j.Source, j.Display, j.Color, j.Opacity));
    }

    // 레이아웃 편집기 (user, 2026-10-13): its 적용 carries the 단심 window's place and size and its 단심 창 switch.
    [Fact]
    public void The_layout_editor_applies_the_단심_window()
    {
        var live = new Settings();
        var edited = new Settings();
        edited.Judgment.Set("워로드 · 고독한 기사", true);
        (edited.Judgment.Left, edited.Judgment.Top, edited.Judgment.Width) = (300, 400, 1.4);
        live.ApplyLayoutFrom(edited);
        Assert.Equal((true, 300.0, 400.0, 1.4), (live.Judgment.On("워로드 · 고독한 기사"), live.Judgment.Left, live.Judgment.Top, live.Judgment.Width));
        Assert.True(new Settings().LayoutDiffers(edited));
    }
}
