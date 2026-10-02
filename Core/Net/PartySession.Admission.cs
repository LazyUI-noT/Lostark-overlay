using System.Net;
using System.Security.Cryptography;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Net;

/// <summary>Host: who gets in. A Hello is refused for another version or a full party, dropped if its device signature
/// doesn't hold, refused if the device was kicked this session (<see cref="Kick"/>), and otherwise welcomed at once when
/// the device is trusted or 참가 요청 승인 is off; else the host asks (<see cref="AdmissionChanges.Asked"/>) and answers
/// each Hello with a Pending until <see cref="Approve"/> or <see cref="Decline"/>. Still at most one reply per Hello
/// (plus the one Welcome or Refuse an answer sends), so a token holder can't use the host to flood anyone.</summary>
public sealed partial class PartySession
{
    /// <summary>A joining member's standing at the door.</summary>
    enum Approval { None, Asking, Approved }

    public const string Declined = "호스트가 참가를 거절했습니다.";
    public const string BlockedText = "호스트가 내보낸 기기라서 이 파티에 다시 참가할 수 없습니다.";
    public const string Replaced = "같은 기기로 다른 곳에서 다시 참가해서 이 연결은 끝났습니다.";
    /// <summary>Requests open at once: more new devices meanwhile are ignored (they time out), so throwaway keys from
    /// spoofed addresses can't bury the host in popups.</summary>
    public const int MaxAsking = 3;
    /// <summary>How long a declined device is refused without asking again.</summary>
    public static readonly TimeSpan DeclineCooldown = TimeSpan.FromSeconds(30);
    static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(1);
    const int MaxReportKeys = 256;

    bool _approveJoins;
    HashSet<string> _trusted = [];
    // Decided in this session, kept over a SetAdmission with a saved list that hasn't caught up yet.
    readonly HashSet<string> _approvedHere = [], _kickedHere = [];
    readonly Dictionary<string, string> _blocked = [];        // device → the name it was kicked under
    readonly Dictionary<string, TimeSpan> _reportedAt = [];   // joiner and reason → last notice
    readonly Dictionary<string, TimeSpan> _declinedAt = [];   // device → when it was declined
    readonly List<JoinRequest> _asked = [];
    readonly List<ulong> _withdrawn = [];
    readonly List<TrustedDevice> _newlyTrusted = [];
    readonly List<string> _untrusted = [], _refused = [];

    /// <summary>Host: 참가 요청 승인 on or off, and the devices (fingerprints) let in without asking, plus those approved in
    /// this session and minus those kicked in it.</summary>
    public void SetAdmission(bool approveJoins, IEnumerable<string> trusted) =>
        (_approveJoins, _trusted) = (approveJoins, [.. trusted.Concat(_approvedHere).Where(d => !_kickedHere.Contains(d))]);

    /// <summary>Host: lets in a joiner the host asked about, and trusts its device from then on. False if it is no longer
    /// waiting (it gave up, or was answered already).</summary>
    public bool Approve(ulong id)
    {
        if (Ended || Asking(id) is not { } m) return false;
        m.Ask = Approval.Approved;
        _approvedHere.Add(m.Device);
        _kickedHere.Remove(m.Device);
        if (_trusted.Add(m.Device)) _newlyTrusted.Add(new TrustedDevice(m.Device, m.Name));
        Welcome(m);
        return true;
    }

    /// <summary>Host: refuses this attempt; the device may ask again after <see cref="DeclineCooldown"/>. False if it is no
    /// longer waiting.</summary>
    public bool Decline(ulong id)
    {
        if (Ended || Asking(id) is not { } m) return false;
        Send(m, MessageType.Refuse, Protocol.Refuse(Declined), _crypto.Group);
        if (_declinedAt.Count >= MaxReportKeys)
            foreach (var old in _declinedAt.Where(d => _lastNow - d.Value >= DeclineCooldown).Select(d => d.Key).ToList()) _declinedAt.Remove(old);
        if (_declinedAt.Count < MaxReportKeys) _declinedAt[m.Device] = _lastNow;
        m.Ask = Approval.None;   // answered, not withdrawn
        Remove(m);
        return true;
    }

    /// <summary>Host: 다시 허용. A device kicked this session may join again.</summary>
    public void Unblock(string device) => _blocked.Remove(device);

    /// <summary>Host: devices kicked this session, with the name each was kicked under.</summary>
    public IReadOnlyList<(string Device, string Name)> Blocked => [.. _blocked.Select(b => (b.Key, b.Value))];

    Member? Asking(ulong id) => _members.Find(x => x.Id == id && x.Ask == Approval.Asking);

    /// <summary>내보내기: the device stays out for the rest of the session and is no longer trusted.</summary>
    void Block(Member m)
    {
        if (m.Device.Length == 0) return;
        _blocked[m.Device] = m.Name;
        _trusted.Remove(m.Device);
        _approvedHere.Remove(m.Device);
        _kickedHere.Add(m.Device);
        _untrusted.Add(m.Device);
    }

    AdmissionChanges TakeAdmission()
    {
        if (_asked.Count + _withdrawn.Count + _newlyTrusted.Count + _untrusted.Count + _refused.Count == 0) return AdmissionChanges.None;
        var changes = new AdmissionChanges([.. _asked], [.. _withdrawn], [.. _newlyTrusted], [.. _untrusted], [.. _refused]);
        _asked.Clear(); _withdrawn.Clear(); _newlyTrusted.Clear(); _untrusted.Clear(); _refused.Clear();
        return changes;
    }

    /// <summary>Host: one reply per Hello (Pending, Welcome or Refuse), or none.</summary>
    void OnHello(Member? m, ulong sender, HelloBody h, IPEndPoint from, TimeSpan now)
    {
        if (m is not null)
        {
            // A retry: after a lost Welcome, or while the host decides (it also keeps the request alive).
            if (m.Confirmed || !from.Equals(m.Endpoint)) return;
            m.LastHeard = now;
            Reply(m);
            return;
        }
        var pending = _members.Where(x => !x.Confirmed).ToList();
        if (pending.Count >= MaxPending || pending.Count(x => x.Endpoint!.Address.Equals(from.Address)) >= MaxPendingPerAddress) return;
        // Only the device's own key signs this sender and session key for this host: anyone else's claim to it is dropped
        // unanswered. (An older version's Hello has no signature; it is refused by its version below.)
        bool current = h.Version == Protocol.Version;
        if (current && !DeviceIdentity.Verify(h.DeviceKey, h.Signature, sender, h.SessionKey, SessionCrypto.Pin(_crypto.PublicKey))) return;
        var device = current ? DeviceIdentity.Fingerprint(h.DeviceKey) : "";
        // The device's own stale entry (a rejoin before the host noticed the drop) doesn't fill the party: Confirm replaces it.
        var confirmed = _members.Where(x => x.Confirmed && (device.Length == 0 || x.Device != device)).Select(x => x.Id).ToList();
        if ((Membership.Refusal(confirmed, sender, h.Version, _admitted) ?? Membership.AppRefusal(_app, h.App)) is { } reason)
        {
            _send(_crypto.Seal(_crypto.Group, (byte)MessageType.Refuse, Protocol.Refuse(reason)), from);
            string? why = h.Version != Protocol.Version || Membership.AppRefusal(_app, h.App) is not null ? "버전이 다릅니다"
                : confirmed.Count >= Membership.MaxMembers ? "파티가 가득 찼습니다" : null;   // a nonce clash isn't news
            if (why is not null) Report($"{from.Address}|{h.Name}|{why}", h, why, now);
            return;
        }
        if (_blocked.ContainsKey(device))
        {
            _send(_crypto.Seal(_crypto.Group, (byte)MessageType.Refuse, Protocol.Refuse(BlockedText)), from);
            Report($"{device}|blocked", h, "내보낸 기기입니다", now);
            return;
        }
        bool ask = _approveJoins && !_trusted.Contains(device);
        if (ask && _declinedAt.TryGetValue(device, out var declined) && now - declined < DeclineCooldown)
        {
            _send(_crypto.Seal(_crypto.Group, (byte)MessageType.Refuse, Protocol.Refuse(Declined)), from);
            return;
        }
        if (ask && _members.Count(x => x.Ask == Approval.Asking) >= MaxAsking) return;
        var cookie = RandomNumberGenerator.GetBytes(SessionCrypto.CookieSize);
        if (_crypto.Pair(h.SessionKey, cookie) is not { } pair) return;
        m = new Member(sender, UniqueName(h.Name, device), from, pair, now, confirmed: false) { Cookie = cookie, Device = device };
        if (ask)
        {
            (m.Ask, m.AskedAt) = (Approval.Asking, now);
            _asked.Add(new JoinRequest(sender, m.Name, h.App, device));
        }
        else m.Ask = Approval.Approved;
        _members.Add(m);
        _admitted.Add(sender);
        Reply(m);
    }

    void Reply(Member m)
    {
        if (m.Ask == Approval.Asking) Send(m, MessageType.Pending, [], _crypto.Group);
        else if (m.Ask == Approval.Approved) Welcome(m);
    }

    void Welcome(Member m) => Send(m, MessageType.Welcome, Protocol.Welcome(_crypto.PublicKey, m.Cookie), _crypto.Group);

    /// <summary>A refused join for the host's notice, at most once per <paramref name="key"/> (joiner and reason) a minute.</summary>
    void Report(string key, HelloBody h, string why, TimeSpan now)
    {
        if (_reportedAt.TryGetValue(key, out var last) && now - last < ReportEvery) return;
        if (_reportedAt.Count >= MaxReportKeys)
            foreach (var old in _reportedAt.Where(r => now - r.Value >= ReportEvery).Select(r => r.Key).ToList()) _reportedAt.Remove(old);
        if (_reportedAt.Count >= MaxReportKeys) return;   // a flood of names: the first ones were told
        _reportedAt[key] = now;
        var version = h.Version != Protocol.Version ? "이전 버전" : $"v{h.App}";
        _refused.Add($"{Clip(h.Name)} ({version}) 참가 거절: {why}");
    }

    static string Clip(string name) => string.IsNullOrWhiteSpace(name) ? "이름 없음" : name.Trim();

    /// <summary>Host: the member proved its pair key from its Hello's endpoint; it now gets the roster, heartbeats
    /// and everyone's State. The same device still listed (it dropped and came back) is replaced. False (and refused)
    /// if the party filled up while it was joining.</summary>
    bool Confirm(Member m, TimeSpan now)
    {
        // Told, so a copy of this device still running doesn't rejoin and push this one out in turn.
        if (m.Device.Length > 0 && Others().FirstOrDefault(x => x.Device == m.Device) is { } stale)
        {
            Send(stale, MessageType.Refuse, Protocol.Refuse(Replaced));
            Remove(stale, replaced: true);
        }
        if (_members.Count(x => x.Confirmed) >= Membership.MaxMembers)
        {
            Send(m, MessageType.Refuse, Protocol.Refuse(Membership.Full));
            Remove(m);
            return false;
        }
        m.Confirmed = true;
        _hadPeer = true;
        _statusDirty = _peersDirty = true;
        Broadcast(MessageType.Roster, RosterBody());
        Send(m, MessageType.Heartbeat, Protocol.Heartbeat(now, reply: false));
        foreach (var other in Others().Where(o => o != m)) SendRelayed(other, m, now);
        foreach (var other in _members.Where(o => o != m && o.Confirmed && o.Look is not null))
        {
            m.LookOwed.Add(other.Id);
            SendLook(m, other);
        }
        _stateDirty = _mine is not null;
        Flush(now, force: true);
        return true;
    }
}
