using System.Diagnostics;

namespace GaugeOverlay.Core.Data;

/// <summary>Installs an unpacked update next to the running app. Windows lets a running exe be renamed but not overwritten, so
/// GaugeOverlay.exe is renamed to *.old and the new one copied in (copied, not moved: the update folder may be on another
/// drive). Profiles is copied to Profiles.old and its files replaced in place: a folder can't be moved while another program
/// (Explorer, a virus scan) has a file in it open (user's log, 2026-10-13), but the files can still be read and written. Any
/// failure puts the old files back. The next start removes the leftovers (<see cref="Cleanup"/>).</summary>
public static class UpdateInstaller
{
    public const string ExeName = "GaugeOverlay.exe", ProfilesName = "Profiles";
    const string Old = ".old";
    /// <summary>Limits on an update zip, unpacked: the exe is about 200 MB and Profiles a few hundred small files.</summary>
    public const long MaxUnpacked = 600_000_000;
    public const int MaxEntries = 2000;

    /// <summary>Checked before unpacking: only GaugeOverlay.exe and files under Profiles/, the exe there, at most
    /// <see cref="MaxEntries"/> entries and <see cref="MaxUnpacked"/> bytes. Null when fine, else why not.</summary>
    public static string? ZipProblem(IReadOnlyList<(string Name, long Length)> entries)
    {
        if (entries.Count > MaxEntries) return "업데이트 파일에 항목이 너무 많습니다.";
        if (entries.Sum(e => e.Length) > MaxUnpacked) return "업데이트 파일이 너무 큽니다.";
        var names = entries.Select(e => e.Name.Replace('\\', '/')).ToList();
        if (!names.Contains(ExeName)) return "업데이트 파일에 GaugeOverlay.exe가 없습니다.";
        var odd = names.FirstOrDefault(n => n != ExeName && !(n.StartsWith(ProfilesName + "/", StringComparison.Ordinal) && !n.Split('/').Contains("..")));
        return odd is null ? null : $"업데이트 파일에 예상하지 못한 항목이 있습니다: {odd}";
    }

    /// <summary>Puts <paramref name="newDir"/>'s exe and Profiles into <paramref name="appDir"/>. Null on success, else the
    /// reason, with the old files back in place.</summary>
    public static string? Swap(string appDir, string newDir)
    {
        string exe = Path.Combine(appDir, ExeName), profiles = Path.Combine(appDir, ProfilesName);
        string newExe = Path.Combine(newDir, ExeName), newProfiles = Path.Combine(newDir, ProfilesName);
        if (!File.Exists(newExe) || !Directory.Exists(newProfiles)) return "업데이트 파일에 GaugeOverlay.exe나 Profiles가 없습니다.";
        Cleanup(appDir, null);   // a leftover *.old from an earlier update would block the renames

        bool exeAside = false, profilesBacked = false, profilesIn = false;
        try
        {
            File.Move(exe, exe + Old); exeAside = true;
            File.Copy(newExe, exe);
            if (Directory.Exists(profiles)) { Mirror(profiles, profiles + Old); profilesBacked = true; }
            profilesIn = true;   // from here a failure may have replaced part of it
            Mirror(newProfiles, profiles);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[update] swap failed: {ex.Message}");
            Revert(exe, profiles, exeAside, profilesBacked, profilesIn);
            return ex.Message;
        }
    }

    static void Revert(string exe, string profiles, bool exeAside, bool profilesBacked, bool profilesIn)
    {
        try
        {
            if (profilesIn && profilesBacked) Mirror(profiles + Old, profiles);
            else if (profilesIn && Directory.Exists(profiles)) Directory.Delete(profiles, recursive: true);
            if (Directory.Exists(profiles + Old)) Directory.Delete(profiles + Old, recursive: true);   // also a backup cut off by the failure
            if (exeAside)
            {
                if (File.Exists(exe)) File.Delete(exe);   // the new one, whole or cut off by a failed copy
                File.Move(exe + Old, exe);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[update] revert failed: {ex.Message}"); }
    }

    /// <summary>Makes <paramref name="to"/> hold exactly <paramref name="from"/>'s files: each written over in place (truncated and
    /// rewritten, which a program reading it with write sharing allows), and those <paramref name="from"/> doesn't have removed.</summary>
    static void Mirror(string from, string to)
    {
        Directory.CreateDirectory(to);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(from))
        {
            var name = Path.GetFileName(file);
            names.Add(name);
            Retry(() =>
            {
                using var src = File.OpenRead(file);
                using var dst = new FileStream(Path.Combine(to, name), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                src.CopyTo(dst);
            });
        }
        foreach (var dir in Directory.GetDirectories(from))
        {
            names.Add(Path.GetFileName(dir));
            Mirror(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
        foreach (var stale in Directory.GetFiles(to).Where(f => !names.Contains(Path.GetFileName(f)))) Retry(() => File.Delete(stale));
        foreach (var stale in Directory.GetDirectories(to).Where(d => !names.Contains(Path.GetFileName(d)))) Retry(() => Directory.Delete(stale, recursive: true));
    }

    const int Attempts = 5;
    static readonly TimeSpan RetryWait = TimeSpan.FromMilliseconds(200);

    /// <summary>A virus scan may hold a just-unpacked file for a moment: a few tries before giving up.</summary>
    static void Retry(Action a)
    {
        for (int i = 1; ; i++)
        {
            try { a(); return; }
            catch (IOException) when (i < Attempts) { Thread.Sleep(RetryWait); }
        }
    }

    /// <summary>Removes GaugeOverlay.exe.old, Profiles.old and the download folder. Whatever is still locked (the old process
    /// not gone yet) is left for the next start.</summary>
    public static void Cleanup(string appDir, string? updateDir)
    {
        Try(() => File.Delete(Path.Combine(appDir, ExeName + Old)));
        Try(() => { if (Directory.Exists(Path.Combine(appDir, ProfilesName + Old))) Directory.Delete(Path.Combine(appDir, ProfilesName + Old), recursive: true); });
        if (updateDir is not null) Try(() => { if (Directory.Exists(updateDir)) Directory.Delete(updateDir, recursive: true); });
    }

    static void Try(Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[update] cleanup: {ex.Message}"); }
    }
}
