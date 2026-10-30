namespace GaugeOverlay.Core.Vision;

/// <summary>Is the skill bar on screen? Loading screens and cutscenes hide the whole HUD, so most calibrated slots
/// read Unreadable. Only frames that read at least <see cref="MinSlots"/> calibrated slots vote (key presses and the
/// 4 Hz ready checks give such frames), and <see cref="Votes"/> agreeing votes in a row decide.</summary>
public sealed class HudVote
{
    public const int MinSlots = 4, Votes = 3;
    private int _hidden, _shown;

    /// <summary>Null until the first decision.</summary>
    public bool? Visible { get; private set; }

    /// <summary>Counts one frame and returns its own vote: true (bar shown), false (hidden), or null when it read
    /// fewer than <see cref="MinSlots"/> calibrated slots.</summary>
    public bool? Add(int calibrated, int unreadable)
    {
        if (calibrated < MinSlots) return null;
        bool shown = unreadable * 3 < calibrated * 2;
        if (shown) { _hidden = 0; if (++_shown >= Votes) Visible = true; }
        else { _shown = 0; if (++_hidden >= Votes) Visible = false; }
        return shown;
    }

    /// <summary>Whether a frame may save references for slots that have none: when its own vote says shown, or, while
    /// fewer than <see cref="MinSlots"/> references are <paramref name="stored"/> (a fit taken with most slots cooling),
    /// when it read most of them and every one it read is readable (one readable slot alone could be a dark cutscene
    /// frame, and a bad reference stays until the next refit). No frame can vote in that state, so without the second
    /// case the missing references would never be added and <see cref="Visible"/> would stay unknown.</summary>
    public static bool MaySave(bool? shown, int stored, int calibrated, int unreadable) =>
        shown == true || (shown is null && stored < MinSlots && calibrated * 2 > stored && unreadable == 0);
}
