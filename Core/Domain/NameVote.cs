namespace GaugeOverlay.Core.Domain;

/// <summary>A name read from the game menu counts once the same text is read twice in a row (homework.plan.md): Windows OCR's
/// misreads of a name vary from frame to frame, the name itself doesn't. Pure.</summary>
public sealed record NameVote(string? Last, int Count)
{
    public static readonly NameVote None = new(null, 0);

    /// <summary>The next vote, and the name once it counts (null until then, and for no read).</summary>
    public (NameVote Vote, string? Name) Read(string? name)
    {
        if (name is null) return (None, null);
        var vote = name == Last ? this with { Count = Count + 1 } : new NameVote(name, 1);
        return (vote, vote.Count >= 2 ? name : null);
    }
}
