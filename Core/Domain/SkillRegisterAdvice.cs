namespace GaugeOverlay.Core.Domain;

/// <summary>What 스킬 등록 on a fight's timeline does now: register the icons, or explain what to do first.</summary>
public enum SkillRegister { Go, PlayThatCharacter, StartGame }

/// <summary>스킬 등록 registers the character being played, so it may only go ahead when that is the fight's character
/// (records-timeline.design.md: no silent capture of another character's bar). Pure.</summary>
public static class SkillRegisterAdvice
{
    public static SkillRegister For(string recordCharacter, string? playing, bool gameRunning)
    {
        if (!gameRunning) return SkillRegister.StartGame;
        if (recordCharacter == RecordCharacter.Unknown || playing != recordCharacter) return SkillRegister.PlayThatCharacter;
        return SkillRegister.Go;
    }
}
