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
}
