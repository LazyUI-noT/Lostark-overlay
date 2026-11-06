using System.Text.Json;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Data;

/// <summary>숙제's clears (homework.plan.md): one small JSON list, kept apart from 전투 기록 so a pruned or deleted fight never
/// un-clears a raid, and from settings so an undo can't take one back. Only this week's are kept. A file that can't be read is
/// read as none (the week's clears are lost, nothing else).</summary>
public sealed class HomeworkStore(string path)
{
    readonly object _gate = new();   // the save thread and the UI's 편집 take turns

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaugeOverlay", "homework.json");

    public IReadOnlyList<HomeworkClear> Load()
    {
        lock (_gate) return Read();
    }

    /// <summary>Adds <paramref name="clear"/> and drops the clears from before this week; false when it was there already.</summary>
    public bool Add(HomeworkClear clear, DateTime now)
    {
        lock (_gate)
        {
            var week = Homework.WeekStart(now);
            if (clear.At < week) return false;   // last week's (a fight given its character late): nothing to cross out
            var all = Read().Where(c => c.At >= week).ToList();
            // A sure clear replaces a maybe of the same raid; a maybe never replaces a sure one (a fight and the fallback never both decide).
            bool added = !all.Contains(clear) && (!clear.Unsure || !all.Any(c => c.Character == clear.Character && c.Raid == clear.Raid && !c.Unsure));
            if (added)
            {
                all.RemoveAll(c => c.Character == clear.Character && c.Raid == clear.Raid && c.Unsure);
                all.Add(clear);
            }
            Write(all);
            return added;
        }
    }

    /// <summary>Takes <paramref name="clear"/> back (편집 of its fight, confirmed by the user, or the fallback's own clear on a retry);
    /// false when it wasn't there, as when a click replaced it.</summary>
    public bool Remove(HomeworkClear clear)
    {
        lock (_gate)
        {
            var all = Read().ToList();
            if (!all.Remove(clear)) return false;
            Write(all);
            return true;
        }
    }

    /// <summary>The 미완 / 클리어 / 확인필요 part clicked: this week's clear of the character's raid becomes <paramref name="to"/>, or none (null).</summary>
    public void Set(string character, string raid, DateTime now, HomeworkClear? to)
    {
        lock (_gate)
        {
            var week = Homework.WeekStart(now);
            var all = Read().Where(c => c.At >= week && !(c.Character == character && c.Raid == raid)).ToList();
            if (to is not null) all.Add(to);
            Write(all);
        }
    }

    /// <summary>A click on 클리어 or an added raid's tag: <paramref name="clear"/> kept, or, when its character cleared that raid this week,
    /// that taken back. True when the raid is cleared now.</summary>
    public bool Toggle(HomeworkClear clear)
    {
        lock (_gate)
        {
            var week = Homework.WeekStart(clear.At);
            var all = Read().Where(c => c.At >= week).ToList();
            bool cleared = all.RemoveAll(c => c.Character == clear.Character && c.Raid == clear.Raid) == 0;
            if (cleared) all.Add(clear);
            Write(all);
            return cleared;
        }
    }

    List<HomeworkClear> Read()
    {
        try
        {
            if (!File.Exists(path)) return [];
            return [.. (JsonSerializer.Deserialize<List<HomeworkClear>>(File.ReadAllText(path), SettingsStore.Json) ?? [])
                .Where(c => c is { Character: not null, Raid: not null, Difficulty: not null })];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return []; }
    }

    void Write(List<HomeworkClear> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, SettingsStore.Json));
        File.Move(tmp, path, overwrite: true);
    }
}
