using System.Text.Json;
using System.Text.Json.Nodes;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>One effect of a 코어 빌드 ("effects" in Profiles/cores/&lt;클래스&gt;.json): it applies when the chosen levels equal
/// <see cref="Requires"/> (only the cores it names count). <see cref="Kind"/> <see cref="CoreEffects.Reset"/> or
/// <see cref="CoreEffects.Reduce"/> with a <see cref="Trigger"/> skill (its name, shown only) and a <see cref="Target"/> slot
/// changes behaviour (<see cref="Tracks"/>): the target is read on every frame for <see cref="WatchSeconds"/> after the trigger's
/// slot is cast. <see cref="Self"/> ("target": "self"): the target is the trigger's own slot, which its cast may reset. Any other kind is <see cref="Text"/> for the support table. <see cref="Tag"/> and <see cref="Source"/> say how
/// sure the fact is and who gave it. Compares by reference through <see cref="Requires"/>.</summary>
public sealed record CoreEffect(IReadOnlyDictionary<CoreType, int> Requires, string Kind, string? Trigger, SlotId? Target,
    double WatchSeconds, string? Text, string Tag, string Source, bool Self = false)
{
    public bool Tracks => Kind is CoreEffects.Reset or CoreEffects.Reduce && Trigger is not null && (Target is not null || Self);
}

/// <summary>Slot <see cref="TargetSlot"/> is to be read on every frame for <see cref="Duration"/> after <see cref="TriggerSlot"/> is cast.</summary>
public sealed record CoreWatch(SlotId TriggerSlot, SlotId TargetSlot, TimeSpan Duration);

/// <summary>코어 빌드's shipped data and what a saved <see cref="CoreChoice"/> makes of it. Pure: nothing here reads the screen or the
/// keyboard; the engine calls <see cref="Watches"/> when a slot is cast.</summary>
public static class CoreEffects
{
    public const string Reset = "cooldown_reset", Reduce = "cooldown_reduce", TextOnly = "text";
    /// <summary>"target": the trigger's own slot (its cast may reset it).</summary>
    const string SelfTarget = "self";

    /// <summary>The longest watch the data may ask for, in seconds (a read per frame costs a little).</summary>
    public const double MaxWatchSeconds = 5;
    const double DefaultWatchSeconds = 1;

    static readonly IReadOnlyDictionary<string, IReadOnlyList<CoreEffect>> None = new Dictionary<string, IReadOnlyList<CoreEffect>>();

    /// <summary>The cores a 직업각인 of this role chooses: 해 and 달 for a dealer, 별 for a supporter.</summary>
    public static IReadOnlyList<CoreType> Allowed(Role role) => role == Role.서포터 ? [CoreType.별] : [CoreType.해, CoreType.달];

    /// <summary>Profiles/cores/&lt;className&gt;.json by 직업각인. A class without a file has no effects; so has one whose file can't be
    /// read or fails <see cref="Parse"/>, which is logged. Never throws for absence or bad data. Uncached: callers cache.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CoreEffect>> Load(string profilesDir, string className) =>
        Load(profilesDir, className, out _);

    /// <summary>As <see cref="Load(string, string)"/>; <paramref name="readFailed"/> is true when the file exists but could not be read
    /// (locked, no access), which may pass, so a caller keeping the result should read again later. A missing or malformed file is not a
    /// read failure: it reads the same next time.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CoreEffect>> Load(string profilesDir, string className, out bool readFailed)
    {
        readFailed = false;
        if (className.Length == 0 || className.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return None;
        var file = Path.Combine(profilesDir, "cores", className + ".json");
        if (!File.Exists(file)) return None;
        try { return Parse(File.ReadAllText(file)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            System.Diagnostics.Trace.WriteLine($"[cores] {file}: {ex.Message}");
            readFailed = ex is IOException or UnauthorizedAccessException;
            return None;
        }
    }

    /// <summary>A cores file's text as effects per 직업각인. Strict, so a typo can't silently do nothing: throws
    /// <see cref="InvalidDataException"/> for a requires that is missing, empty, names a core other than 해/달/별 or a level outside 1–3;
    /// a repeated key or one it doesn't know (other than "_note"-style ones); an unknown "effect"; a cooldown effect without a
    /// non-blank trigger or a target slot (an exact slot id); "watchSeconds" outside (0, 5] (default 1); or a text effect
    /// without "text".</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<CoreEffect>> Parse(string json)
    {
        try { return ParseNodes(json); }
        catch (ArgumentException ex) { throw new InvalidDataException($"cores: a key is repeated ({ex.Message})."); }   // JsonNode throws at the first read of a repeated key
    }

    static Dictionary<string, IReadOnlyList<CoreEffect>> ParseNodes(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root || root["subclasses"] is not JsonObject subclasses)
            throw new InvalidDataException("cores: expected { \"subclasses\": { 직업각인: { \"effects\": [...] } } }.");
        OnlyKeys(root, "the file", "subclasses");
        var result = new Dictionary<string, IReadOnlyList<CoreEffect>>();
        foreach (var (sub, node) in subclasses)
        {
            if (node is not JsonObject o || o["effects"] is not JsonArray list) throw Bad(sub, "needs an \"effects\" list");
            OnlyKeys(o, sub, "effects");
            result[sub] = [.. list.Select(e => ParseEffect(sub, e))];
        }
        return result;
    }

    static InvalidDataException Bad(string sub, string what) => new($"cores: {sub} {what}.");

    /// <summary>Rejects a key that is not in <paramref name="allowed"/>, so a misspelt one can't be ignored; "_note"-style keys may stay.</summary>
    static void OnlyKeys(JsonObject o, string where, params string[] allowed)
    {
        if (o.Select(p => p.Key).FirstOrDefault(k => !k.StartsWith('_') && !allowed.Contains(k, StringComparer.Ordinal)) is { } unknown)
            throw Bad(where, $"has an unknown key \"{unknown}\" (allowed: {string.Join(", ", allowed)})");
    }

    /// <summary>The member of <typeparamref name="T"/> named exactly <paramref name="name"/>; Enum.TryParse would also take "0" and "a,b".</summary>
    static bool Named<T>(string name, out T value) where T : struct, Enum
    {
        bool ok = Enum.GetNames<T>().Contains(name, StringComparer.Ordinal);
        value = ok ? Enum.Parse<T>(name) : default;
        return ok;
    }

    static CoreEffect ParseEffect(string sub, JsonNode? node)
    {
        if (node is not JsonObject e) throw Bad(sub, "has an effect that is not an object");
        OnlyKeys(e, sub, "requires", "effect", "trigger", "target", "watchSeconds", "text", "tag", "source");
        string? Str(string name) => e[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        string kind = Str("effect") is Reset or Reduce or TextOnly ? Str("effect")! : throw Bad(sub, "has an unknown \"effect\" (cooldown_reset, cooldown_reduce or text)");
        var (trigger, text) = (Str("trigger"), Str("text"));
        bool self = Str("target") == SelfTarget;
        SlotId? target = self ? null : Str("target") is { } t ? (Named<SlotId>(t, out var slot) ? slot : throw Bad(sub, $"has an unknown target slot \"{t}\"")) : null;
        if (kind != TextOnly && (string.IsNullOrWhiteSpace(trigger) || target is null && !self)) throw Bad(sub, $"{kind} needs a \"trigger\" and a \"target\" slot");
        if (kind == TextOnly && text is null) throw Bad(sub, "text effect needs \"text\"");

        double seconds = e["watchSeconds"] is null ? DefaultWatchSeconds
            : e["watchSeconds"] is JsonValue w && w.TryGetValue<double>(out var d) && d > 0 && d <= MaxWatchSeconds ? d
            : throw Bad(sub, $"has a \"watchSeconds\" outside 0–{MaxWatchSeconds}");
        return new CoreEffect(ParseRequires(sub, e["requires"]), kind, trigger, target, seconds, text, Str("tag") ?? "", Str("source") ?? "", self);
    }

    static Dictionary<CoreType, int> ParseRequires(string sub, JsonNode? node)
    {
        if (node is not JsonObject r || r.Count == 0) throw Bad(sub, "has an effect with no \"requires\"");
        var requires = new Dictionary<CoreType, int>();
        foreach (var (name, level) in r)
        {
            if (!Named<CoreType>(name, out var core)) throw Bad(sub, $"requires \"{name}\", which is not 해, 달 or 별");
            if (level is not JsonValue v || !v.TryGetValue<int>(out var n) || n is < CoreChoice.MinLevel or > CoreChoice.MaxLevel)
                throw Bad(sub, $"requires {name} at a level outside {CoreChoice.MinLevel}–{CoreChoice.MaxLevel}");
            requires[core] = n;
        }
        return requires;
    }

    /// <summary>The effects <paramref name="choice"/> switches on for a 직업각인 of <paramref name="role"/>: none unless it is applied;
    /// those whose every required core is one the role chooses and is at the level required.</summary>
    public static IReadOnlyList<CoreEffect> Active(CoreChoice choice, Role role, IEnumerable<CoreEffect> effects)
    {
        if (!choice.Apply) return [];
        var allowed = Allowed(role);
        return [.. effects.Where(e => e.Requires.Count > 0 && e.Requires.All(r => allowed.Contains(r.Key) && choice.Of(r.Key) == r.Value))];
    }

    /// <summary>What to watch: for each active effect that <see cref="CoreEffect.Tracks"/> and whose trigger skill the user placed in a
    /// slot (<see cref="CoreChoice.Slots"/>). An effect with no slot chosen gives none.</summary>
    public static IReadOnlyList<CoreWatch> Watches(CoreChoice choice, Role role, IEnumerable<CoreEffect> effects)
    {
        var watches = new List<CoreWatch>();
        foreach (var e in Active(choice, role, effects))
            if (e.Tracks && choice.Slots.TryGetValue(e.Trigger!, out var trigger))
                watches.Add(new CoreWatch(trigger, e.Self ? trigger : e.Target!.Value, TimeSpan.FromSeconds(e.WatchSeconds)));
        return watches;
    }

    /// <summary>What the engine does when <paramref name="cast"/> is seen at frame time <paramref name="at"/>: each slot to read on every
    /// frame, and until when (<c>at + Duration</c>), from the <paramref name="watches"/> the settings gave (null: none).</summary>
    public static IReadOnlyList<(SlotId Target, TimeSpan Until)> WatchesAfterCast(IReadOnlyList<CoreWatch>? watches, SlotId cast, TimeSpan at) =>
        watches is null ? [] : [.. watches.Where(w => w.TriggerSlot == cast).Select(w => (w.TargetSlot, at + w.Duration))];

    /// <summary>True when <paramref name="slot"/>'s own cast may reset it (a "self" watch): the tracker then believes Ready soon
    /// after its cast flash (<see cref="Cooldown.CooldownTracker.ResetHold"/>).</summary>
    public static bool ResetsOnUse(IReadOnlyList<CoreWatch>? watches, SlotId slot) =>
        watches?.Any(w => w.TriggerSlot == slot && w.TargetSlot == slot) == true;

    /// <summary>The skills whose slot 설정 asks for (user, 2026-10-13): those of the effects <paramref name="choice"/>'s levels switch
    /// on, whether or not 적용 is on (off, the rows are greyed). Another level's skills aren't asked.</summary>
    public static IReadOnlyList<string> Triggers(CoreChoice choice, Role role, IEnumerable<CoreEffect> effects) =>
        [.. Active(choice with { Apply = true }, role, effects).Where(e => e.Tracks).Select(e => e.Trigger!).Distinct()];

    /// <summary>The cores an effect needs as the game writes a build (user, 2026-10-13): 해, 달 and 별 levels in that order, x where
    /// it doesn't ask ("3xx" = 해 3; "x2x" = 달 2).</summary>
    public static string Code(IReadOnlyDictionary<CoreType, int> requires) =>
        string.Concat(Enum.GetValues<CoreType>().Select(c => requires.TryGetValue(c, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "x"));

    /// <summary>The engine's call when <paramref name="cast"/> is seen at frame time <paramref name="at"/>: hands each
    /// <see cref="WatchesAfterCast"/> pair to <paramref name="watch"/> (the scheduler's Watch).</summary>
    public static void ApplyAfterCast(IReadOnlyList<CoreWatch>? watches, SlotId cast, TimeSpan at, Action<SlotId, TimeSpan> watch)
    {
        foreach (var (target, until) in WatchesAfterCast(watches, cast, at)) watch(target, until);
    }

    /// <summary>One short Korean line for 설정 → 캐릭터's table: per effect, the cores it needs and what it does ("해 3: 버스트 캐넌을
    /// 쓰면 아덴2(C) 쿨초를 빨리 읽습니다"); a text effect says its own text. <paramref name="keyName"/> names a slot by the user's key (the
    /// slot's own name when none is set, which then stands alone); null: the slot's name.</summary>
    public static string Describe(IEnumerable<CoreEffect> effects, Func<SlotId, string>? keyName = null) => string.Join(" / ", effects.Select(e =>
        $"{Code(e.Requires)}: " +
        (e.Text ?? (e.Self ? $"{Josa.Object(e.Trigger!)} 쓴 뒤 바로 다시 준비되면 곧바로 보여 줍니다"
            : $"{Josa.Object(e.Trigger!)} 쓰면 {Named(e.Target!.Value, keyName)} 쿨초를 빨리 읽습니다"))));

    static string Named(SlotId slot, Func<SlotId, string>? keyName) =>
        keyName?.Invoke(slot) is { } key && key != slot.ToString() ? $"{slot}({key})" : slot.ToString();
}
