namespace GaugeOverlay.Core.Domain;

/// <summary>A part of a box's needs: 공통 (either way), 기본, 대체, 선택 (metro-map.plan.md).</summary>
public enum MapNeedRole { Common, Main, Fallback, Optional }

/// <summary>One need: any one of <paramref name="Alternatives"/> is enough (택1). An id is a box, "#…" a condition that isn't one
/// (<see cref="FeatureMap.Satisfied"/>). <paramref name="Depth"/> &gt; 0: brought in by the off box above it.</summary>
public sealed record MapNeed(IReadOnlyList<string> Alternatives, int Depth, bool Optional = false);

/// <summary>A section of the hover card; <paramref name="Met"/>: every needed top row has an alternative that is on.</summary>
public sealed record MapNeedSection(MapNeedRole Role, IReadOnlyList<MapNeed> Rows, bool Met);

public static partial class FeatureMap
{
    const int MaxNeedDepth = 2;

    /// <summary>The ways a box can work, written out where its arrows don't say it (Homework.SourceOf, HomeworkService.Menu);
    /// every other box needs its solid arrows and takes its dashed ones.</summary>
    static readonly Dictionary<string, (MapNeedRole Role, string[] Alternatives, bool Optional)[]> NeedTables = new()
    {
        ["hw"] =
        [
            (MapNeedRole.Common, ["char"], false), (MapNeedRole.Common, ["dungeon"], false), (MapNeedRole.Common, ["api", "#oldinstall"], false),
            (MapNeedRole.Main, ["rec"], false),
            (MapNeedRole.Fallback, ["boss"], false), (MapNeedRole.Fallback, ["shadow"], true),   // HomeworkFallback reads the boss bar
        ],
        ["char"] = [(MapNeedRole.Common, ["api", "#registered", "#fightnames"], false)],   // names lock on only to known ones
        ["rec"] =
        [
            (MapNeedRole.Common, ["g-jeonbun"], false),
            (MapNeedRole.Optional, ["skill"], true), (MapNeedRole.Optional, ["dungeon"], true), (MapNeedRole.Optional, ["api"], true),
        ],
    };

    /// <summary>The card's name for a need: a box's title, or a condition's.</summary>
    public static string NeedTitle(string need) => need switch
    {
        "#oldinstall" => "키 없이 쓰던 설치",
        "#registered" => "등록한 캐릭터",
        "#fightnames" => "전투 기록의 이름",
        _ => Boxes.First(b => b.Id == need).Title,
    };

    /// <summary>What box <paramref name="id"/> needs, section by section in the card's order; a section with no rows is left out.
    /// An off box needed alone brings its own 필수 rows under it, <see cref="MaxNeedDepth"/> steps at most, each box once.</summary>
    public static IReadOnlyList<MapNeedSection> Needs(Settings s, string id, bool hasKey)
    {
        var table = TableOf(id);
        var sections = new List<MapNeedSection>();
        foreach (var role in Enum.GetValues<MapNeedRole>())
        {
            var top = table.Where(t => t.Role == role).ToList();
            if (top.Count == 0) continue;
            var seen = new HashSet<string> { id };
            var rows = top.SelectMany(t => Expand(s, hasKey, t.Alternatives, t.Optional, 0, seen)).ToList();
            bool met = top.Where(t => !t.Optional).All(t => t.Alternatives.Any(a => Satisfied(s, a, hasKey)));
            sections.Add(new MapNeedSection(role, rows, met));
        }
        return sections;
    }

    /// <summary>The need is there: a box that is on (<c>api</c>: a key saved, <c>hw</c>: <see cref="Settings.HomeworkOn"/>), or a
    /// condition that holds. A box with no switch yet (직업 인식, 콘텐츠 인식, 캐릭터 이름 인식) is never off.</summary>
    public static bool Satisfied(Settings s, string need, bool hasKey) => need switch
    {
        "api" => hasKey,
        "hw" => s.HomeworkOn(hasKey),
        "#oldinstall" => s.HomeworkWithoutKey,
        "#registered" => s.Homework.Characters.Count > 0,   // the 숙제 list: added by hand or read from the grid
        "#fightnames" => true,   // a 전투 기록's names, read without a key
        _ => State(s, need) != BoxState.Off,
    };

    static IEnumerable<MapNeed> Expand(Settings s, bool hasKey, string[] alternatives, bool optional, int depth, HashSet<string> seen)
    {
        yield return new MapNeed(alternatives, depth, optional);
        var one = alternatives[0];
        if (depth >= MaxNeedDepth || alternatives.Length != 1 || one.StartsWith('#') || Satisfied(s, one, hasKey) || !seen.Add(one)) yield break;
        foreach (var sub in TableOf(one).Where(t => t.Role == MapNeedRole.Common))
            foreach (var row in Expand(s, hasKey, sub.Alternatives, optional, depth + 1, seen))
                yield return row;
    }

    /// <summary>The box's table, or one from its arrows: party lines, game settings (column 0) and boxes inside it left out;
    /// solid = 필수, dashed = 선택, one row per source.</summary>
    static IReadOnlyList<(MapNeedRole Role, string[] Alternatives, bool Optional)> TableOf(string id) =>
        NeedTables.TryGetValue(id, out var table) ? table
        : [.. Edges.Where(e => e.To == id && !e.Party && Boxes.First(b => b.Id == e.From) is var from && from.Column > 0 && from.Inside != id)
            .DistinctBy(e => e.From)
            .Select(e => (e.Optional ? MapNeedRole.Optional : MapNeedRole.Common, new[] { e.From }, e.Optional))
            .OrderBy(t => t.Item1)];
}
