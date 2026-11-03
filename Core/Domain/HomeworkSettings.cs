namespace GaugeOverlay.Core.Domain;

/// <summary>설정 → 숙제 (homework.plan.md): the characters known (read from the 다른 캐릭터 접속 grid, or added by hand) with their
/// picks, and the tags the user renamed ("raid|difficulty" → text). The clears themselves are in homework.json (HomeworkStore),
/// not here: an undo must not take one back.</summary>
public sealed class HomeworkSettings
{
    public List<HomeworkCharacter> Characters { get; set; } = [];
    public Dictionary<string, string> Tags { get; set; } = [];
    /// <summary>숙제 표시: the tags under the 다른 캐릭터 접속 grid's cards.</summary>
    public bool ShowOnGrid { get; set; } = true;

    /// <summary>The character called <paramref name="name"/>, or null.</summary>
    public HomeworkCharacter? Find(string? name) => Characters.FirstOrDefault(c => c.Name == name);
}

/// <summary>A character on the 숙제 list: its in-game name, class (from its card), 직업각인 (learned while it is played) and picks.</summary>
public sealed class HomeworkCharacter
{
    public const int MaxPicks = 5;

    public string Name { get; set; } = "";
    public string? ClassName { get; set; }
    public string? Subclass { get; set; }
    /// <summary>Left off the 숙제 page and the grid (it stays known, so its name still snaps reads).</summary>
    public bool Hidden { get; set; }
    public List<HomeworkPick> Picks { get; set; } = [];
}
