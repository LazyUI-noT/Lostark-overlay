namespace GaugeOverlay.Core.Domain;

/// <summary>업데이트 확인 (설정, bottom left): whether the app looks for a new release at start, and the release it last told
/// the user about, so the notice shows once per version.</summary>
public sealed class UpdateSettings
{
    public bool Check { get; set; } = true;
    /// <summary>업데이트 채널: 안정 (stable releases) or 베타 (vX.Y.Z-beta.N pre-releases too; 안정 again takes a beta back).</summary>
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    /// <summary>Tag of the release the tray notice was shown for ("v1.0.6"); "" for none.</summary>
    public string Notified { get; set; } = "";
    /// <summary>Just installed, shown once by the new version: its tag and its notes as plain lines; "" when nothing is due.</summary>
    public string WhatsNewTag { get; set; } = "";
    public string WhatsNew { get; set; } = "";
}

/// <summary>Which releases 업데이트 확인 offers (<see cref="GaugeOverlay.Core.Data.UpdateCheck.Offer"/>).</summary>
public enum UpdateChannel { Stable, Beta }
