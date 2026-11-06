using System.IO.Compression;
using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>전투 기록 내보내기 / 가져오기 (homework.plan.md, user 2026-11-04): fights shared as one <see cref="Extension"/> file (gzip
/// JSON), sent on Discord and opened by another user's app. No names: I am 나, the others 파티원 1, 2… in member order, and the
/// character name is left out; the class · 직업각인 stays. A file that isn't one reads as no fights.</summary>
public static class RecordExport
{
    public const string Extension = ".gofight";
    public const int MaxFights = 200;
    // An imported fight's size: past these it isn't a real fight (a 10-minute 8-player one has ~5k samples, a few thousand
    // events) and drawing it would stall the page.
    public const int MaxMembers = 16, MaxPhases = 64, MaxSamples = 100_000, MaxEvents = 50_000, MaxBoss = 50_000, MaxText = 64;

    /// <summary>An imported fight small enough to draw: counts and texts in bounds.</summary>
    public static bool Fits(FightRecord r) =>
        r.Members.Count <= MaxMembers && r.Phases.Count <= MaxPhases && r.Samples.Count <= MaxSamples && r.Events.Count <= MaxEvents && r.Boss.Count <= MaxBoss
        && new[] { r.Me, r.Gate, r.Difficulty, r.MyClassName, r.MySubclass, r.ProposedClass }.All(s => s is null || s.Length <= MaxText)
        && r.Members.All(m => m.Name.Length <= MaxText)
        && (r.Skills ?? new Dictionary<SlotId, RecordSkill>()).Values.All(s => s.Name.Length <= MaxText)
        && (r.Unsure ?? new Dictionary<SlotId, IReadOnlyList<RecordSkill>>()).Values.All(c => c.Count <= MaxCandidates && c.All(s => s.Name.Length <= MaxText));

    /// <summary>The candidates an unsure slot keeps (FightSkills.Apply).</summary>
    public const int MaxCandidates = 3;
    const int Version = 1;
    const string MeName = "나";

    /// <summary>The fight as it is shared: the names replaced, the character dropped, not 보관.</summary>
    public static FightRecord Anonymous(FightRecord r)
    {
        var names = new Dictionary<string, string> { [r.Me] = MeName };
        foreach (var m in r.Members.Where(m => m.Name != r.Me)) names.TryAdd(m.Name, $"파티원 {names.Count}");
        foreach (var n in r.Samples.Select(s => s.Name).Concat(r.Events.Select(e => e.Name))) names.TryAdd(n, $"파티원 {names.Count}");
        return r with
        {
            Me = MeName, Character = null, Kept = false,
            Members = [.. r.Members.Select(m => m with { Name = names[m.Name] })],
            Samples = [.. r.Samples.Select(s => s with { Name = names[s.Name] })],
            Events = [.. r.Events.Select(e => e with { Name = names[e.Name] })],
        };
    }

    /// <summary>Writes <paramref name="fights"/>, made <see cref="Anonymous"/>.</summary>
    public static void Write(Stream to, IReadOnlyList<FightRecord> fights)
    {
        using var gz = new GZipStream(to, CompressionLevel.Optimal, leaveOpen: true);
        JsonSerializer.Serialize(gz, new ExportFile(Version, [.. fights.Select(Anonymous)]), SettingsStore.Json);
    }

    /// <summary>The fights in an export that can be drawn (at most <see cref="MaxFights"/>); none for anything else.</summary>
    public static IReadOnlyList<FightRecord> Read(Stream from)
    {
        try
        {
            using var gz = new GZipStream(from, CompressionMode.Decompress, leaveOpen: true);
            using var json = new MemoryStream();
            var buffer = new byte[81920];
            for (int n; (n = gz.Read(buffer)) > 0;)
            {
                json.Write(buffer, 0, n);
                if (json.Length > RecordStore.MaxUnpacked) return [];
            }
            json.Position = 0;
            var file = JsonSerializer.Deserialize<ExportFile>(json, SettingsStore.Json);
            return [.. (file?.Fights ?? []).Where(r => RecordStore.IsValid(r) && Fits(r)).Take(MaxFights).Select(r => r with { Character = null })];
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or NotSupportedException) { return []; }
    }

    sealed record ExportFile(int Version, IReadOnlyList<FightRecord>? Fights);
}
