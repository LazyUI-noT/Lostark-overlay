using System.Runtime.InteropServices;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Platform;

/// <summary>This app's GPU load from Windows' "GPU Engine" performance counter (PDH), for 리소스 검사. <see cref="Open"/> is null
/// when the counter is missing (old Windows, counters turned off): the page then says 측정할 수 없음. Never throws.</summary>
internal sealed partial class GpuCounter : IDisposable
{
    const uint PdhMoreData = 0x800007D2, FmtDouble = 0x200;
    IntPtr _query, _counter;
    readonly int _pid = Environment.ProcessId;

    public static GpuCounter? Open()
    {
        try
        {
            var g = new GpuCounter();
            if (PdhOpenQueryW(null, 0, out g._query) != 0) return null;
            if (PdhAddEnglishCounterW(g._query, @"\GPU Engine(*)\Utilization Percentage", 0, out g._counter) != 0 || PdhCollectQueryData(g._query) != 0)
            {
                g.Dispose();
                return null;
            }
            return g;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    /// <summary>Percent since the last call (the first call has nothing to compare with: null).</summary>
    public double? Read()
    {
        if (_query == 0 || PdhCollectQueryData(_query) != 0) return null;
        uint size = 0, count = 0;
        if (PdhGetFormattedCounterArrayW(_counter, FmtDouble, ref size, ref count, IntPtr.Zero) != PdhMoreData) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, FmtDouble, ref size, ref count, buffer) != 0) return null;
            var items = new List<(string, double)>();
            const int ItemSize = 24;   // PDH_FMT_COUNTERVALUE_ITEM_W on x64: name pointer, status + padding, double
            for (int i = 0; i < count; i++)
            {
                var at = buffer + i * ItemSize;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(at));
                if (name is not null && Marshal.ReadInt32(at, 8) == 0) items.Add((name, BitConverter.Int64BitsToDouble(Marshal.ReadInt64(at, 16))));
            }
            return GpuLoad.Percent(items, _pid);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (_query != 0) PdhCloseQuery(_query);
        _query = 0;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint PdhOpenQueryW(string? source, nint user, out IntPtr query);
    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint PdhAddEnglishCounterW(IntPtr query, string path, nint user, out IntPtr counter);
    [LibraryImport("pdh.dll")] private static partial uint PdhCollectQueryData(IntPtr query);
    [LibraryImport("pdh.dll")] private static partial uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, ref uint count, IntPtr buffer);
    [LibraryImport("pdh.dll")] private static partial uint PdhCloseQuery(IntPtr query);
}
