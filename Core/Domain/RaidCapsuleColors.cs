using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>The user's difficulty capsule colours (RaidBadge; 숙제's palette). Only the changed ones are stored, so a file without them
/// opens on the defaults. Local display only: never part of a party message. 나이트메어 etc. are keyed by <see cref="DifficultyTier"/>.</summary>
public sealed class RaidCapsuleColors
{
    /// <summary>The tiers the user can recolour, in the order the palette lists them (the grey unknown one stays).</summary>
    public static readonly IReadOnlyList<DifficultyTier> Editable = [DifficultyTier.Normal, DifficultyTier.Hard, DifficultyTier.Nightmare, DifficultyTier.Single, DifficultyTier.Matching,
         DifficultyTier.Step1, DifficultyTier.Step2, DifficultyTier.Step3];

    [JsonConverter(typeof(TierColorsJson))]
    public Dictionary<DifficultyTier, string> Colors { get; set; } = [];

    /// <summary>The shipped colour: brightened to read on 설정's dark background (user, 2026-11-09).</summary>
    public static string DefaultOf(DifficultyTier tier) => tier switch
    {
        DifficultyTier.Normal => "#5E8FD6",
        DifficultyTier.Single => "#4CB08A",
        DifficultyTier.Hard => "#F07C2C",
        DifficultyTier.Nightmare => "#8F4FD6",
        DifficultyTier.Matching => "#3FB6C4",
        DifficultyTier.Step1 => "#C85FA8",
        DifficultyTier.Step2 => "#D6B34C",
        DifficultyTier.Step3 => "#D6594C",
        _ => "#8C8C8C",
    };

    /// <summary>The colour as "#RRGGBB": the stored one, or the default when none is stored or it doesn't parse (a hand edit).</summary>
    public string HexOf(DifficultyTier tier) =>
        Colors is not null && Colors.TryGetValue(tier, out var hex) && HexColor.TryParse(hex, out var c) ? HexColor.Format(c) : DefaultOf(tier);

    /// <summary>Stores <paramref name="hex"/> for <paramref name="tier"/>; the default (or a bad code) removes the entry.</summary>
    public void Set(DifficultyTier tier, string hex)
    {
        Colors ??= [];
        if (!HexColor.TryParse(hex, out var c) || HexColor.Format(c) == DefaultOf(tier)) Colors.Remove(tier);
        else Colors[tier] = HexColor.Format(c);
    }

    public void Reset(DifficultyTier tier) => Colors?.Remove(tier);

    public bool IsDefault(DifficultyTier tier) => HexOf(tier) == DefaultOf(tier);
}

/// <summary>Reads the colour map tolerantly: a key that is no <see cref="DifficultyTier"/> (a hand edit, a newer version) or a
/// value that is no string is skipped, so one bad entry never makes the whole settings file unreadable.</summary>
internal sealed class TierColorsJson : JsonConverter<Dictionary<DifficultyTier, string>>
{
    public override Dictionary<DifficultyTier, string> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var result = new Dictionary<DifficultyTier, string>();
        if (reader.TokenType != JsonTokenType.StartObject) { reader.Skip(); return result; }
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string key = reader.GetString() ?? "";
            reader.Read();
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse<DifficultyTier>(key, out var tier) && Enum.IsDefined(tier))
                result[tier] = reader.GetString() ?? "";
            else reader.Skip();
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<DifficultyTier, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (tier, hex) in value) writer.WriteString(tier.ToString(), hex);
        writer.WriteEndObject();
    }
}
