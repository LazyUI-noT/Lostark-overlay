namespace GaugeOverlay.Core.Domain;

/// <summary>The 원정대 from the Lost Ark API meets the 숙제 list (lostark-api.plan.md): the API's names are exact.</summary>
public static class ApiRoster
{
    /// <summary>The name a lookup starts from (user, 2026-11-08): the 대표 캐릭터 typed in 설정 → 로아 API, exact and on the 원정대's
    /// server, so a misread screen name can't pick the roster; without one, the character read off the 게임 메뉴.</summary>
    public static string? Root(string? main, string? current) => main?.Trim() is { Length: > 0 } m ? m : current;

    /// <summary>Characters listed from a grid card's small name (<see cref="HomeworkCharacter.FromCard"/>, often misread) that the API
    /// doesn't list, each renamed to the nearest API name no character has yet (<see cref="CharacterName.Nearest"/>); one API name
    /// takes at most one. Confirmed names are never renamed.</summary>
    public static IReadOnlyList<(string From, string To)> Renames(IReadOnlyList<HomeworkCharacter> characters, IReadOnlyList<string> apiNames)
    {
        var free = apiNames.Where(n => characters.All(c => c.Name != n)).ToList();
        var renames = new List<(string, string)>();
        foreach (var c in characters.Where(c => c.FromCard && !apiNames.Contains(c.Name)))
        {
            if (CharacterName.Nearest(c.Name, free) is not { } to) continue;
            renames.Add((c.Name, to));
            free.Remove(to);
        }
        return renames;
    }
}
