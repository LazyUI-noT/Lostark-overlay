using System.Net;
using System.Security.Cryptography;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Net;

/// <summary>One party session's logic, star topology (PLAN §5 Networking, §7 item 8): who is in it, the handshake, and
/// what to send when. No sockets, threads or clock: the caller makes one call at a time with the time, and the session
/// sends through the delegate it was made with. The host relays every member's State (which carries the 아덴 게이지) to
/// the others, re-encoded on its own clock. A member sends only when its snapshot changes (at most 20 Hz), plus the
/// latest State with each 2 s heartbeat so a lost packet is repaired.
/// <para>Joining: Hello under the group key → Welcome with the host's public key (checked against the token's pin) and a
/// cookie that goes into the pair key → the joiner's first State or Heartbeat under its pair key, from the Hello's
/// endpoint, confirms it. A member's packets are accepted only from that endpoint for the rest of the session. Until then the
/// host sends exactly one reply per Hello and nothing else (no amplification), doesn't show the joiner, and doesn't
/// count it toward <see cref="Membership.MaxMembers"/>. Who gets in (참가 요청 승인, 신뢰한 기기, 내보내기): PartySession.Admission.cs.</para></summary>
public sealed partial class PartySession : IDisposable
{
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(2), DropAfter = TimeSpan.FromSeconds(6),
        HelloEvery = TimeSpan.FromMilliseconds(500), JoinTimeout = TimeSpan.FromSeconds(3), MinSendGap = TimeSpan.FromMilliseconds(50),
        LookEvery = TimeSpan.FromSeconds(1), ApprovalWait = TimeSpan.FromSeconds(30), RejoinFor = TimeSpan.FromSeconds(60);

    /// <summary>Joins in progress the host keeps, in all and per source address (two people behind one router can join
    /// at once). Further Hellos are ignored, so a token holder flooding them can't keep others out.</summary>
    const int MaxPending = 8, MaxPendingPerAddress = 2;

    sealed class Member(ulong id, string name, IPEndPoint? endpoint, PacketKey? key, TimeSpan now, bool confirmed)
    {
        public readonly ulong Id = id;
        public string Name = name;
        public readonly IPEndPoint? Endpoint = endpoint;
        /// <summary>The pair key with this member: on the host, every other member's; on a peer, only the host's.</summary>
        public readonly PacketKey? Key = key;
        /// <summary>Host only: the cookie in this member's Welcome, resent as is if the Welcome is lost.</summary>
        public byte[] Cookie = [];
        public TimeSpan LastHeard = now, RttHalf, LastRelayed = TimeSpan.MinValue;   // MinValue = never; subtracting it overflows
        public Snapshot? State;
        public bool Confirmed = confirmed, RelayPending;
        /// <summary>The member's own box look (null until they arrive) and their version.</summary>
        public Look? Look;
        public byte LookVersion;
        /// <summary>Whose look (origin ids) were sent to this member and not yet acknowledged; resent until they are.</summary>
        public readonly HashSet<ulong> LookOwed = [];
        public TimeSpan LastLookIn = TimeSpan.MinValue;   // host only: when this member's last update was taken
        /// <summary>Host only: the member's device (fingerprint) and, while joining, whether it may come in.</summary>
        public string Device = "";
        public Approval Ask;
        public TimeSpan AskedAt;
    }

    readonly SessionCrypto _crypto;
    readonly Action<byte[], IPEndPoint> _send;
    readonly bool _isHost;
    readonly Version _app;   // this app's version: sent in a Hello (peer), required of joiners (host)
    readonly IPEndPoint? _hostEndpoint;   // peer only
    readonly byte[] _hostPin = [];        // peer only
    readonly DeviceIdentity? _device;     // peer only: signs the Hello (not owned)
    readonly bool _rejoin;                // peer only: started after losing the host (RejoinFor, "다시 연결 중…")
    TimeSpan? _pendingSince;              // peer only: the host is deciding (first Pending)
    TimeSpan _lastNow;                    // the time of the last Tick or Receive, for calls made without one (Decline)
    readonly string? _token;              // host only
    readonly List<Member> _members = [];   // everyone in the session, self included, in join order
    readonly HashSet<ulong> _admitted = [];   // host only: every id admitted, departed and unconfirmed too
    bool _welcomed, _hadPeer;
    ulong _hostId;
    TimeSpan _rttHalf, _joinStarted, _lastHello, _lastHeartbeat, _lastLookSent, _lastStateSent = TimeSpan.MinValue;

    Snapshot? _mine;
    string _myName;
    IReadOnlySet<SlotGroup> _sendGroups = new HashSet<SlotGroup>();
    bool _sendGauge, _stateDirty;

    bool _peersDirty = true, _statusDirty = true;
    string? _notice;

    PartySession(SessionCrypto crypto, string name, TimeSpan now, Action<byte[], IPEndPoint> send, Version app)
    {
        (_crypto, _myName, _send, _app) = (crypto, name, send, app);
        _members.Add(new Member(crypto.SenderId, name, null, null, now, confirmed: true));
        _admitted.Add(crypto.SenderId);
        (_lastHeartbeat, _joinStarted) = (now, now);
    }

    PartySession(SessionCrypto crypto, string name, TimeSpan now, Action<byte[], IPEndPoint> send, Version app, IPEndPoint publicEndpoint)
        : this(crypto, name, now, send, app)
    {
        _isHost = true;
        _token = SessionCrypto.EncodeToken(publicEndpoint, crypto.Secret, SessionCrypto.Pin(crypto.PublicKey));
    }

    PartySession(SessionCrypto crypto, string name, TimeSpan now, Action<byte[], IPEndPoint> send, Version app, IPEndPoint host, byte[] hostPin,
        DeviceIdentity device, bool rejoin)
        : this(crypto, name, now, send, app)
    {
        (_hostEndpoint, _hostPin, _device, _rejoin) = (host, hostPin, device, rejoin);
    }

    /// <summary>Starts a new session with a fresh secret; <see cref="Token"/> carries <paramref name="publicEndpoint"/> (IPv4).
    /// Only joiners on app version <paramref name="app"/> are let in; with <paramref name="approveJoins"/>, a device not in
    /// <paramref name="trusted"/> (fingerprints) waits for <see cref="Approve"/>.</summary>
    public static PartySession Host(IPEndPoint publicEndpoint, string name, TimeSpan now, Action<byte[], IPEndPoint> send, Version app,
        bool approveJoins = false, IEnumerable<string>? trusted = null)
    {
        var s = new PartySession(SessionCrypto.CreateNew(), name, now, send, app, publicEndpoint);
        s.SetAdmission(approveJoins, trusted ?? []);
        return s;
    }

    /// <summary>Joins the session in <paramref name="token"/> (sends the first Hello, with app version <paramref name="app"/>,
    /// signed by <paramref name="device"/>); null if the token isn't valid. A <paramref name="rejoin"/> (after losing the
    /// host) keeps trying for <see cref="RejoinFor"/> instead of <see cref="JoinTimeout"/>.</summary>
    public static PartySession? Join(string token, string name, TimeSpan now, Action<byte[], IPEndPoint> send, Version app,
        DeviceIdentity device, bool rejoin = false)
    {
        if (!SessionCrypto.TryParseToken(token, out var host, out var secret, out var pin)) return null;
        SessionCrypto crypto;
        try { crypto = new SessionCrypto(secret); }
        finally { CryptographicOperations.ZeroMemory(secret); }
        var session = new PartySession(crypto, name, now, send, app, host, pin, device, rejoin);
        session.SendHello(now);
        return session;
    }

    /// <summary>This member's id in the session.</summary>
    public ulong Id => _crypto.SenderId;

    /// <summary>Host only: the token to give to peers; null otherwise and once ended. Never logged or saved.</summary>
    public string? Token => Ended ? null : _token;

    /// <summary>A peer was welcomed by the host (a host counts as welcomed).</summary>
    public bool Welcomed => _isHost || _welcomed;

    /// <summary>True for the session that hosts the party.</summary>
    public bool IsHost => _isHost;

    /// <summary>The session is over (left, refused, timed out or host lost); <see cref="TakeChanges"/> says why.</summary>
    public bool Ended { get; private set; }

    /// <summary>Peer: the session ended only because the host went silent, so joining again with the same token may work
    /// (not after the host ended it, a kick, a refusal or <see cref="Leave"/>).</summary>
    public bool CanRejoin { get; private set; }

    /// <summary>"호스트 중 (3/4)", "참가 중 (2/4)", "연결 중…", "승인 기다리는 중…", "다시 연결 중…", or null once ended.</summary>
    public string? Status
    {
        get
        {
            int n = _members.Count(m => m.Confirmed);
            return Ended ? null
                : _isHost ? $"호스트 중 ({n}/{Membership.MaxMembers})"
                : _welcomed ? $"참가 중 ({n}/{Membership.MaxMembers})"
                : _pendingSince is not null ? "승인 기다리는 중…"
                : _rejoin ? "다시 연결 중…"
                : "연결 중…";
        }
    }

    /// <summary>Other members as the overlay draws them.</summary>
    public IReadOnlyList<Player> Peers(TimeSpan now) => [.. Others().Select(m => new Player(m.Name, m.State ?? Snapshot.Empty(now), m.Look))];

    /// <summary>What changed since the last call.</summary>
    public PartyChanges TakeChanges()
    {
        var changes = new PartyChanges(_peersDirty, _statusDirty, _notice, TakeAdmission());
        (_peersDirty, _statusDirty, _notice) = (false, false, null);
        return changes;
    }

    /// <summary>The local snapshot and what of it to send; sent at most 20 Hz, and only when something changed.</summary>
    public void Publish(Snapshot mine, string name, IReadOnlySet<SlotGroup> send, bool sendGauge, TimeSpan now)
    {
        var last = _mine;
        _stateDirty |= last is null || last.AwakeningChanged != mine.AwakeningChanged || last.Gauge != mine.Gauge || last.ClassId != mine.ClassId
            || !last.Slots.SequenceEqual(mine.Slots) || !last.Groups.SetEquals(mine.Groups)
            || !last.AdenSlots.Order().SequenceEqual(mine.AdenSlots.Order())
            || !_sendGroups.SetEquals(send) || _sendGauge != sendGauge;
        (_mine, _myName, _sendGroups, _sendGauge) = (mine, name, send, sendGauge);
        if (!Ended) Flush(now);
    }

    /// <summary>Your own boxes' colours, size and layout, for members who receive them (파티원 설정 받기, 크기 받기, 배치
    /// 받기). Sent once, and again on the next call (업데이트, a class change); each receiver acknowledges, and whatever
    /// isn't acknowledged is resent every <see cref="HelloEvery"/>. The host takes a member's update at most every
    /// <see cref="LookEvery"/>.</summary>
    public void PublishLook(Look look, TimeSpan now)
    {
        if (Ended) return;
        var me = Self;
        (me.Look, me.LookVersion) = (look, (byte)(me.LookVersion + 1));
        OweLook(me, now);
    }

    /// <summary>The notice a kicked member sees.</summary>
    public const string Kicked = "호스트가 파티에서 내보냈습니다.";

    /// <summary>Host: 내보내기. Tells the member named <paramref name="name"/> and drops it; the others get the roster
    /// without it (which also ends the member's session if the notice is lost). Its device can't join again this session
    /// (until <see cref="Unblock"/>) and is no longer trusted. False when not the host or nobody else has that name.</summary>
    public bool Kick(string name)
    {
        if (Ended || !_isHost || Others().FirstOrDefault(m => m.Name == name) is not { } m) return false;
        Block(m);
        Send(m, MessageType.Refuse, Protocol.Refuse(Kicked));
        // Also the roster without it, which ends its session if the notice is lost; with both lost, its host timeout does.
        Send(m, MessageType.Roster, Protocol.Roster([.. _members.Where(x => x.Confirmed && x != m).Select(x => (x.Id, x.Name))]));
        Remove(m);
        return true;
    }

    /// <summary>Ends the session (a host's ends for everyone). Safe to call again.</summary>
    public void Leave()
    {
        if (Ended) return;
        Broadcast(MessageType.Bye, []);
        End(null);
    }

    /// <summary>Runs every 50 ms: Hello retries, heartbeats, drops, held-back relays and State sends.</summary>
    public void Tick(TimeSpan now)
    {
        if (Ended) return;
        _lastNow = now;
        if (!Welcomed)
        {
            if (_pendingSince is { } asked ? now - asked > ApprovalWait : now - _joinStarted > (_rejoin ? RejoinFor : JoinTimeout))
                End(_pendingSince is not null ? "호스트가 응답하지 않았습니다."
                    : _rejoin ? HostLost
                    : "호스트에 연결하지 못했습니다. 토큰이 맞는지, 호스트가 포트를 열었는지, 같은 버전인지 확인해 주세요.");
            else if (now - _lastHello >= HelloEvery) SendHello(now);
            return;
        }
        if (now - _lastHeartbeat >= HeartbeatEvery)
        {
            _lastHeartbeat = now;
            Broadcast(MessageType.Heartbeat, Protocol.Heartbeat(now, reply: false));
            _stateDirty = _mine is not null;   // repair a lost State
            if (_isHost)
            {
                Broadcast(MessageType.Roster, RosterBody());   // repairs a Roster lost or overtaken by the Welcome
                foreach (var m in Others()) SendRelayed(m, null, now);
            }
        }
        if (_isHost)
        {
            foreach (var m in _members.Where(m => m.Id != Id && (now - m.LastHeard > (m.Confirmed ? DropAfter : JoinTimeout)
                || m.Ask == Approval.Asking && now - m.AskedAt > ApprovalWait + JoinTimeout)).ToList())
            {
                Remove(m);
                if (Ended) return;
            }
            foreach (var m in Others().Where(m => m.RelayPending).ToList()) Relay(m, now);
        }
        else if (now - HostMember.LastHeard > DropAfter)
        {
            End(HostLost);
            CanRejoin = true;
            return;
        }
        if (now - _lastLookSent >= HelloEvery) ResendLook(now);
        Flush(now);
    }

    // ── Receiving ────────────────────────────────────────────────────────────────────

    public void Receive(byte[] data, IPEndPoint from, TimeSpan now)
    {
        if (Ended || !SessionCrypto.TryPeek(data, out var type, out var sender)) return;
        _lastNow = now;
        if (_isHost) OnHostPacket((MessageType)type, sender, data, from, now);
        else OnPeerPacket((MessageType)type, sender, data, from, now);
    }

    void OnHostPacket(MessageType type, ulong sender, byte[] data, IPEndPoint from, TimeSpan now)
    {
        var m = _members.Find(x => x.Id == sender);
        if (type == MessageType.Hello)
        {
            if (_crypto.Group.TryOpen(data, out _, out _, out var helloBody) && Protocol.TryReadHello(helloBody, out var hello))
                OnHello(m, sender, hello, from, now);
            return;
        }
        // Only this member's pair key opens its packets: another token holder can't speak for it. And only from the
        // address it joined from: a member spoofing its source can't send the host's traffic to someone else. The cost
        // is that a NAT that changes the member's port mid-session drops it (after DropAfter), and it joins again.
        if (m?.Key is null || !from.Equals(m.Endpoint) || !m.Key.TryOpen(data, out _, out _, out var body)) return;
        if (!m.Confirmed)
        {
            if (type == MessageType.Bye) { Remove(m); return; }
            if (type is not (MessageType.State or MessageType.Heartbeat) || !Confirm(m, now)) return;
        }
        m.LastHeard = now;
        switch (type)
        {
            case MessageType.State when Protocol.TryReadState(body, now, m.RttHalf, out _, out var snap):
                (m.State, m.RelayPending) = (snap, true);
                Relay(m, now);
                _peersDirty = true;
                break;
            case MessageType.Heartbeat when Protocol.TryReadHeartbeat(body, out var stamp, out bool reply):
                // Only a request is answered: answering a reply whose stamp is too old to use (a stall over 5 s) would
                // bounce it between the two for good.
                if (!reply) Send(m, MessageType.Heartbeat, Protocol.Heartbeat(stamp, reply: true));
                else if (PlausibleStamp(now, stamp)) m.RttHalf = (now - stamp) / 2;
                break;
            case MessageType.Look when Protocol.TryReadLook(body, out var origin, out var version, out var look) && origin == m.Id:
                if (Newer(m, version))
                {
                    // Held back (no ack, so the member resends it) until LookEvery has passed: no relay flood.
                    if (m.LastLookIn != TimeSpan.MinValue && now - m.LastLookIn < LookEvery) break;
                    (m.Look, m.LookVersion, m.LastLookIn) = (look, version, now);
                    _peersDirty = true;
                    OweLook(m, now);
                }
                Send(m, MessageType.LookAck, Protocol.LookAck(origin, version));   // a repeat too: its ack was lost
                break;
            case MessageType.LookAck when Protocol.TryReadLookAck(body, out var origin, out var version):
                Acknowledged(m, origin, version);
                break;
            case MessageType.Bye:
                Remove(m);
                break;
        }
    }

    void OnPeerPacket(MessageType type, ulong sender, byte[] data, IPEndPoint from, TimeSpan now)
    {
        if (!_welcomed)
        {
            // Every token holder has the group key, so the handshake is taken only from the token's host endpoint:
            // another member can't end this join with a Refuse of its own (the Welcome is also pinned to the host key).
            if (!from.Equals(_hostEndpoint) || !_crypto.Group.TryOpen(data, out _, out _, out var handshake)) return;
            if (type == MessageType.Welcome) OnWelcome(sender, handshake, now);
            else if (type == MessageType.Refuse) End(Protocol.ReadRefuse(handshake));
            else if (type == MessageType.Pending && _pendingSince is null) (_pendingSince, _statusDirty) = (now, true);
            return;
        }
        var host = HostMember;
        if (sender != _hostId || !host.Key!.TryOpen(data, out _, out _, out var body)) return;
        host.LastHeard = now;
        switch (type)
        {
            case MessageType.Roster when Protocol.TryReadRoster(body, out var roster):
                var old = _members.ToDictionary(m => m.Id);
                _members.Clear();
                foreach (var (id, name) in roster)
                {
                    var m = old.GetValueOrDefault(id) ?? new Member(id, name, null, null, now, confirmed: true);
                    m.Name = name;
                    _members.Add(m);
                }
                if (!_members.Any(m => m.Id == Id) || !_members.Contains(host)) { End("파티에서 빠졌습니다."); return; }
                _statusDirty = _peersDirty = true;
                break;
            case MessageType.State when Protocol.TryReadState(body, now, _rttHalf, out var origin, out var snap):
                if (origin != Id && _members.Find(m => m.Id == origin) is { } sm) { sm.State = snap; _peersDirty = true; }
                break;
            case MessageType.Heartbeat when Protocol.TryReadHeartbeat(body, out var stamp, out bool reply):
                if (!reply) Broadcast(MessageType.Heartbeat, Protocol.Heartbeat(stamp, reply: true));   // as the host's
                else if (PlausibleStamp(now, stamp)) _rttHalf = (now - stamp) / 2;
                break;
            case MessageType.Look when Protocol.TryReadLook(body, out var origin, out var version, out var look):
                // An origin not in the roster yet (its Roster lost) isn't acknowledged, so the host sends it again.
                if (origin == Id || _members.Find(m => m.Id == origin) is not { } about) break;
                if (Newer(about, version)) { (about.Look, about.LookVersion) = (look, version); _peersDirty = true; }
                Send(host, MessageType.LookAck, Protocol.LookAck(origin, version));
                break;
            case MessageType.LookAck when Protocol.TryReadLookAck(body, out var origin, out var version):
                Acknowledged(host, origin, version);
                break;
            case MessageType.Refuse:   // the party filled up while we were joining
                End(Protocol.ReadRefuse(body));
                break;
            case MessageType.Bye:
                End("호스트가 파티를 끝냈습니다.");
                break;
        }
    }

    /// <summary>Peer: the pin proves the key is the token's host's, so only that host can welcome us.</summary>
    void OnWelcome(ulong sender, byte[] body, TimeSpan now)
    {
        if (!Protocol.TryReadWelcome(body, out var hostKey, out var cookie)
            || !SessionCrypto.Pin(hostKey).AsSpan().SequenceEqual(_hostPin) || _crypto.Pair(hostKey, cookie) is not { } pair) return;
        (_welcomed, _hostId) = (true, sender);
        _members.Add(new Member(sender, "", _hostEndpoint, pair, now, confirmed: true));
        _statusDirty = true;
        Broadcast(MessageType.Heartbeat, Protocol.Heartbeat(now, reply: false));   // confirms us at the host
        Flush(now, force: true);
        if (Self.Look is not null) OweLook(Self, now);   // published before the host answered
    }

    Member HostMember => _members.First(m => m.Id == _hostId);
    Member Self => _members.First(m => m.Id == Id);

    /// <summary>Host: drops a member (and its pair key, so nothing more is accepted from it). <paramref name="replaced"/>:
    /// the same device is coming back in its place, so an empty party doesn't end.</summary>
    void Remove(Member m, bool replaced = false)
    {
        _members.Remove(m);
        m.Key?.Dispose();
        if (m.Ask == Approval.Asking) _withdrawn.Add(m.Id);
        if (!m.Confirmed) return;   // nobody was told about it
        _statusDirty = _peersDirty = true;
        if (!replaced && _hadPeer && _members.Count(x => x.Confirmed) == 1) { End("파티원이 모두 나가서 파티를 끝냈습니다."); return; }
        Broadcast(MessageType.Roster, RosterBody());
    }

    const string HostLost = "호스트와 연결이 끊겼습니다.";

    void End(string? notice)
    {
        _notice ??= notice;
        _statusDirty = _peersDirty = true;
        Dispose();
    }

    /// <summary>Ends without telling anyone, zeroes the secret and drops every key. <see cref="Leave"/> tells the others.</summary>
    public void Dispose()
    {
        if (Ended) return;
        Ended = true;
        foreach (var m in _members) m.Key?.Dispose();
        _members.Clear();
        _crypto.Dispose();
    }

    // ── Sending ──────────────────────────────────────────────────────────────────────

    /// <summary>Confirmed members other than self.</summary>
    IEnumerable<Member> Others() => _members.Where(m => m.Confirmed && m.Id != Id);

    /// <summary>To the host (peer, once welcomed) or every other confirmed member (host), each under its pair key.</summary>
    void Broadcast(MessageType type, byte[] body)
    {
        foreach (var m in _isHost ? Others() : _members.Where(m => m.Id == _hostId && _welcomed)) Send(m, type, body);
    }

    /// <summary>Under <paramref name="key"/>, or the pair key with <paramref name="to"/>.</summary>
    void Send(Member to, MessageType type, byte[] body, PacketKey? key = null)
    {
        if (to.Endpoint is { } ep && (key ?? to.Key) is { } k) _send(_crypto.Seal(k, (byte)type, body), ep);
    }

    void SendHello(TimeSpan now)
    {
        _lastHello = now;
        _send(_crypto.Seal(_crypto.Group, (byte)MessageType.Hello, Protocol.Hello(_myName, _crypto.PublicKey, _app, _device!, _hostPin, Id)), _hostEndpoint!);
    }

    /// <summary>Host: relays a member's new State to the others, at most every <see cref="MinSendGap"/> per member, so a
    /// member sending faster than its own limit isn't amplified; one held back goes out on a later tick.</summary>
    void Relay(Member m, TimeSpan now)
    {
        if (m.LastRelayed != TimeSpan.MinValue && now - m.LastRelayed < MinSendGap) return;
        SendRelayed(m, null, now);
        (m.RelayPending, m.LastRelayed) = (false, now);
    }

    /// <summary>Host: member <paramref name="about"/>'s last State, re-encoded on the host clock, to one member or all others.</summary>
    void SendRelayed(Member about, Member? to, TimeSpan now)
    {
        if (about.State is not { } s) return;
        var body = Protocol.State(about.Id, s, s.Groups, sendGauge: true, now);   // gauge is already null if not sent
        foreach (var t in to is null ? Others().Where(o => o != about).ToList() : [to]) Send(t, MessageType.State, body);
    }

    void Flush(TimeSpan now, bool force = false)
    {
        if (_mine is null || !_stateDirty || !Welcomed) return;
        if (!force && _lastStateSent != TimeSpan.MinValue && now - _lastStateSent < MinSendGap) return;
        Broadcast(MessageType.State, Protocol.State(Id, _mine, _sendGroups, _sendGauge, now));
        (_stateDirty, _lastStateSent) = (false, now);
    }

    /// <summary>Sends <paramref name="about"/>'s look to everyone who should have them (the host, from a peer; the
    /// other members, at the host) and keeps them owed until acknowledged.</summary>
    void OweLook(Member about, TimeSpan now)
    {
        foreach (var to in (_isHost ? Others() : _members.Where(m => m.Id == _hostId && _welcomed)).Where(t => t != about))
        {
            to.LookOwed.Add(about.Id);
            SendLook(to, about);
        }
        _lastLookSent = now;
    }

    /// <summary>Resends every colour set not yet acknowledged; one whose member has left is dropped.</summary>
    void ResendLook(TimeSpan now)
    {
        _lastLookSent = now;
        foreach (var to in _members.Where(m => m.Id != Id && m.Confirmed))
            foreach (var origin in to.LookOwed.ToList())
            {
                if (_members.Find(m => m.Id == origin) is { Look: not null } about) SendLook(to, about);
                else to.LookOwed.Remove(origin);
            }
    }

    void SendLook(Member to, Member about) =>
        Send(to, MessageType.Look, Protocol.Look(about.Id, about.LookVersion, about.Look!));

    void Acknowledged(Member by, ulong origin, byte version)
    {
        if (_members.Find(m => m.Id == origin) is { } about && about.LookVersion == version) by.LookOwed.Remove(origin);
    }

    /// <summary>A version after the one held (wrapping at 255), or the first.</summary>
    static bool Newer(Member m, byte version) => m.Look is null || (sbyte)(version - m.LookVersion) > 0;

    /// <summary>The stamp is raw wire data from a member: an echoed stamp is at most a few seconds old, and anything else
    /// (future, ancient, near TimeSpan.MinValue) would overflow the subtraction or skew every cooldown of that member.</summary>
    static bool PlausibleStamp(TimeSpan now, TimeSpan stamp) => stamp <= now && stamp > now - TimeSpan.FromSeconds(5);

    byte[] RosterBody() => Protocol.Roster([.. _members.Where(m => m.Confirmed).Select(m => (m.Id, m.Name))]);

    /// <summary>The name, numbered if someone else has it; a member on <paramref name="device"/> (about to be replaced by
    /// this join) doesn't count.</summary>
    string UniqueName(string name, string device)
    {
        name = string.IsNullOrWhiteSpace(name) ? $"파티원{_members.Count}" : name.Trim();
        var unique = name;
        for (int i = 2; _members.Any(m => m.Name == unique && (device.Length == 0 || m.Device != device)); i++) unique = $"{name}{i}";
        return unique;
    }
}
