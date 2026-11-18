using System.Globalization;
using System.Text;

namespace GaugeOverlay.Core.Domain;

/// <summary>The timing side of the motion recordings (--demo --motion=, ui-motion-tokens.plan.md phase 6): frames are grabbed as
/// fast as the window can be drawn, each stamped with its time (ms from the moment the motion started), so a clip is
/// the frames held for the gaps between their stamps (an ffmpeg concat list), and a contact sheet takes the frame nearest each
/// moment asked for.</summary>
public static class MotionFrames
{
    /// <summary>How long each frame stays up: until the next one's stamp; the last one for <paramref name="tailMs"/>.</summary>
    public static double[] Durations(IReadOnlyList<double> timesMs, double tailMs) =>
        [.. timesMs.Select((t, i) => i + 1 < timesMs.Count ? timesMs[i + 1] - t : tailMs)];

    /// <summary>ffmpeg concat-demuxer text for <paramref name="files"/>, every duration <paramref name="slow"/> times longer (the
    /// motions last 100–400 ms: real time is a blink). The last file is listed again with no duration: the demuxer ignores
    /// the last duration otherwise.</summary>
    public static string ConcatList(IReadOnlyList<string> files, IReadOnlyList<double> timesMs, double slow, double tailMs)
    {
        var text = new StringBuilder();
        var durations = Durations(timesMs, tailMs);
        for (int i = 0; i < files.Count; i++)
            text.Append("file '").Append(files[i]).Append("'\nduration ").Append((durations[i] * slow / 1000).ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
        if (files.Count > 0) text.Append("file '").Append(files[^1]).Append("'\n");
        return text.ToString();
    }

    /// <summary>For each moment in <paramref name="marksMs"/>, the index of the frame whose stamp is nearest (the earlier on a tie).</summary>
    public static int[] Nearest(IReadOnlyList<double> timesMs, IReadOnlyList<double> marksMs)
    {
        if (timesMs.Count == 0) return [];
        return [.. marksMs.Select(mark =>
        {
            int best = 0;
            for (int i = 1; i < timesMs.Count; i++)
                if (Math.Abs(timesMs[i] - mark) < Math.Abs(timesMs[best] - mark)) best = i;
            return best;
        })];
    }
}
