using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>스킬 등록 from the 전투 기록 timeline (records-timeline.design.md): register only when the character being played is the fight's.</summary>
public class SkillRegisterAdviceTests
{
    const string Warlord = "워로드 · 고독한기사";
    const string Bard = "바드 · 진실된용맹";

    [Fact]
    public void Registers_when_the_fights_character_is_being_played()
        => Assert.Equal(SkillRegister.Go, SkillRegisterAdvice.For(Warlord, Warlord, gameRunning: true));

    [Fact]
    public void Asks_to_play_that_character_when_another_one_is_playing()
        => Assert.Equal(SkillRegister.PlayThatCharacter, SkillRegisterAdvice.For(Bard, Warlord, gameRunning: true));

    [Fact]
    public void Asks_to_start_the_game_when_it_is_not_running()
        => Assert.Equal(SkillRegister.StartGame, SkillRegisterAdvice.For(Warlord, Warlord, gameRunning: false));

    [Fact]
    public void An_unknown_fight_character_is_never_registered_as_the_one_being_played()
        => Assert.Equal(SkillRegister.PlayThatCharacter, SkillRegisterAdvice.For(RecordCharacter.NoClass, Warlord, gameRunning: true));
}
