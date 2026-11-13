using System.Globalization;

namespace GaugeOverlay.Core.Domain;

/// <summary>DPS and damage as the DPS 미터기 and 전투 기록 write them (user, 2026-11-12). Pure.</summary>
public static class AmountText
{
    const double Eok = 1e8, Jo = 1e12;
    const double JoFrom = 999.5 * Eok;   // what rounds to 1000억 is written in 조

    /// <summary>DPS: always 억, one decimal ("0.5억", "131.5억"), never 만 or 조.</summary>
    public static string Dps(double v) => (v / Eok).ToString("#,0.0", CultureInfo.InvariantCulture) + "억";

    /// <summary>Damage: three digits in 억 ("154억", "54.3억", "5.43억"), in 조 from 1000억 ("0.1조", "0.154조", "1.54조").</summary>
    public static string Damage(double v) => v >= JoFrom ? Three(v / Jo) + "조" : Three(v / Eok) + "억";

    static string Three(double x)
    {
        int decimals = x >= 99.95 ? 0 : x >= 9.995 ? 1 : x >= 0.9995 ? 2 : 3;
        return x.ToString(decimals == 0 ? "#,0" : "#,0." + new string('#', decimals), CultureInfo.InvariantCulture);
    }
}
