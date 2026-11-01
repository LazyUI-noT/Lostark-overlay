namespace GaugeOverlay.Core.Vision;

/// <summary>Which GPU staging texture each captured region is copied into. The regions asked for change from one capture to
/// the next (each read on its own cadence: debuff frame and timer passes, the 암수 strip with or without the boss's HP text, the
/// boss name once a second), so textures are kept by size, not by place in the list: a region reuses any free texture of its
/// size, and a new one is made only for a size not held yet. Kept by place, they were destroyed and made again many times a
/// second, which stalls the GPU the game draws on (user report, 2026-11-01: FPS drops in 0.15 that 0.14 didn't have).</summary>
public static class StagingSlots
{
    /// <summary>For each of <paramref name="wanted"/>, the index in <paramref name="pool"/> of a free texture its size; indexes from
    /// <c>pool.Count</c> on are new textures, to be added to the pool in that order.</summary>
    public static int[] Assign(IReadOnlyList<(int W, int H)> pool, IReadOnlyList<(int W, int H)> wanted)
    {
        var slots = new int[wanted.Count];
        var taken = new bool[pool.Count];
        int next = pool.Count;
        for (int i = 0; i < wanted.Count; i++)
        {
            int found = -1;
            for (int j = 0; j < pool.Count && found < 0; j++)
                if (!taken[j] && pool[j] == wanted[i]) found = j;
            if (found >= 0) (taken[found], slots[i]) = (true, found);
            else slots[i] = next++;
        }
        return slots;
    }
}
