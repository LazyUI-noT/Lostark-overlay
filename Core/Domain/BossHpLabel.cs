namespace GaugeOverlay.Core.Domain;

/// <summary>Amounts as the eye reads them (user, 2026-11-05): "[1조 5,392억]", "[4,696억] +(1,200억)" with a shield, "[1,534만]".</summary>
public static class EasyRead
{
    public static string Of(long amount, long shield = 0) =>
        shield > 0 ? $"[{BossHpGraph.AmountText(amount)}] +({BossHpGraph.AmountText(shield)})" : $"[{BossHpGraph.AmountText(amount)}]";
}

/// <summary>The boss's HP (and shield) from the bar's text, shown beside its name at <paramref name="Label"/> (Geometry.BossHpLabel,
/// screen pixels). Local only.</summary>
public sealed record BossHpView(long Current, long Shield, PixelRect Label)
{
    public string Text => EasyRead.Of(Current, Shield);
}

/// <summary>The boss HP label over the bar's reads (2/s with the meter, every 4 s with the 줄 수 창 alone): a read shows it, a missed
/// one keeps it, none for <see cref="Hold"/> hides it (the bar gone: a cutscene, the fight over, the reads off). Stepped with null
/// on every engine step too, so it expires by time when reads stop. Pure.</summary>
public sealed record BossHpLabel(BossHpView? View, TimeSpan SeenAt)
{
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(4.5);
    public static readonly BossHpLabel None = new(null, TimeSpan.Zero);

    public BossHpLabel Step(BossHpView? read, TimeSpan t) =>
        read is not null ? new(read, t) : View is not null && t - SeenAt > Hold ? None : this;
}
