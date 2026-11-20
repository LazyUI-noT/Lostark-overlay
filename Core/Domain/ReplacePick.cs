namespace GaugeOverlay.Core.Domain;

/// <summary>The 새 캡처 popup's pick (preview-images.plan.md §4): one new shot and one of the 5 slots to compare. Only once both
/// are picked does the shot show the slot's number (it will become image N) and the slot get its red dashed outline.</summary>
public sealed record ReplacePick(int? Shot = null, int? Slot = null)
{
    public bool Ready => Shot is not null && Slot is not null;

    /// <summary>The number drawn large on the picked shot (1–5), or null.</summary>
    public int? NumberOnShot => Ready ? Slot + 1 : null;

    /// <summary>The slot (0–4) drawn with a red dashed outline, or null.</summary>
    public int? DashedSlot => Ready ? Slot : null;
}
