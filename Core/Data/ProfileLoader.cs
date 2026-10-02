using System.Text.Json.Nodes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>One class from classes.json. <see cref="Subclasses"/> maps 직업각인 → its 아덴 게이지 (null = not tracked).
/// <see cref="Id"/> is its "id" in the file (1–255, unique), sent to the party in one byte and used for its emblem; 0 = no
/// class. <see cref="Role"/> is its "role" ("서포터"; missing = 딜러), which picks a party member's colour by role.
/// <see cref="AdenRects"/> ("adenRects") moves 아덴1/아덴2 for a class that draws them elsewhere; <see cref="Own"/> is what a
/// 직업각인 sets for itself (<see cref="RoleOf"/>, <see cref="AdenSlotsOf"/>, <see cref="AdenDark"/>). Its dictionaries compare by
/// reference: don't rely on record equality. <see cref="SameSkills"/> ("sameSkills": true): its 직업각인 have the same skills, so
/// neither the 초각스 nor the skills tell them apart (디스트로이어); the one last played is used.</summary>
public sealed record ClassInfo(string Family, string Name, IReadOnlyList<SlotId> AdenSlots, IReadOnlyDictionary<string, GaugeRule?> Subclasses,
    int Id = 0, Role Role = Role.딜러, IReadOnlyDictionary<SlotId, PixelRect>? AdenRects = null,
    IReadOnlyDictionary<string, SubclassInfo?>? Own = null, bool SameSkills = false)
{
    /// <summary>This class's 직업각인 in <paramref name="history"/> (Settings.SubclassHistory), or null: none, or one it doesn't have.</summary>
    public string? Remembered(IReadOnlyDictionary<string, string> history) =>
        history.GetValueOrDefault(Name) is { } h && Subclasses.ContainsKey(h) ? h : null;

    SubclassInfo? Of(string? subclass) => subclass is not null ? Own?.GetValueOrDefault(subclass) : null;
    public Role RoleOf(string? subclass) => Of(subclass)?.Role ?? Role;
    public IReadOnlyList<SlotId> AdenSlotsOf(string? subclass) => Of(subclass)?.AdenSlots ?? AdenSlots;
    public DarkCue AdenDark(string? subclass) => Of(subclass)?.AdenDark ?? DarkCue.None;
}

/// <summary>A 직업각인's own "role" and "adenSlots" (null = the class's), and "adenDark": its 아덴 skills are case 2, read as
/// <see cref="ReadKind.Unavailable"/> without enough resource, by the class's cue (true: the icon dark; "label": the key
/// label grey, 워로드).</summary>
public sealed record SubclassInfo(Role? Role, IReadOnlyList<SlotId>? AdenSlots, DarkCue AdenDark);

/// <summary>When the gauge bar glows: <see cref="Sections"/>, each fully filled segment; <see cref="SlotReady"/>, the
/// filled part while <see cref="GlowRule.Slot"/> is ready and the gauge is at least <see cref="GlowRule.MinGauge"/>.</summary>
public enum GlowWhen { Sections, SlotReady }

public sealed record GlowRule(GlowWhen When, SlotId? Slot = null, double MinGauge = 0);

/// <summary>A 직업각인's 아덴 게이지 defaults ("adenGauge" in classes.json): where the bar is split (0–1, ascending) and
/// when it glows (null = never). <see cref="Ramp"/> (색 단계): the colour level (1 weak, 2 medium, 3 strong) the whole
/// fill takes when 1, 2, … sections are full, the last one kept past the list; empty = no ramp.</summary>
public sealed record GaugeRule(IReadOnlyList<double> Splits, GlowRule? Glow, IReadOnlyList<int>? Ramp = null,
    IReadOnlyList<GaugeSpend>? Spend = null, GaugeColors? Colors = null)
{
    public IReadOnlyList<int> Ramp { get; } = Ramp ?? [];
    public IReadOnlyList<GaugeSpend> Spend { get; } = Spend ?? [];

    /// <summary>What a use of <paramref name="slot"/> leaves of <paramref name="value"/>: the full sections, lowest first
    /// and at most the <see cref="GaugeSpend.Max"/> of the <see cref="Spend"/> entry holding the slot, are taken and the
    /// rest of the fill stays; or, for an entry with an <see cref="GaugeSpend.Amount"/>, that share is taken. Null when
    /// nothing would be taken (no full section, less than the amount, or the slot spends nothing).</summary>
    public double? AfterSpend(SlotId slot, double value)
    {
        if (Spend.FirstOrDefault(s => s.Slots.Contains(slot)) is not { } spend) return null;
        if (spend.Amount is { } a) return value >= a - 1e-9 ? Math.Max(0, value - a) : null;
        double[] ends = [.. Splits, 1];
        int spent = Math.Min(ends.Count(e => value >= e - 1e-9), spend.Max);
        return spent == 0 ? null : Math.Max(0, value - ends[spent - 1]);
    }

    /// <summary>Whether a use of <paramref name="slot"/> always takes its spend (도화가), so the gauge drops at the press
    /// instead of waiting for reads to confirm it.</summary>
    public bool SpendsAtOnce(SlotId slot) => Spend.FirstOrDefault(s => s.Slots.Contains(slot)) is { Immediate: true };

    /// <summary>Seconds after a use of <paramref name="slot"/> in which the next use is free (바드), or null.</summary>
    public double? FreeRecast(SlotId slot) => Spend.FirstOrDefault(s => s.Slots.Contains(slot)) is { Immediate: true } spend ? spend.FreeRecast : null;

    /// <summary>How near a read must be to what a use of <paramref name="slot"/> leaves to confirm the use: half the
    /// narrowest section, so a read a section off is never taken; 0.1 for a share spend.</summary>
    public double SpendMargin(SlotId slot)
    {
        if (Spend.FirstOrDefault(s => s.Slots.Contains(slot)) is { Amount: not null }) return 0.1;
        double[] ends = [0, .. Splits, 1];
        return ends.Zip(ends.Skip(1), (a, b) => b - a).Min() / 2;
    }
}

/// <summary>"spend" in "adenGauge": using one of <see cref="Slots"/> takes the full sections, at most <see cref="Max"/>
/// (바드 절실한구원's Z and X: 3 of its 4 crystals), or a fixed <see cref="Amount"/> of the gauge (발키리 0.8).</summary>
public sealed record GaugeSpend(IReadOnlyList<SlotId> Slots, int Max, double? Amount = null, bool Immediate = false, double? FreeRecast = null);

/// <summary>"colors" in "adenGauge": the class's own default bar colours, <see cref="Full"/> for full sections and
/// <see cref="Filling"/> for the one still filling (바드: blue and gold, as in the game).</summary>
public sealed record GaugeColors(string Full, string Filling);

/// <summary>Reads Profiles/classes.json into typed records.</summary>
public static class ProfileLoader
{
    /// <summary>Every class, in file order. Throws <see cref="InvalidDataException"/> for a missing, out-of-range or
    /// repeated id, or a bad "adenGauge".</summary>
    public static IReadOnlyList<ClassInfo> LoadClasses(string profilesDir)
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(profilesDir, "classes.json")))!.AsObject();
        List<ClassInfo> classes = [.. from family in root
                   where !family.Key.StartsWith('_')
                   from cls in family.Value!.AsObject()
                   select new ClassInfo(
                       family.Key,
                       cls.Key,
                       [.. cls.Value!["adenSlots"]!.AsArray().Select(s => ParseSlot(s!.GetValue<string>()))],
                       cls.Value["subclasses"]!.AsObject()
                           .ToDictionary(sc => sc.Key, sc => ParseGauge($"{cls.Key} {sc.Key}", sc.Value?["adenGauge"])),
                       ClassId(cls.Key, cls.Value["id"]),
                       ParseRole(cls.Key, cls.Value["role"]) ?? Role.딜러,
                       ParseRects(cls.Key, cls.Value["adenRects"]),
                       cls.Value["subclasses"]!.AsObject().ToDictionary(sc => sc.Key, sc => ParseOwn($"{cls.Key} {sc.Key}", sc.Value)),
                       cls.Value["sameSkills"] is JsonValue same && same.TryGetValue<bool>(out var s) && s)];
        if (classes.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new InvalidDataException($"classes.json: id {twice.Key} is used by {string.Join(", ", twice.Select(c => c.Name))}.");
        return classes;
    }

    /// <summary>null or false = none; true = a plain bar; an object = { "splits": [0–1…], "glow": { "when": "sections" |
    /// "slotReady", "slot": slot id, "minGauge": 0–1 } }.</summary>
    private static GaugeRule? ParseGauge(string name, JsonNode? node)
    {
        InvalidDataException Bad(string what) => new($"classes.json: {name} has a bad \"adenGauge\" ({what}).");
        if (node is null) return null;
        if (node is JsonValue v) return v.TryGetValue<bool>(out var on) ? on ? new GaugeRule([], null) : null : throw Bad("true, false, null or an object");
        if (node is not JsonObject o) throw Bad("true, false, null or an object");

        double Share(JsonNode? n, string what) =>
            n is JsonValue x && x.TryGetValue<double>(out var d) && d is > 0 and < 1 ? d : throw Bad($"{what} must be between 0 and 1");
        List<double> splits = o["splits"] switch
        {
            null => [],
            JsonArray a => [.. a.Select(n => Share(n, "a split")).Distinct().Order()],
            _ => throw Bad("splits must be a list"),
        };
        List<int> ramp = o["ramp"] switch
        {
            null => [],
            JsonArray a => [.. a.Select(n => n is JsonValue x && x.TryGetValue<int>(out var l) && l is >= 1 and <= 3 ? l : throw Bad("a ramp level must be 1, 2 or 3"))],
            _ => throw Bad("ramp must be a list"),
        };
        GaugeSpend Spend(JsonNode? n) => n is JsonObject sp
            ? new GaugeSpend(
                sp["slots"] is JsonArray ids && ids.Count > 0
                    ? [.. ids.Select(n => n is JsonValue x && x.TryGetValue<string>(out var id) && Enum.GetNames<SlotId>().Contains(id)
                        ? Enum.Parse<SlotId>(id) : throw Bad("spend.slots must be slot ids"))]
                    : throw Bad("spend needs slots"),
                sp["amount"] is not null ? 0 : sp["max"] is JsonValue m && m.TryGetValue<int>(out var max) && max >= 1 ? max : throw Bad("spend.max must be 1 or more"),
                sp["amount"] is null ? null
                    : sp["amount"] is JsonValue am && am.TryGetValue<double>(out var amount) && amount is > 0 and <= 1 ? amount
                    : throw Bad("spend.amount must be a share of the gauge, over 0 and at most 1"),
                sp["immediate"] is null ? false
                    : sp["immediate"] is JsonValue im && im.TryGetValue<bool>(out var atOnce) ? atOnce
                    : throw Bad("spend.immediate must be true or false"),
                sp["freeRecast"] is null ? null
                    : sp["freeRecast"] is JsonValue fr && fr.TryGetValue<double>(out var recast) && recast > 0 && sp["immediate"] is JsonValue { } imm && imm.TryGetValue<bool>(out var on) && on ? recast
                    : throw Bad("spend.freeRecast must be seconds over 0, on a spend that is immediate"))
            : throw Bad("spend must be an object or a list of them");
        // One object, or a list of them for skills that spend different amounts (도화가: Z up to 2 orbs, C 1).
        List<GaugeSpend> spend = o["spend"] switch
        {
            null => [],
            JsonArray a => [.. a.Select(Spend)],
            var sp => [Spend(sp)],
        };
        string Hex(JsonNode? n, string what) =>
            n is JsonValue x && x.TryGetValue<string>(out var h) && h is ['#', _, _, _, _, _, _] && h.Skip(1).All(char.IsAsciiHexDigit)
                ? h : throw Bad($"colors.{what} must be \"#RRGGBB\"");
        GaugeColors? colors = o["colors"] switch
        {
            null => null,
            JsonObject c => new GaugeColors(Hex(c["full"], "full"), Hex(c["filling"], "filling")),
            _ => throw Bad("colors must be an object"),
        };
        if (o["glow"] is null) return new GaugeRule(splits, null, ramp, spend, colors);
        if (o["glow"] is not JsonObject glow) throw Bad("glow must be an object");
        var rule = (glow["when"] as JsonValue)?.TryGetValue<string>(out var when) == true ? when : null;
        return new GaugeRule(splits, rule switch
        {
            "sections" => new GlowRule(GlowWhen.Sections),
            "slotReady" => new GlowRule(GlowWhen.SlotReady,
                (glow["slot"] as JsonValue)?.TryGetValue<string>(out var s) == true && Enum.GetNames<SlotId>().Contains(s)
                    ? Enum.Parse<SlotId>(s) : throw Bad("slotReady needs a slot id"),
                glow["minGauge"] switch   // 1 = a full gauge (빛의 기사's C)
                {
                    null => 0,
                    JsonValue m when m.TryGetValue<double>(out var d) && d is > 0 and <= 1 => d,
                    _ => throw Bad("minGauge must be above 0 and at most 1"),
                }),
            _ => throw Bad("glow.when must be sections or slotReady"),
        }, ramp, spend, colors);
    }

    private static int ClassId(string name, JsonNode? id) =>
        id is JsonValue v && v.TryGetValue<int>(out var n) && n is >= 1 and <= byte.MaxValue ? n
            : throw new InvalidDataException($"classes.json: {name} needs an \"id\" from 1 to 255 (sent to the party in one byte).");

    private static Role? ParseRole(string name, JsonNode? role) =>
        role is null ? null
        : role is JsonValue v && v.TryGetValue<string>(out var s) && Enum.TryParse<Role>(s, out var r) && Enum.IsDefined(r) ? r
        : throw new InvalidDataException($"classes.json: {name} has an unknown \"role\" (딜러 or 서포터).");

    /// <summary>"adenRects": { "아덴1": [x, y, w, h], … } in reference pixels (Geometry), 아덴 slots only.</summary>
    private static Dictionary<SlotId, PixelRect>? ParseRects(string name, JsonNode? node)
    {
        if (node is null) return null;
        InvalidDataException Bad() => new($"classes.json: {name} has a bad \"adenRects\" (\"아덴1\"/\"아덴2\": [x, y, w, h], w and h above 0).");
        if (node is not JsonObject o) throw Bad();
        var rects = new Dictionary<SlotId, PixelRect>();
        foreach (var (key, value) in o)
        {
            if (key is not ("아덴1" or "아덴2") || value is not JsonArray a || a.Count != 4
                || a.Select(n => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : (int?)null).ToArray() is not [int x, int y, int w, int h]
                || w <= 0 || h <= 0) throw Bad();
            rects[Enum.Parse<SlotId>(key)] = new(x, y, w, h);
        }
        return rects;
    }

    /// <summary>A 직업각인's own "role", "adenSlots" and "adenDark" (a null entry = none).</summary>
    private static SubclassInfo? ParseOwn(string name, JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var dark = o["adenDark"] switch
        {
            null => DarkCue.None,
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? DarkCue.Icon : DarkCue.None,
            JsonValue v when v.TryGetValue<string>(out var s) && s == "label" => DarkCue.Label,
            _ => throw new InvalidDataException($"classes.json: {name} has a bad \"adenDark\" (true, false or \"label\")."),
        };
        IReadOnlyList<SlotId>? slots = o["adenSlots"] is JsonArray a ? [.. a.Select(s => ParseSlot(s!.GetValue<string>()))] : null;
        return new SubclassInfo(ParseRole(name, o["role"]), slots, dark);
    }

    private static SlotId ParseSlot(string name) =>
        Enum.GetNames<SlotId>().Contains(name)
            ? Enum.Parse<SlotId>(name)
            : throw new InvalidDataException($"Unknown slot reference \"{name}\".");
}
