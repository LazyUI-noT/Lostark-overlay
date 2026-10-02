using System.Diagnostics;

namespace GaugeOverlay.Core.Data;

/// <summary>Installs an unpacked update next to the running app. Windows lets a running exe be renamed but not overwritten, so
/// GaugeOverlay.exe and Profiles are renamed to *.old and the new ones copied in (copied, not moved: the update folder may be
/// on another drive). Any failure puts the old files back. The next start removes the leftovers (<see cref="Cleanup"/>).</summary>
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

        bool exeAside = false, profilesAside = false, profilesIn = false;
        try
        {
            File.Move(exe, exe + Old); exeAside = true;
            File.Copy(newExe, exe);
            if (Directory.Exists(profiles)) { Directory.Move(profiles, profiles + Old); profilesAside = true; }
            profilesIn = true;   // from here a failure may have copied part of it
            CopyDir(newProfiles, profiles);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[update] swap failed: {ex.Message}");
            Revert(exe, profiles, exeAside, profilesAside, profilesIn);
            return ex.Message;
        }
    }

    static void Revert(string exe, string profiles, bool exeAside, bool profilesAside, bool profilesIn)
    {
        try
        {
            if (profilesIn && Directory.Exists(profiles)) Directory.Delete(profiles, recursive: true);
            if (profilesAside) Directory.Move(profiles + Old, profiles);
            if (exeAside)
            {
                if (File.Exists(exe)) File.Delete(exe);   // the new one, whole or cut off by a failed copy
                File.Move(exe + Old, exe);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[update] revert failed: {ex.Message}"); }
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from)) CopyDir(dir, Path.Combine(to, Path.GetFileName(dir)));
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
