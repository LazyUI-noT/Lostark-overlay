namespace GaugeOverlay.Core.Domain;

/// <summary>The party display name's length limit: 7 Korean characters or 14 English ones, as a Korean (or other
/// full-width) character is about twice as wide. Enforced where the name is typed and again where a party member's
/// name is drawn (their client may not enforce it).</summary>
public static class PlayerName
{
    public const int MaxUnits = 14, MinUnits = 2;   // a wide character counts 2

    /// <summary>The longest prefix of <paramref name="name"/> within <paramref name="max"/> units (이름 길이), kept within
    /// <see cref="MinUnits"/>–<see cref="MaxUnits"/>.</summary>
    public static string Clip(string name, int max = MaxUnits)
    {
        int units = 0;
        for (int i = 0; i < name.Length; i++)
        {
            units += Wide(name[i]) ? 2 : 1;
            if (units > Math.Clamp(max, MinUnits, MaxUnits)) return name[..(char.IsLowSurrogate(name[i]) ? i - 1 : i)];   // keep pairs whole
        }
        return name;
    }

    // Hangul, CJK and full-width forms.
    static bool Wide(char c) => c is >= 'ᄀ' and <= 'ᇿ' or >= '⺀' and <= '꓏'
        or >= '가' and <= '힣' or >= '豈' and <= '﫿' or >= '＀' and <= '｠';
}
