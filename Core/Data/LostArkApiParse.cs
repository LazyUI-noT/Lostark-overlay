using System.Globalization;
using System.Text.Json;

namespace GaugeOverlay.Core.Data;

/// <summary>A character of the 원정대 (siblings): name, server, class and item level.</summary>
public sealed record ApiCharacter(string Name, string Server, string ClassName, double ItemLevel);

/// <summary>An ark passive 각인 as the armory lists it; <paramref name="Stone"/> is the ability stone's level, null without.</summary>
public sealed record ApiEngraving(string Name, string Grade, int Level, int? Stone);

/// <summary>A profile stat (치명, 특화 … 공격력) as a number.</summary>
public sealed record ApiStat(string Type, long Value);

/// <summary>One character's armory: <paramref name="CombatPower"/> is the build last saved (null when the server gave none),
/// <paramref name="Subclass"/> the 직업각인 (the ark passive's title, written with spaces: "빛의 기사").</summary>
public sealed record ApiProfile(string Name, string Server, string ClassName, double ItemLevel, double? CombatPower, string? Subclass,
    IReadOnlyList<ApiEngraving> Engravings, IReadOnlyList<ApiStat> Stats);

/// <summary>Reading the API's answers (measured 2026-11-08): numbers are strings with thousands commas ("1,755.00"), an unknown
/// character is a JSON null, and siblings list every server. Nothing is trusted: anything malformed reads as missing.</summary>
public sealed partial class LostArkApi
{
    /// <summary>The 원정대 on <paramref name="server"/>, in the API's order; empty when the answer can't be read.</summary>
    public static IReadOnlyList<ApiCharacter> ParseSiblings(string json, string server)
    {
        using var doc = TryParse(json);
        if (doc?.RootElement is not { ValueKind: JsonValueKind.Array } list) return [];
        return [.. list.EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.Object && Text(c, "ServerName") == server && Text(c, "CharacterName") is { Length: > 0 })
            .Select(c => new ApiCharacter(Text(c, "CharacterName")!, server, Text(c, "CharacterClassName") ?? "", Number(c, "ItemAvgLevel") ?? 0))];
    }

    /// <summary>The armory with its profile; null when there is no profile (unknown character, malformed answer).</summary>
    public static ApiProfile? ParseArmory(string json)
    {
        using var doc = TryParse(json);
        if (doc?.RootElement is not { ValueKind: JsonValueKind.Object } root || !root.TryGetProperty("ArmoryProfile", out var p)
            || p.ValueKind != JsonValueKind.Object || Text(p, "CharacterName") is not { Length: > 0 } name)
            return null;
        string? subclass = root.TryGetProperty("ArkPassive", out var ark) && ark.ValueKind == JsonValueKind.Object && Text(ark, "Title") is { Length: > 0 } t ? t : null;
        return new ApiProfile(name, Text(p, "ServerName") ?? "", Text(p, "CharacterClassName") ?? "", Number(p, "ItemAvgLevel") ?? 0,
            Number(p, "CombatPower"), subclass, Engravings(root), Stats(p));
    }

    static List<ApiEngraving> Engravings(JsonElement root) =>
        root.TryGetProperty("ArmoryEngraving", out var e) && e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty("ArkPassiveEffects", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Text(x, "Name") is { Length: > 0 })
                .Select(x => new ApiEngraving(Text(x, "Name")!, Text(x, "Grade") ?? "", (int)(Number(x, "Level") ?? 0), Number(x, "AbilityStoneLevel") is { } s ? (int)s : null))]
            : [];

    static List<ApiStat> Stats(JsonElement profile) =>
        profile.TryGetProperty("Stats", out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object && Text(x, "Type") is { Length: > 0 } && Number(x, "Value") is not null)
                .Select(x => new ApiStat(Text(x, "Type")!, (long)Number(x, "Value")!.Value))]
            : [];

    static JsonDocument? TryParse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A number given as a JSON number or as a string with thousands commas; null when absent or not a number.</summary>
    static double? Number(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        return v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString()?.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
