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
    /// <summary>레벨 따라 자동 (one switch for all characters): a character's picks are redone when its item level changes.</summary>
    public bool FollowLevel { get; set; } = true;
    /// <summary>The 다른 캐릭터 접속 grid's card order, names by place ("" not learned yet; <see cref="CardOrder"/>). A new list on
    /// every change: the save thread reads it.</summary>
    public List<string> Order { get; set; } = [];
    /// <summary>게임 순서 따르기: the 숙제 tab follows <see cref="Order"/>, a learned order putting it back in the game's order; off,
    /// the tab keeps the order the user dragged (card-order.plan.md).</summary>
    public bool FollowGameOrder { get; set; } = true;
    /// <summary>Raids the user added (a new list on every change: the save thread reads it).</summary>
    public List<HomeworkCustomRaid> CustomRaids { get; set; } = [];

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
    /// <summary>Its item level, whole part (read from its card or the portrait, or typed); null: not known yet.</summary>
    public int? ItemLevel { get; set; }
    /// <summary>Left off the 숙제 page and the grid (it stays known, so its name still snaps reads).</summary>
    public bool Hidden { get; set; }
    /// <summary>Its name came from its grid card's small text, which can misread the same way every time (밝기리 for 밟키리): the
    /// first big name near it renames it (<see cref="Homework.ConfirmedBy"/>), and so does the user. False in older files.</summary>
    public bool FromCard { get; set; }
    public List<HomeworkPick> Picks { get; set; } = [];
}
