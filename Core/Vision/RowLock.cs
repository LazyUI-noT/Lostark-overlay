namespace GaugeOverlay.Core.Vision;

/// <summary>The boss debuff row's line, settled over finds (DebuffReader.FindRowY): a line moves only after the same new one (± 2 px)
/// was found <see cref="Repeats"/> times in a row, so a damage number or an effect over the row for a moment doesn't move it. Pure.</summary>
public sealed record RowLock(int? Y, int? Candidate, int Seen)
{
    public const int Repeats = 3;
    const int Same = 2;
    public static readonly RowLock None = new(null, null, 0);

    public RowLock Next(int? found)
    {
        if (found is not { } y) return this;
        if (Y is { } held && Math.Abs(y - held) <= Same) return this with { Candidate = null, Seen = 0 };
        int seen = Candidate is { } c && Math.Abs(y - c) <= Same ? Seen + 1 : 1;
        return seen >= Repeats ? new(y, null, 0) : this with { Candidate = y, Seen = seen };
    }
}
