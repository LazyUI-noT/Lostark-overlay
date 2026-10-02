using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.App.Services;

/// <summary>This PC's party identity (<see cref="DeviceIdentity"/>) as %LOCALAPPDATA%\GaugeOverlay\device.key (PKCS#8;
/// local, not roaming, so a synced profile doesn't put the same identity on two PCs): made on first use and replaced if
/// it isn't a key, after which hosts ask about this PC again. Not encrypted: it is only as safe as the Windows user
/// profile it sits in.</summary>
static class DeviceKeyFile
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaugeOverlay", "device.key");

    public static DeviceIdentity Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                if (DeviceIdentity.Import(File.ReadAllBytes(path)) is { } saved) return saved;
                Trace.WriteLine("[party] device.key unreadable: making a new one");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Locked for a moment (antivirus, backup): the saved key stays as it is, and this run uses a temporary one.
            Trace.WriteLine($"[party] device.key: {e.Message}; using a temporary key");
            return DeviceIdentity.Create();
        }

        var fresh = DeviceIdentity.Create();
        var bytes = fresh.Export();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        // Still usable for this run; the next start makes another, and hosts ask again.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[party] device.key not saved: {e.Message}"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        return fresh;
    }
}
