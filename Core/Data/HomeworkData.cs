using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Loads the 숙제 raids from Profiles/raids.json ("homework"): data, so a raid or a difficulty is added without a
/// code change. A raid without difficulties is skipped, and one without gates too unless a level has an entry: that one is
/// picked by level and cleared only by its [클리어] button (no boss data, so no fight clears it). None when the file is
/// missing or broken.</summary>
public static class HomeworkData
{
    public static IReadOnlyList<HomeworkRaid> Load(string profilesDir)
    {
        try
        {
            var path = Path.Combine(profilesDir, RaidData.FileName);
            if (!File.Exists(path)) return [];
            var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), Json);
            return [.. (file?.Homework ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Id) && r.Levels is { Count: > 0 } && (r.Gates is { Count: > 0 } || r.Levels.Any(l => l.Entry is not null)))
                .Select(r => new HomeworkRaid(r.Id!, r.Name ?? r.Id!, r.Gates ?? [],
                    [.. r.Levels!.Where(l => !string.IsNullOrWhiteSpace(l.Difficulty)).Select(l => new HomeworkLevel(
                        l.Difficulty!, l.Tag ?? l.Difficulty!, Enum.TryParse<HomeworkColor>(l.Color, true, out var c) ? c : HomeworkColor.White, l.Last ?? [], l.Entry, l.EndsHidden))]))];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    sealed record FileDto(List<RaidDto>? Homework);
    sealed record RaidDto(string? Id, string? Name, List<string>? Gates, List<LevelDto>? Levels);
    sealed record LevelDto(string? Difficulty, string? Tag, string? Color, List<string>? Last, int? Entry, bool EndsHidden = false);
}
