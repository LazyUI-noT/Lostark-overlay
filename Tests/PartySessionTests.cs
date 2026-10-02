using System.Net;
using System.Security.Cryptography;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.Tests;

/// <summary>The party session over an in-memory network (instant, in-order delivery), including what another token
/// holder running modified code can and can't do.</summary>
public sealed class PartySessionTests : IDisposable
{
    static readonly IReadOnlySet<SlotGroup> All = Enum.GetValues<SlotGroup>().ToHashSet();
    static readonly TimeSpan Step = TimeSpan.FromMilliseconds(50);
    static readonly IPEndPoint HostAt = At(1);

    TimeSpan _now = TimeSpan.FromMinutes(1);
    readonly Queue<(byte[] Data, IPEndPoint From, IPEndPoint To)> _wire = new();
    readonly Dictionary<IPEndPoint, Action<byte[], IPEndPoint>> _listeners = [];
    readonly List<IDisposable> _owned = [];
    readonly List<PartySession> _sessions = [];
    /// <summary>Every packet delivered so far.</summary>
    readonly List<(byte[] Data, IPEndPoint From, IPEndPoint To)> _delivered = [];

    public void Dispose() { foreach (var d in _owned) d.Dispose(); }

    // ── Joining and leaving ───────────────────────────────────────────────────────────

    [Fact]
    public void Members_see_each_other_and_the_time_left()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        var t0 = _now;
        Publish(host, 10, "호스트");
        Publish(a, 15, "민수");
        Publish(b, 20, "지현");
        Wait(Step * 2);

        Assert.Equal("민수,지현", Names(host));
        Assert.Equal("지현,호스트", Names(a));
        Assert.Equal("민수,호스트", Names(b));
        Assert.Equal(("호스트 중 (3/4)", "참가 중 (3/4)"), (host.Status, a.Status));
        Assert.Equal(t0 + TimeSpan.FromSeconds(10), EndsAt(a, "호스트"));
        Assert.Equal(t0 + TimeSpan.FromSeconds(15), EndsAt(b, "민수"));   // relayed by the host
        Assert.Equal(t0 + TimeSpan.FromSeconds(20), EndsAt(host, "지현"));
    }

    [Fact]
    public void A_member_on_another_app_version_is_refused()
    {
        var host = Host();
        var old = Start(host, "민수", At(2), new Version(1, 0, 6));
        Deliver();
        Assert.True(old.Ended);
        Assert.Contains("내 버전 v1.0.6", old.TakeChanges().Notice);
        Assert.Equal("호스트 중 (1/4)", host.Status);
    }

    [Fact]
    public void A_fifth_member_is_refused()
    {
        var host = Host();
        var joined = Enumerable.Range(2, 3).Select(i => Join(host, $"p{i}", At(i))).ToList();
        var fifth = Join(host, "영희", At(9));
        Assert.All(joined, j => Assert.Equal("참가 중 (4/4)", j.Status));
        Assert.True(fifth.Ended);
        Assert.Equal(Membership.Full, fifth.TakeChanges().Notice);
    }

    /// <summary>A join in progress doesn't hold a slot, so the host checks again when it confirms.</summary>
    [Fact]
    public void The_last_slot_goes_to_whoever_confirms_first()
    {
        var host = Host();
        Join(host, "p2", At(2));
        Join(host, "p3", At(3));
        var d = Start(host, "d", At(4));   // both Hellos reach the host before either confirms
        var e = Start(host, "e", At(5));
        Deliver();

        Assert.Equal("참가 중 (4/4)", d.Status);
        Assert.True(e.Ended);
        Assert.Equal(Membership.Full, e.TakeChanges().Notice);
        Assert.Equal(("호스트 중 (4/4)", "d,p2,p3"), (host.Status, Names(host)));
    }

    [Fact]
    public void Leaving_updates_everyone_and_the_host_leaving_ends_the_party()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        b.Leave();
        Deliver();
        Assert.Equal(("민수", "호스트", "참가 중 (2/4)"), (Names(host), Names(a), a.Status));

        host.Leave();
        Deliver();
        Assert.True(a.Ended);
        Assert.Contains("끝냈", a.TakeChanges().Notice);
    }

    [Fact]
    public void A_silent_member_is_dropped_and_a_silent_host_ends_the_party()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        Silence(At(3), b);
        Wait(PartySession.DropAfter + Step * 2);
        Assert.Equal(("민수", "호스트"), (Names(host), Names(a)));

        Silence(HostAt, host);
        Wait(PartySession.DropAfter + Step * 2);
        Assert.True(a.Ended);
    }

    [Fact]
    public void A_host_that_never_answers_fails_the_join()
    {
        var host = Host();
        Silence(HostAt, host);
        var a = Start(host, "민수", At(2));
        Wait(PartySession.JoinTimeout + Step * 2);
        Assert.True(a.Ended);
        Assert.NotNull(a.TakeChanges().Notice);
    }

    // ── Another token holder ──────────────────────────────────────────────────────────

    /// <summary>It has the group key (from the token) and its own pair key, but not another member's: a forged
    /// Bye, State or Roster changes nothing, and a forged huge counter doesn't lock the member out.</summary>
    [Fact]
    public void A_token_holder_cannot_speak_for_another_member()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        var rogue = JoinRogue(host, At(66));
        var t0 = _now;
        Publish(a, 15, "민수");

        foreach (var key in new[] { rogue.Crypto.Group, rogue.Pair! })
        {
            Forge(key, a.Id, MessageType.Bye, [], HostAt);
            Forge(key, a.Id, MessageType.State, Protocol.State(a.Id, Cooling(99), All, true, _now), HostAt);
            Forge(key, host.Id, MessageType.Bye, [], At(3));
            Forge(key, host.Id, MessageType.Roster, Protocol.Roster([(host.Id, "호스트"), (b.Id, "지현")]), At(3));
        }
        Deliver();

        Assert.Equal("말로리,민수,지현", Names(host));
        Assert.Equal("말로리,민수,호스트", Names(b));
        Assert.Equal(t0 + TimeSpan.FromSeconds(15), EndsAt(b, "민수"));

        Wait(Step);
        Publish(a, 5, "민수");   // not locked out by the forged counter
        Assert.Equal(_now + TimeSpan.FromSeconds(5), EndsAt(b, "민수"));
    }

    /// <summary>The token pins the host's key, so a Welcome from anyone else (here, first) is ignored.</summary>
    [Fact]
    public void A_welcome_from_anyone_but_the_tokens_host_is_ignored()
    {
        var host = Host();
        var rogue = Rogue(host, At(66));
        var c = Start(host, "철수", At(4));
        var welcome = Protocol.Welcome(rogue.Crypto.PublicKey, new byte[SessionCrypto.CookieSize]);
        _wire.Enqueue((rogue.Crypto.Seal(rogue.Crypto.Group, (byte)MessageType.Welcome, welcome), rogue.At, At(4)));
        Deliver();

        Assert.Equal(("참가 중 (2/4)", "호스트"), (c.Status, Names(c)));
        Assert.Equal("철수", Names(host));
    }

    /// <summary>Joins in progress don't hold slots, and one address can have only two in progress.</summary>
    [Fact]
    public void A_flood_of_hellos_does_not_keep_others_out()
    {
        var host = Host();
        Join(host, "민수", At(2));
        for (int i = 0; i < 20; i++)
        {
            var crypto = TokenHolder(host);
            _wire.Enqueue((crypto.Seal(crypto.Group, (byte)MessageType.Hello, Protocol.Hello("x", crypto.PublicKey, App)), At(66, 50000 + i), HostAt));
        }
        Deliver();

        var c = Join(host, "철수", At(3));
        Assert.Equal("참가 중 (3/4)", c.Status);
        Assert.Equal("민수,철수", Names(host));
        Assert.Equal(2, _delivered.Count(p => p.From.Equals(HostAt) && p.To.Address.Equals(At(66).Address)));
    }

    /// <summary>A client sending States faster than 20 Hz isn't amplified: the host relays each member at most every
    /// 50 ms, and the latest State still arrives.</summary>
    [Fact]
    public void The_host_relays_a_member_at_most_every_50ms()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var rogue = JoinRogue(host, At(66));
        int before = StatesTo(At(2));
        var t0 = _now;
        for (int i = 1; i <= 10; i++) Send(rogue, MessageType.State, Protocol.State(rogue.Crypto.SenderId, Cooling(i), All, true, _now));
        Deliver();
        Assert.Equal(1, StatesTo(At(2)) - before);

        Wait(Step);
        Assert.Equal(2, StatesTo(At(2)) - before);
        Assert.Equal(t0 + TimeSpan.FromSeconds(10), EndsAt(a, "말로리"));
    }

    /// <summary>A member's address is the one it joined from: an authentic packet from anywhere else (a member spoofing
    /// its source) is dropped, so the host never sends its relay traffic to a third party.</summary>
    [Fact]
    public void A_member_cannot_move_its_traffic_to_another_address()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var rogue = JoinRogue(host, At(66));
        Send(rogue, MessageType.Heartbeat, Protocol.Heartbeat(_now, reply: false), from: At(77));
        Publish(a, 15, "민수");
        Wait(PartySession.HeartbeatEvery);

        Assert.DoesNotContain(_delivered, p => p.From.Equals(HostAt) && p.To.Equals(At(77)));
        Assert.Contains(_delivered, p => p.From.Equals(HostAt) && p.To.Equals(At(66)) && p.Data[0] == (byte)MessageType.State);
    }

    /// <summary>A token holder can send a Hello from a spoofed address and knows the host's public key (from its own
    /// join), but the Welcome and its cookie go to the spoofed address, so it can't derive the pair key to confirm.</summary>
    [Fact]
    public void A_hello_from_a_spoofed_address_cannot_be_confirmed_blind()
    {
        var host = Host();
        Join(host, "민수", At(2));
        var rogue = JoinRogue(host, At(66));
        Assert.True(Protocol.TryReadWelcome(rogue.Got.First(g => g.Type == MessageType.Welcome).Body, out var hostKey, out _));

        var ghost = TokenHolder(host);
        _wire.Enqueue((ghost.Seal(ghost.Group, (byte)MessageType.Hello, Protocol.Hello("유령", ghost.PublicKey, App)), At(88), HostAt));
        Deliver();
        foreach (var cookie in new[] { new byte[SessionCrypto.CookieSize], Array.Empty<byte>(), rogue.Cookie })
        {
            using var guess = ghost.Pair(hostKey, cookie)!;
            _wire.Enqueue((ghost.Seal(guess, (byte)MessageType.Heartbeat, Protocol.Heartbeat(_now, reply: false)), At(88), HostAt));
        }
        Deliver();

        Assert.Equal("말로리,민수", Names(host));
    }

    [Fact]
    public void A_replayed_hello_after_leaving_gets_no_reply_and_no_ghost()
    {
        var host = Host();
        Join(host, "민수", At(2));
        var c = Join(host, "철수", At(3));
        var hello = _delivered.First(p => p.From.Equals(At(3)) && p.Data[0] == (byte)MessageType.Hello);
        c.Leave();
        Deliver();

        int count = _delivered.Count;
        _wire.Enqueue(hello);
        Deliver();
        Assert.Equal(count + 1, _delivered.Count);   // the replay itself, and no reply
        Assert.Equal("민수", Names(host));
    }

    // ── Look: colours, box size and layout (파티원 설정 받기) ────────────────────────────

    static readonly Look Red = new(new Dictionary<SlotGroup, string> { [SlotGroup.일반스킬] = "#FF0000" }),
        Blue = new(new Dictionary<SlotGroup, string> { [SlotGroup.일반스킬] = "#0000FF" }, 1.5, 0.5,
            new Dictionary<Part, Cell> { [Part.일반1] = new(0, 0, 40, 40) });

    [Fact]
    public void A_members_colours_reach_everyone_through_the_host()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        a.PublishLook(Red, _now);
        host.PublishLook(Blue, _now);
        Wait(Step * 2);

        Assert.Equal("#FF0000", ColoursOf(b, "민수"));
        Assert.Equal("#FF0000", ColoursOf(host, "민수"));
        Assert.Equal("#0000FF", ColoursOf(a, "호스트"));
        Assert.Null(ColoursOf(a, "지현"));
    }

    /// <summary>A lost Look is sent again until acknowledged; once everyone has acknowledged, nothing more is sent.</summary>
    [Fact]
    public void Lost_colours_are_sent_again_until_acknowledged_and_then_never()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        bool lost = false;
        _lose = p => !lost && p.To.Equals(At(3)) && p.Data[0] == (byte)MessageType.Look && (lost = true);
        a.PublishLook(Red, _now);
        Deliver();
        Assert.True(lost);
        Assert.Null(ColoursOf(b, "민수"));

        Wait(PartySession.HelloEvery * 2);
        Assert.Equal("#FF0000", ColoursOf(b, "민수"));

        int count = _delivered.Count(IsColours);
        Wait(PartySession.HeartbeatEvery * 3);
        Assert.Equal(count, _delivered.Count(IsColours));
    }

    [Fact]
    public void A_late_joiner_gets_the_colours_already_sent()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        a.PublishLook(Red, _now);
        host.PublishLook(Blue, _now);
        Wait(Step * 2);

        var c = Join(host, "철수", At(4));
        Wait(Step * 2);
        Assert.Equal("#FF0000", ColoursOf(c, "민수"));
        Assert.Equal("#0000FF", ColoursOf(c, "호스트"));
    }

    /// <summary>The colours belong to the member: when someone leaves and the others move up, they stay with their owner.</summary>
    [Fact]
    public void Colours_follow_the_member_not_the_order()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        var c = Join(host, "철수", At(4));
        b.PublishLook(Red, _now);
        Wait(Step * 2);
        a.Leave();
        Wait(Step * 2);

        Assert.Equal("지현,철수", Names(host));
        Assert.Equal("#FF0000", ColoursOf(c, "지현"));
        Assert.Null(ColoursOf(c, "호스트"));
    }

    /// <summary>A second update within a second is held back (not acknowledged), so it arrives once the second is up.</summary>
    [Fact]
    public void Updates_are_accepted_at_most_once_a_second()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));
        a.PublishLook(Red, _now);
        Deliver();
        a.PublishLook(Blue, _now);
        Deliver();
        Assert.Equal("#FF0000", ColoursOf(b, "민수"));

        Wait(TimeSpan.FromSeconds(1.5));
        Assert.Equal("#0000FF", ColoursOf(b, "민수"));
    }

    [Fact]
    public void The_box_size_and_layout_travel_with_the_colours()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        host.PublishLook(Blue, _now);
        Wait(Step * 2);

        var look = a.Peers(_now).Single(p => p.Name == "호스트").Look!;
        Assert.Equal((1.5, 0.5), (look.Width, look.Height));
        Assert.Equal(new Cell(0, 0, 40, 40), look.Layout![Part.일반1]);
    }

    // ── Kicking (내보내기) ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_host_kicks_a_member_who_is_told_and_the_others_see_them_go()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        var b = Join(host, "지현", At(3));

        Assert.True(host.Kick("민수"));
        Deliver();

        Assert.True(a.Ended);
        Assert.Equal(PartySession.Kicked, a.TakeChanges().Notice);
        Assert.Equal("지현", Names(host));
        Assert.Equal("호스트", Names(b));
        Assert.Equal("호스트 중 (2/4)", host.Status);
    }

    [Fact]
    public void A_kicked_member_is_out_even_if_the_notice_is_lost()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        Join(host, "지현", At(3));
        _lose = p => p.To.Equals(At(2)) && p.Data[0] == (byte)MessageType.Refuse;

        host.Kick("민수");
        Deliver();

        Assert.True(a.Ended);   // the roster without it ends it
    }

    [Fact]
    public void A_kicked_member_can_join_again_with_the_same_token()
    {
        var host = Host();
        Join(host, "민수", At(2));
        Join(host, "지현", At(3));
        host.Kick("민수");
        Deliver();

        var again = Join(host, "민수", At(2, 40001));

        Assert.Equal("참가 중 (3/4)", again.Status);
    }

    [Fact]
    public void Only_the_host_kicks_and_only_someone_who_is_there()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        Join(host, "지현", At(3));

        Assert.False(a.Kick("지현"));
        Assert.False(host.Kick("없는사람"));
        Assert.False(host.Kick("호스트"));
        Deliver();
        Assert.Equal("민수,지현", Names(host));
    }

    // ── Hostile or late packets ───────────────────────────────────────────────────────

    /// <summary>A token holder can seal a Refuse under the group key; before the Welcome only the host's endpoint (from
    /// the token) may end the join.</summary>
    [Fact]
    public void A_refusal_from_anyone_but_the_host_does_not_end_a_join()
    {
        var host = Host();
        var rogue = Rogue(host, At(66));
        var a = Start(host, "민수", At(2));
        _wire.Enqueue((rogue.Crypto.Seal(rogue.Crypto.Group, (byte)MessageType.Refuse, Protocol.Refuse(Membership.Full)), rogue.At, At(2)));
        Deliver();

        Assert.False(a.Ended);
        Assert.Equal("참가 중 (2/4)", a.Status);
    }

    /// <summary>A heartbeat answered more than 5 s late (a stalled PC) carries a stamp too old to measure with. It is
    /// dropped: answering a reply would bounce it between the two for good, the stamp only getting older.</summary>
    [Fact]
    public void A_late_heartbeat_reply_is_dropped_not_answered()
    {
        var host = Host();
        var a = Join(host, "민수", At(2));
        Wait(PartySession.HeartbeatEvery - Step * 2);
        Publish(a, 10, "민수");                      // heard by the host just now, so it isn't dropped during the stall
        // 민수's PC stalls just before the host's heartbeat: no ticks, everything to it waits, then lands in order.
        _sessions.Remove(a);
        var held = new List<(byte[] Data, IPEndPoint From, IPEndPoint To)>();
        _lose = p => p.To.Equals(At(2)) && Keep(held, p);
        Wait(TimeSpan.FromSeconds(5.5));              // over 5 s since the heartbeat's stamp by the time it lands
        _lose = null;
        foreach (var p in held) _wire.Enqueue(p);

        for (int n = 0; n < 200 && _wire.TryDequeue(out var p); n++)
            if (_listeners.TryGetValue(p.To, out var receive)) receive(p.Data, p.From);
        Assert.Empty(_wire);   // no ping-pong left running
    }

    static bool Keep(List<(byte[] Data, IPEndPoint From, IPEndPoint To)> held, (byte[] Data, IPEndPoint From, IPEndPoint To) p)
    {
        held.Add(p);
        return true;
    }

    // ── Network and helpers ───────────────────────────────────────────────────────────

    /// <summary>Packets for which this returns true are lost.</summary>
    Func<(byte[] Data, IPEndPoint From, IPEndPoint To), bool>? _lose;

    static bool IsColours((byte[] Data, IPEndPoint From, IPEndPoint To) p) =>
        p.Data[0] is (byte)MessageType.Look or (byte)MessageType.LookAck;

    string? ColoursOf(PartySession viewer, string name) =>
        viewer.Peers(_now).Single(p => p.Name == name).Look?.Colors[SlotGroup.일반스킬];

    static IPEndPoint At(int host, int port = 40000) => new(IPAddress.Parse($"10.0.0.{host}"), port);

    Action<byte[], IPEndPoint> SendFrom(IPEndPoint from) => (data, to) => _wire.Enqueue((data, from, to));

    void Deliver()
    {
        while (_wire.TryDequeue(out var p))
        {
            if (_lose?.Invoke(p) == true) continue;
            _delivered.Add(p);
            if (_listeners.TryGetValue(p.To, out var receive)) receive(p.Data, p.From);
        }
    }

    /// <summary>Advances the clock in 50 ms ticks, running every session's timer and delivering what it sends.</summary>
    void Wait(TimeSpan span)
    {
        for (var end = _now + span; _now < end;)
        {
            _now += Step;
            foreach (var s in _sessions) s.Tick(_now);
            Deliver();
        }
    }

    static readonly Version App = new(1, 0, 7);

    PartySession Host()
    {
        var s = PartySession.Host(HostAt, "호스트", _now, SendFrom(HostAt), App);
        Listen(HostAt, s);
        return s;
    }

    /// <summary>Sends the Hello; nothing is delivered yet.</summary>
    PartySession Start(PartySession host, string name, IPEndPoint at, Version? app = null)
    {
        var s = PartySession.Join(host.Token!, name, _now, SendFrom(at), app ?? App)!;
        Listen(at, s);
        return s;
    }

    PartySession Join(PartySession host, string name, IPEndPoint at)
    {
        var s = Start(host, name, at);
        Deliver();
        return s;
    }

    void Listen(IPEndPoint at, PartySession s)
    {
        _listeners[at] = (data, from) => s.Receive(data, from, _now);
        _sessions.Add(s);
        _owned.Add(s);
    }

    /// <summary>The session stops sending and receiving, as if its PC went offline.</summary>
    void Silence(IPEndPoint at, PartySession s)
    {
        _listeners.Remove(at);
        _sessions.Remove(s);
    }

    /// <summary>A snapshot whose 일반1 cooldown ends <paramref name="seconds"/> from now.</summary>
    Snapshot Cooling(double seconds)
    {
        var s = Snapshot.Empty(_now);
        return s with { Slots = s.Slots.SetItem((int)SlotId.일반1,
            new SlotState(SlotId.일반1, SlotPhase.Cooldown, _now + TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(30), false, _now)) };
    }

    void Publish(PartySession s, double seconds, string name)
    {
        s.Publish(Cooling(seconds), name, All, sendGauge: true, _now);
        Deliver();
    }

    string Names(PartySession viewer) => string.Join(",", viewer.Peers(_now).Select(p => p.Name).Order(StringComparer.Ordinal));

    TimeSpan? EndsAt(PartySession viewer, string name) => viewer.Peers(_now).Single(p => p.Name == name).Snapshot[SlotId.일반1].EndsAt;

    int StatesTo(IPEndPoint to) => _delivered.Count(p => p.From.Equals(HostAt) && p.To.Equals(to) && p.Data[0] == (byte)MessageType.State);

    /// <summary>Someone given the token, running their own code.</summary>
    sealed class RogueClient(SessionCrypto crypto, IPEndPoint at)
    {
        public readonly SessionCrypto Crypto = crypto;
        public readonly IPEndPoint At = at;
        public PacketKey? Pair;
        public byte[] Cookie = [];
        public readonly List<(MessageType Type, byte[] Body)> Got = [];

        public void Receive(byte[] data)
        {
            byte type;
            byte[] body;
            if (Pair is not null && Pair.TryOpen(data, out type, out _, out body)) Got.Add(((MessageType)type, body));
            else if (Crypto.Group.TryOpen(data, out type, out _, out body)) Got.Add(((MessageType)type, body));
        }
    }

    SessionCrypto TokenHolder(PartySession host)
    {
        Assert.True(SessionCrypto.TryParseToken(host.Token!, out _, out var secret, out _));
        var crypto = new SessionCrypto(secret);
        CryptographicOperations.ZeroMemory(secret);
        _owned.Add(crypto);
        return crypto;
    }

    RogueClient Rogue(PartySession host, IPEndPoint at)
    {
        var r = new RogueClient(TokenHolder(host), at);
        _listeners[at] = (data, _) => r.Receive(data);
        return r;
    }

    /// <summary>Joins as "말로리" the way the real client does, and keeps its pair key.</summary>
    RogueClient JoinRogue(PartySession host, IPEndPoint at)
    {
        var r = Rogue(host, at);
        Send(r, MessageType.Hello, Protocol.Hello("말로리", r.Crypto.PublicKey, App), r.Crypto.Group);
        Deliver();
        Assert.True(Protocol.TryReadWelcome(r.Got.Single(g => g.Type == MessageType.Welcome).Body, out var hostKey, out r.Cookie));
        r.Pair = r.Crypto.Pair(hostKey, r.Cookie)!;
        _owned.Add(r.Pair);
        Send(r, MessageType.Heartbeat, Protocol.Heartbeat(_now, reply: false));
        Deliver();
        return r;
    }

    void Send(RogueClient r, MessageType type, byte[] body, PacketKey? key = null, IPEndPoint? from = null) =>
        _wire.Enqueue((r.Crypto.Seal(key ?? r.Pair!, (byte)type, body), from ?? r.At, HostAt));

    /// <summary>A packet claiming to be from <paramref name="asId"/>, with a counter far ahead of anything real.</summary>
    void Forge(PacketKey key, ulong asId, MessageType type, byte[] body, IPEndPoint to) =>
        _wire.Enqueue((key.Seal(asId, ulong.MaxValue - 1, (byte)type, body), At(66), to));
}
