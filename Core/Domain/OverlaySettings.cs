namespace GaugeOverlay.Core.Domain;

public sealed class OverlaySettings : IWindowPlace
{
    public double Left { get; set; } = double.NaN;   // NaN = default position (centred above the skill bar)
    public double Top { get; set; } = double.NaN;
    // Box width and height, each 0.3 – 2.0. A party member's window has its own (PeerDisplay).
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.9;        // plate
    /// <summary>배경 판 표시: the plate behind your boxes and party members'; off, only the boxes, gauge and names are drawn.
    /// The detached buff window never has one.</summary>
    public bool ShowPlate { get; set; } = true;
    /// <summary>The plate's colour (#RRGGBB) behind your boxes; <see cref="Opacity"/> sets how see-through.</summary>
    public string PlateColor { get; set; } = DefaultPlate;
    public const string DefaultPlate = "#0C0D10";
    public double BoxOpacity { get; set; } = 1.0;     // boxes (and their timers)
    public bool ShowTimer { get; set; } = true;
    public double TimerFont { get; set; } = 14;      // DIP; drawn capped at what fits the box (OverlayRenderer.MaxTimerFont)
    /// <summary>쿨 돈 뒤 지난 시간's number colour (#RRGGBB), outlined in white, or black for a light colour.</summary>
    public string ReadyCountColor { get; set; } = "#000000";
    public CooldownStyle CooldownStyle { get; set; } = CooldownStyle.Sweep;
    public FillFrom FillFrom { get; set; } = FillFrom.Bottom;
    /// <summary>Border drawn on a box while the game shows its gold frame on that skill (#RRGGBB), and its width in
    /// DIP, growing inward from the box edge; 0 = not drawn.</summary>
    public string GoldColor { get; set; } = "#FFD24A";
    public double GoldThickness { get; set; } = 2;

    public string AwakeningChangedColor { get; set; } = "#FF4D4D";   // red, as in the game

    /// <summary>While placing boxes: snap to the other boxes (Shift turns it off for a drag), and resize a set of boxes
    /// together (FreeLayout.Linked).</summary>
    public bool LayoutSnap { get; set; } = true;
    public bool LayoutLinkSizes { get; set; } = true;

    /// <summary>Hide the overlay from screen capture (needed when it overlaps the skill bar; also hides it from OBS/Discord).</summary>
    public bool ExcludeFromCapture { get; set; } = true;

    /// <summary>오버레이 표시: off hides your overlay and the party windows (set also by the hotkey and the tray).</summary>
    public bool Visible { get; set; } = true;
    /// <summary>오버레이 표시 항상: on (with <see cref="Visible"/>), shown over any app, not only the game or 설정. Kept apart
    /// from <see cref="Visible"/> so the hotkey's on/off comes back to it, and older versions read Visible as before.</summary>
    public bool Always { get; set; }
    /// <summary>스킬창이 안 보이면 숨기기: with the game in front, hide while the skill bar isn't seen (cutscene, loading).</summary>
    public bool HideWithoutHud { get; set; }

    public OverlayShow Mode() => !Visible ? OverlayShow.끄기 : Always ? OverlayShow.항상 : OverlayShow.켜기;

    public void SetMode(OverlayShow mode)
    {
        Visible = mode != OverlayShow.끄기;
        if (Visible) Always = mode == OverlayShow.항상;   // 끄기 keeps the on-mode for the hotkey's way back
    }
}
