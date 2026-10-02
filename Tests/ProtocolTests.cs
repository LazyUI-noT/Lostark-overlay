using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.Tests;

public class ProtocolTests
{
    static readonly TimeSpan T0 = TimeSpan.FromSeconds(100);
    static Snapshot Sample()
    {
        var s = Snapshot.Empty(T0);
        var slots = s.Slots
            .SetItem((int)SlotId.각성기, new SlotState(SlotId.각성기, SlotPhase.Ready, null, null, false, T0))
            .SetItem((int)SlotId.일반1, new SlotState(SlotId.일반1, SlotPhase.Cooldown, T0 + TimeSpan.FromMilliseconds(7250), TimeSpan.FromSeconds(12), true, T0))
            .SetItem((int)SlotId.일반2, new SlotState(SlotId.일반2, SlotPhase.LongCooldown, null, null, false, T0, Minutes: 3))
            .SetItem((int)SlotId.아덴1, new SlotState(SlotId.아덴1, SlotPhase.Cooldown, T0 + TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), false, T0));
        return s with { Slots = slots, AwakeningChanged = true, Gauge = 0.25, AdenSlots = [SlotId.아덴1] };
    }

    [Fact]
    public void StateRoundTripRebuildsOnReceiverClockWithRttCompensation()
    {
        var all = Enum.GetValues<SlotGroup>().ToHashSet();
        var body = Protocol.State(42, Sample(), all, sendGauge: true, T0);
        var receiverNow = TimeSpan.FromSeconds(5000);   // a different PC's clock
        var rttHalf = TimeSpan.FromMilliseconds(30);

        Assert.True(Protocol.TryReadState(body, receiverNow, rttHalf, out var origin, out var s));
        Assert.Equal(42UL, origin);
        Assert.Equal([SlotId.아덴1], s.AdenSlots.ToArray());
        Assert.True(s.AwakeningChanged);
        Assert.Equal(0.25, s.Gauge!.Value, 4);   // gauge travels inside State
        Assert.Equal(all, s.Groups);

        var cd = s[SlotId.일반1];
        Assert.Equal(SlotPhase.Cooldown, cd.Phase);
        Assert.Equal(receiverNow + TimeSpan.FromMilliseconds(7250 - 30), cd.EndsAt);
        Assert.Equal(TimeSpan.FromSeconds(12), cd.Total);
        Assert.True(cd.Estimated);
        Assert.Equal(SlotPhase.LongCooldown, s[SlotId.일반2].Phase);
        Assert.Equal(3, s[SlotId.일반2].Minutes);
        Assert.Equal(SlotPhase.Ready, s[SlotId.각성기].Phase);
        Assert.Equal(receiverNow + TimeSpan.FromMilliseconds(2000 - 30), s[SlotId.아덴1].EndsAt);
    }

    /// <summary>Version 8 peers don't know Unavailable (they drop a state with an unknown phase), so it travels as a
    /// LongCooldown of 0 minutes: drawn the same, dim with no timer, on a peer row.</summary>
    [Fact]
    public void A_dark_skill_travels_as_a_dim_one_older_peers_accept()
    {
        var s = Sample();
        s = s with { Slots = s.Slots.SetItem((int)SlotId.아덴1, new SlotState(SlotId.아덴1, SlotPhase.Unavailable, null, null, false, T0)) };
        var body = Protocol.State(1, s, Enum.GetValues<SlotGroup>().ToHashSet(), true, T0);

        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var got));
        Assert.Equal((SlotPhase.LongCooldown, 0), (got[SlotId.아덴1].Phase, got[SlotId.아덴1].Minutes));
    }

    [Fact]
    public void The_class_number_travels_with_the_state()
    {
        var body = Protocol.State(1, Sample() with { ClassId = 17 }, Enum.GetValues<SlotGroup>().ToHashSet(), true, T0);

        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var got));
        Assert.Equal(17, got.ClassId);
    }

    [Fact]
    public void StackSkillWithAUseLeftStaysStackedForTheParty()
    {
        var s = Sample();
        s = s with { Slots = s.Slots.SetItem((int)SlotId.일반3,
            new SlotState(SlotId.일반3, SlotPhase.Cooldown, T0 + TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), true, T0, Stacked: true)) };
        var body = Protocol.State(1, s, Enum.GetValues<SlotGroup>().ToHashSet(), true, T0);

        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var got));
        Assert.Equal((SlotPhase.Cooldown, true, true), (got[SlotId.일반3].Phase, got[SlotId.일반3].Estimated, got[SlotId.일반3].Stacked));
        Assert.False(got[SlotId.일반1].Stacked);
    }

    [Fact]
    public void The_gold_frame_travels_with_the_state()
    {
        var s = Sample();
        s = s with { Slots = s.Slots.SetItem((int)SlotId.일반4, s[SlotId.일반4] with { Gold = true }) };
        var body = Protocol.State(1, s, Enum.GetValues<SlotGroup>().ToHashSet(), true, T0);

        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var got));
        Assert.Equal((s[SlotId.일반4].Phase, true), (got[SlotId.일반4].Phase, got[SlotId.일반4].Gold));
        Assert.False(got[SlotId.일반1].Gold);
    }

    [Fact]
    public void A_stack_skill_with_no_use_left_travels_with_the_state()
    {
        var s = Sample();
        s = s with { Slots = s.Slots.SetItem((int)SlotId.일반3,
            new SlotState(SlotId.일반3, SlotPhase.Cooldown, T0 + TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), false, T0, StackSkill: true)) };
        var body = Protocol.State(1, s, Enum.GetValues<SlotGroup>().ToHashSet(), true, T0);

        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var got));
        Assert.Equal((SlotPhase.Cooldown, false, true), (got[SlotId.일반3].Phase, got[SlotId.일반3].Stacked, got[SlotId.일반3].StackSkill));
        Assert.False(got[SlotId.일반1].StackSkill);
    }

    [Fact]
    public void StateSendsOnlyEnabledGroups()
    {
        var send = new HashSet<SlotGroup> { SlotGroup.일반스킬 };
        var body = Protocol.State(1, Sample(), send, sendGauge: false, T0);
        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var s));

        Assert.Equal([SlotGroup.일반스킬], s.Groups);
        Assert.Null(s.Gauge);                                      // sendGauge off
        Assert.False(s.AwakeningChanged);                          // travels with 각성기 only
        Assert.Equal(SlotPhase.Unknown, s[SlotId.각성기].Phase);    // not sent
        Assert.Equal(SlotPhase.Unknown, s[SlotId.아덴1].Phase);     // 아덴스킬 for this class, not sent
        Assert.Equal(SlotPhase.Cooldown, s[SlotId.일반1].Phase);
        Assert.True(body.Length < Protocol.State(1, Sample(), Enum.GetValues<SlotGroup>().ToHashSet(), true, T0).Length);
    }

    [Fact]
    public void ElapsedTimeBeforeSendingIsNotCountedTwice()
    {
        // Encoded 1 s after the snapshot: remaining is 6.25 s at that moment.
        var body = Protocol.State(1, Sample(), Enum.GetValues<SlotGroup>().ToHashSet(), true, T0 + TimeSpan.FromSeconds(1));
        Assert.True(Protocol.TryReadState(body, T0, TimeSpan.Zero, out _, out var s));
        Assert.Equal(T0 + TimeSpan.FromMilliseconds(6250), s[SlotId.일반1].EndsAt);
    }

    [Fact]
    public void MalformedStateIsRejected()
    {
        var body = Protocol.State(1, Sample(), Enum.GetValues<SlotGroup>().ToHashSet(), true, T0);
        Assert.False(Protocol.TryReadState(body[..^3], T0, TimeSpan.Zero, out _, out _));
        var badSlot = (byte[])body.Clone();
        badSlot[14] = 200;   // first slot id (after the 14-byte header)
        Assert.False(Protocol.TryReadState(badSlot, T0, TimeSpan.Zero, out _, out _));
    }

    [Fact]
    public void SmallMessagesRoundTrip()
    {
        byte[] key = [.. Enumerable.Range(1, SessionCrypto.PublicKeySize).Select(i => (byte)i)];
        byte[] pin = [.. Enumerable.Range(7, SessionCrypto.PinSize).Select(i => (byte)i)];
        using var device = DeviceIdentity.Create();
        Assert.True(Protocol.TryReadHello(Protocol.Hello(new string('민', 20), key, new Version(1, 2, 3), device, pin, 42), out var hello));
        Assert.Equal(Protocol.Version, hello.Version);
        Assert.Equal(new Version(1, 2, 3), hello.App);
        Assert.Equal(16, hello.Name.Length);
        Assert.Equal(key, hello.SessionKey);
        Assert.Equal(device.PublicKey, hello.DeviceKey);
        Assert.True(DeviceIdentity.Verify(hello.DeviceKey, hello.Signature, 42, key, pin));

        byte[] cookie = [.. Enumerable.Range(100, SessionCrypto.CookieSize).Select(i => (byte)i)];
        Assert.True(Protocol.TryReadWelcome(Protocol.Welcome(key, cookie), out var hostKey, out var readCookie));
        Assert.Equal(key, hostKey);
        Assert.Equal(cookie, readCookie);
        Assert.False(Protocol.TryReadWelcome(key, out _, out _));   // a version 5 Welcome: the key alone

        Assert.True(Protocol.TryReadRoster(Protocol.Roster([(1, "호스트"), (2, "지현")]), out var roster));
        Assert.Equal([(1UL, "호스트"), (2UL, "지현")], roster);

        Assert.True(Protocol.TryReadHeartbeat(Protocol.Heartbeat(T0, reply: true), out var stamp, out var reply));
        Assert.Equal((T0, true), (stamp, reply));

        Assert.Equal("가득", Protocol.ReadRefuse(Protocol.Refuse("가득")));
    }

    /// <summary>An older Hello (no device key, maybe no app version) still reads its version and name, so the host can
    /// refuse it by name.</summary>
    [Fact]
    public void An_older_hello_still_reads_version_and_name()
    {
        byte[] v9 = [9, 2, (byte)'m', (byte)'s', .. new byte[SessionCrypto.PublicKeySize], 1, 0, 8];
        Assert.True(Protocol.TryReadHello(v9, out var hello));
        Assert.Equal(((byte)9, "ms"), (hello.Version, hello.Name));
        Assert.Equal(new Version(1, 0, 8), hello.App);
        Assert.Empty(hello.DeviceKey);
        Assert.Empty(hello.Signature);
    }

    [Fact]
    public void A_look_round_trips_with_its_origin_version_size_and_layout()
    {
        var look = new Look(new Dictionary<SlotGroup, string> { [SlotGroup.일반스킬] = "#12AB34", [SlotGroup.각성기] = "#FF0000" },
            1.25, 0.75, new Dictionary<Part, Cell> { [Part.일반1] = new(-4, 2.5, 34, 34), [Part.게이지] = new(0, 0, 140, 6) });

        Assert.True(Protocol.TryReadLook(Protocol.Look(42, 7, look), out var origin, out var version, out var read));

        Assert.Equal((42UL, (byte)7), (origin, version));
        Assert.Equal(look.Colors, read.Colors);
        Assert.Equal((1.25, 0.75), (read.Width, read.Height));
        Assert.Equal(look.Layout, read.Layout);
        Assert.True(Protocol.TryReadLookAck(Protocol.LookAck(42, 7), out var ackOrigin, out var ackVersion));
        Assert.Equal((42UL, (byte)7), (ackOrigin, ackVersion));
    }

    [Fact]
    public void A_look_without_a_layout_reads_as_the_fixed_grid()
    {
        Assert.True(Protocol.TryReadLook(Protocol.Look(1, 1, new Look(new Dictionary<SlotGroup, string>())), out _, out _, out var read));
        Assert.Null(read.Layout);
        Assert.Equal((1.0, 1.0), (read.Width, read.Height));
    }

    [Fact]
    public void Malformed_looks_are_rejected()
    {
        var good = Protocol.Look(1, 1, new Look(new Dictionary<SlotGroup, string> { [SlotGroup.일반스킬] = "#010203" },
            1, 1, new Dictionary<Part, Cell> { [Part.일반1] = new(0, 0, 34, 34) }));
        Assert.True(Protocol.TryReadLook(good, out _, out _, out _));
        Assert.False(Protocol.TryReadLook(good[..^1], out _, out _, out _));             // short
        Assert.False(Protocol.TryReadLook([.. good, 0], out _, out _, out _));           // trailing byte
        var unknown = (byte[])good.Clone();
        unknown[10] = 200;                                                                // undefined group
        Assert.False(Protocol.TryReadLook(unknown, out _, out _, out _));
        byte[] twice = [.. good[..9], 2, .. good[10..14], .. good[10..]];                // same group twice
        Assert.False(Protocol.TryReadLook(twice, out _, out _, out _));
        var tiny = (byte[])good.Clone();
        (tiny[14], tiny[15]) = (1, 0);                                                    // width 0.001: out of range
        Assert.False(Protocol.TryReadLook(tiny, out _, out _, out _));
        var part = (byte[])good.Clone();
        part[19] = 200;                                                                   // undefined part
        Assert.False(Protocol.TryReadLook(part, out _, out _, out _));
        var flat = (byte[])good.Clone();
        (flat[24], flat[25]) = (0, 0);                                                    // a cell with no width
        Assert.False(Protocol.TryReadLook(flat, out _, out _, out _));
        var far = (byte[])good.Clone();
        (far[20], far[21]) = (0x20, 0x4E);                                                // X = 5000 DIP: would blow up the plate
        Assert.False(Protocol.TryReadLook(far, out _, out _, out _));
        var wide = (byte[])good.Clone();
        (wide[24], wide[25]) = (0x20, 0x4E);                                              // W = 5000 DIP
        Assert.False(Protocol.TryReadLook(wide, out _, out _, out _));
    }

    [Fact]
    public void FifthMemberIsRefused()
    {
        List<ulong> members = [0x1_0000_0001, 0x2_0000_0002, 0x3_0000_0003];
        Assert.Null(Membership.Refusal(members, 0x4_0000_0004, Protocol.Version));
        members.Add(0x4_0000_0004);
        Assert.Contains("가득", Membership.Refusal(members, 0x5_0000_0005, Protocol.Version));
    }

    [Fact]
    public void OtherVersionAndNoncePrefixCollisionAreRefused()
    {
        List<ulong> members = [0xAAAA_0000_1234];
        Assert.Contains("버전", Membership.Refusal(members, 7, (byte)(Protocol.Version + 1)));
        Assert.NotNull(Membership.Refusal(members, 0xBBBB_0000_1234, Protocol.Version));   // same low 4 bytes
    }

    [Fact]
    public void Another_app_version_is_refused_naming_both_and_who_updates()
    {
        Assert.Null(Membership.AppRefusal(new Version(1, 0, 8), new Version(1, 0, 8, 0)));   // a 4th part doesn't count
        var older = Membership.AppRefusal(new Version(1, 0, 8), new Version(1, 0, 7));
        Assert.Contains("호스트 v1.0.8", older);
        Assert.Contains("내 버전 v1.0.7", older);
        Assert.Contains("업데이트 확인", older);
        Assert.Contains("호스트가 업데이트", Membership.AppRefusal(new Version(1, 0, 7), new Version(1, 0, 8)));
    }

    [Fact]
    public void NoncePrefixOfAMemberWhoLeftIsStillRefused()
    {
        // The session key outlives the member; reusing the prefix would repeat AES-GCM nonces under it.
        List<ulong> current = [0xAAAA_0000_0001];
        List<ulong> everAdmitted = [0xAAAA_0000_0001, 0xCCCC_0000_1234];
        Assert.NotNull(Membership.Refusal(current, 0xDDDD_0000_1234, Protocol.Version, everAdmitted));
        Assert.Null(Membership.Refusal(current, 0xDDDD_0000_9999, Protocol.Version, everAdmitted));
    }
}
