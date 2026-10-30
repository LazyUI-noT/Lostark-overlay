namespace GaugeOverlay.Core.Domain;

public sealed class ClassSettings
{
    /// <summary>개별: the class uses its own <see cref="Mine"/> and <see cref="Members"/>; off, 기본's (kept for when it
    /// is turned on again).</summary>
    public bool Own { get; set; }
    /// <summary>나: your own boxes while you play this class.</summary>
    public MineProfile Mine { get; set; } = new();
    /// <summary>파티원: a party member's boxes while they play this class.</summary>
    public MemberProfile Members { get; set; } = new();
}


public sealed class CharacterSettings
{
    public string ClassFamily { get; set; } = "";   // 직업군
    public string Class { get; set; } = "";         // 클래스
    public string Subclass { get; set; } = "";      // 직업각인
    /// <summary>Emblem crop learned on first pick, BGRA, for auto-detection. Base64.</summary>
    public string? EmblemBgra { get; set; }
}
