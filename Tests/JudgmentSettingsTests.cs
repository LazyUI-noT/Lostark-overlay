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
    public void A_file_with_the_removed_대기_중_흐리게_option_still_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"judg-{Guid.NewGuid():N}.json");
        try
        {
            var s = new Settings();
            s.Judgment.Display = JudgmentDisplay.ReadyIcon;
            new SettingsStore(path).Save(s);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"Display\"", "\"IconCooldown\": true, \"Display\""));
            Assert.Contains("IconCooldown", File.ReadAllText(path));
            Assert.Equal(JudgmentDisplay.ReadyIcon, new SettingsStore(path).Load(out _).Judgment.Display);
        }
        finally { File.Delete(path); }
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
            (s.Judgment.Side, s.Judgment.HideWhenEmpty) = (true, true);
            (s.Buffs.HideWhenEmpty, s.Debuffs.HideWhenEmpty) = (true, true);
            new SettingsStore(path).Save(s);
            var back = new SettingsStore(path).Load(out _).Judgment;
            Assert.True(back.On("워로드 · 고독한 기사"));
            Assert.Equal((JudgmentSource.Icon, 150, JudgmentDisplay.ReadyIcon, true), (back.Source, back.FontSize, back.Display, back.OneColor));
            Assert.Equal((true, true), (back.Side, back.HideWhenEmpty));
            var all = new SettingsStore(path).Load(out _);
            Assert.Equal((true, true), (all.Buffs.HideWhenEmpty, all.Debuffs.HideWhenEmpty));

            File.WriteAllText(path, """{ "FirstRunDone": true, "Judgment": { "FontSize": 120, "Characters": null } }""");
            var old = new SettingsStore(path).Load(out bool reset).Judgment;
            Assert.False(reset);
            Assert.Equal(100, old.FontSize);   // not one of the game's sizes
            Assert.False(old.On("워로드 · 고독한 기사"));
        }
        finally { File.Delete(path); }
    }
}
