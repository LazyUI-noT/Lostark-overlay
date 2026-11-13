using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using Microsoft.Win32;

namespace GaugeOverlay.App.Services;

/// <summary>This run's buff lock (buff-lock.design.md): the key kept in settings checked at start, a new one checked at entry.
/// The meter (App.Wiring) and the timeline read <see cref="Locked"/>; the state lives only in memory.</summary>
internal static class BuffUnlock
{
    static readonly Lazy<string?> Pc = new(() => MachineGuid() is { Length: > 0 } id ? BuffKey.PcCode(id) : null);

    /// <summary>This PC's code for a key (a hash of the Windows MachineGuid, never the ID itself); null when the ID can't be
    /// read: no code to hand out, and every key fails (a shared code for all such PCs would let one key open them all).</summary>
    public static string? PcCode => Pc.Value;

    static volatile BuffKeyResult _result = BuffKeyResult.없음;
    public static BuffKeyResult Result => _result;

    /// <summary>--demo's --buff-lock=on|off, for screenshots (set once, before start); null: as the key says.</summary>
    public static bool? Forced { get; set; }

    public static bool Locked => Forced ?? Result != BuffKeyResult.열림;

    /// <summary>At start: the kept key, without its time (a kept key lasts until <see cref="BuffKey.Reset"/> moves on).</summary>
    public static void Start(Settings s)
    {
        _result = BuffKey.Check(s.BuffUnlockKey, PcCode ?? "", DateTimeOffset.UtcNow, entering: false);
        Trace.WriteLine($"[buff-lock] {Result}");
    }

    /// <summary>A key typed on the page: kept only when it opens; a failing one leaves the lock (and an earlier key) as it was.</summary>
    public static BuffKeyResult Enter(Settings s, string key)
    {
        var r = BuffKey.Check(key, PcCode ?? "", DateTimeOffset.UtcNow, entering: true);
        Trace.WriteLine($"[buff-lock] entered: {r}");
        if (r != BuffKeyResult.열림) return r;
        (s.BuffUnlockKey, _result) = (key.Trim(), r);
        return r;
    }

    static string? MachineGuid()
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var crypto = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return crypto?.GetValue("MachineGuid") as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Trace.WriteLine($"[buff-lock] machine id not read: {e.GetType().Name}");
            return null;
        }
    }
}
