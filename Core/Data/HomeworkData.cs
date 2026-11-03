using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>Loads the 숙제 raids from Profiles/raids.json ("homework"): data, so a raid or a difficulty is added without a
/// code change. A raid without gates or difficulties is skipped; none when the file is missing or broken.</summary>
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
                .Where(r => !string.IsNullOrWhiteSpace(r.Id) && r.Gates is { Count: > 0 } && r.Levels is { Count: > 0 })
                .Select(r => new HomeworkRaid(r.Id!, r.Name ?? r.Id!, r.Gates!,
                    [.. r.Levels!.Where(l => !string.IsNullOrWhiteSpace(l.Difficulty)).Select(l => new HomeworkLevel(
                        l.Difficulty!, l.Tag ?? l.Difficulty!, Enum.TryParse<HomeworkColor>(l.Color, true, out var c) ? c : HomeworkColor.White, l.Last ?? []))]))];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    sealed record FileDto(List<RaidDto>? Homework);
    sealed record RaidDto(string? Id, string? Name, List<string>? Gates, List<LevelDto>? Levels);
    sealed record LevelDto(string? Difficulty, string? Tag, string? Color, List<string>? Last);
}
