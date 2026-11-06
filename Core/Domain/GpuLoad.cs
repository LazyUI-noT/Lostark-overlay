namespace GaugeOverlay.Core.Domain;

/// <summary>The app's GPU load from Windows' "GPU Engine" counter (instances named pid_PID_luid_..._engtype_TYPE): as Task Manager
/// does, an engine type's engines add up and the busiest type counts.</summary>
public static class GpuLoad
{
    public static double Percent(IEnumerable<(string Instance, double Value)> counters, int pid)
    {
        string prefix = $"pid_{pid}_";
        return counters.Where(c => c.Instance.StartsWith(prefix, StringComparison.Ordinal))
            .GroupBy(c => c.Instance[(c.Instance.IndexOf("engtype_", StringComparison.Ordinal) + 1)..])
            .Select(g => g.Sum(c => c.Value)).DefaultIfEmpty(0).Max();
    }
}
