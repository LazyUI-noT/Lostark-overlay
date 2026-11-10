namespace GaugeOverlay.Core.Domain;

/// <summary>What 스킬 등록 on a fight's timeline does now: register the icons, or explain what to do first.</summary>
public enum SkillRegister { Go, PlayThatCharacter, StartGame, SetClassFirst }

/// <summary>스킬 등록 registers the character being played, so it may only go ahead when that is the fight's character
/// (records-timeline.design.md: no silent capture of another character's bar). Pure.</summary>
public static class SkillRegisterAdvice
{
    /// <summary><paramref name="recordClass"/> and <paramref name="playing"/> are class labels (<see cref="RecordCharacter.Label"/>):
    /// icons are kept per class and 직업각인, not per in-game character.
    /// A fight without a real class (a [Test] fight, or one saved before classes) asks for one in 편집 first: "play that character"
    /// can't be followed when the fight names none (user, 2026-11-08).</summary>
    public static SkillRegister For(string recordClass, string? playing, bool gameRunning)
    {
        if (recordClass is RecordCharacter.TestClass or RecordCharacter.NoClass) return SkillRegister.SetClassFirst;
        if (!gameRunning) return SkillRegister.StartGame;
        if (playing != recordClass) return SkillRegister.PlayThatCharacter;
        return SkillRegister.Go;
    }
}
