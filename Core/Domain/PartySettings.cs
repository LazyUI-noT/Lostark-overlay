using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>A device the host let in (참가 요청 승인): its <c>DeviceIdentity.Fingerprint</c> and the name it last used.</summary>
public sealed record TrustedDevice(string Device, string Name);

public sealed class PartySettings
{
    public string DisplayName { get; set; } = "";
    public int HostPort { get; set; } = 47800;

    /// <summary>참가 요청 승인: while hosting, ask before letting in a device that isn't trusted.</summary>
    public bool ApproveJoins { get; set; } = true;
    /// <summary>신뢰한 기기: devices approved once, let in without asking from then on (removed by 내보내기 or 삭제).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<TrustedDevice> TrustedDevices { get; set; } = [];
    public Dictionary<SlotGroup, bool> Send { get; set; } = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => true);
    public bool SendGauge { get; set; } = true;

    /// <summary>A copy of the groups to send (a group missing from <see cref="Send"/> is sent, as the settings UI shows it),
    /// safe to hand to the party's network threads while the UI edits <see cref="Send"/>.</summary>
    public IReadOnlySet<SlotGroup> SendGroups() => Enum.GetValues<SlotGroup>().Where(g => Send.GetValueOrDefault(g, true)).ToHashSet();
    /// <summary>What stands left of each party member's boxes.</summary>
    public PeerLabel Label { get; set; } = PeerLabel.IconAndName;

    /// <summary>Where each party slot's window is (0/1/2 = the members numbered by <see cref="PartySlots.Numbers"/>),
    /// whoever occupies it; what it shows follows the member's class (<see cref="ClassSettings.Members"/>). Replaced on
    /// load, not added to (the default three would stay in front of the saved ones).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<PeerDisplay> ShowOthersBySlot { get; set; } = [new(), new(), new()];

    /// <summary>기본 colour for party members: the 일반스킬 colour of each role (<see cref="ColorSource.Default"/>).</summary>
    public Dictionary<Role, string> RoleColor { get; set; } = new() { [Role.딜러] = "#FF6B70", [Role.서포터] = "#E8B53A" };

    /// <summary>파티원 크기 받기 / 파티원 배치 받기: use the box size or layout a member sent, fitted inside the size you set
    /// for them (<see cref="ClassProfiles.Member"/>). Off: what they sent is kept but unused.</summary>
    public bool ReceiveSize { get; set; }
    public bool ReceiveLayout { get; set; }

    /// <summary>파티원 순서: the name pinned to each slot (null = whoever is next by join order). See
    /// <see cref="PartySlots.Assign"/>.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Replace)]
    public List<string?> SlotNames { get; set; } = [];
    /// <summary>내 번호: your number in the party, 1–4; members take the other three (<see cref="PartySlots.Numbers"/>).</summary>
    public int MyNumber { get; set; } = 1;
    /// <summary>서포터 고정: the number a supporter always takes (not while you are the supporter).</summary>
    public bool SupportFixed { get; set; }
    public int SupportNumber { get; set; } = 2;

    /// <summary>Size of the class icon beside a party member's boxes, 0.5 – 2.0.</summary>
    public double IconScale { get; set; } = 1.0;
    /// <summary>이름 길이: members' names are cut to this many units on your screen (한글 2, 영문 1; PlayerName).</summary>
    public int NameLength { get; set; } = PlayerName.MaxUnits;

    /// <summary>파티 묶기: the members stacked in one window with one plate, at <see cref="Left"/>/<see cref="Top"/> (NaN =
    /// the default spot); off, each slot has its own window (<see cref="ShowOthersBySlot"/>).</summary>
    public bool Together { get; set; }
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    /// <summary>The party windows' plate colour (#RRGGBB).</summary>
    public string PlateColor { get; set; } = OverlaySettings.DefaultPlate;
}

public enum PeerLabel { IconAndName, Name, Icon }

public enum Role { 딜러, 서포터 }


/// <summary>One party slot's window spot; what it shows follows its member's class.</summary>
public sealed class PeerDisplay
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
}
