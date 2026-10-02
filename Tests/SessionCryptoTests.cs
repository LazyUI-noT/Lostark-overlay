using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.Tests;

public class SessionCryptoTests
{
    static readonly byte[] Secret = RandomNumberGenerator.GetBytes(SessionCrypto.SecretSize);
    static readonly byte[] Message = "각성기 ready"u8.ToArray();

    [Fact]
    public void RoundTrip()
    {
        using var a = new SessionCrypto(Secret);
        using var b = new SessionCrypto(Secret);
        var packet = a.Seal(a.Group, (byte)MessageType.State, Message);
        Assert.Equal(SessionCrypto.Overhead + Message.Length, packet.Length);
        Assert.True(b.Group.TryOpen(packet, out var type, out var sender, out var body));
        Assert.Equal((byte)MessageType.State, type);
        Assert.Equal(a.SenderId, sender);
        Assert.Equal(Message, body);
    }

    [Fact]
    public void EveryTamperedByteIsRejected()
    {
        using var a = new SessionCrypto(Secret);
        var packet = a.Seal(a.Group, (byte)MessageType.State, Message);
        for (int i = 0; i < packet.Length; i++)
        {
            using var b = new SessionCrypto(Secret);
            var bad = (byte[])packet.Clone();
            bad[i] ^= 0x01;
            Assert.False(b.Group.TryOpen(bad, out _, out _, out _), $"byte {i}");
        }
    }

    [Fact]
    public void ReplayedOrOlderCounterIsRejected()
    {
        using var a = new SessionCrypto(Secret);
        using var b = new SessionCrypto(Secret);
        var first = a.Seal(a.Group, 1, Message);
        var second = a.Seal(a.Group, 1, Message);
        Assert.True(b.Group.TryOpen(second, out _, out _, out _));
        Assert.False(b.Group.TryOpen(second, out _, out _, out _));
        Assert.False(b.Group.TryOpen(first, out _, out _, out _));
        Assert.True(b.Group.TryOpen(a.Seal(a.Group, 1, Message), out _, out _, out _));
    }

    /// <summary>Real messages are a few hundred bytes: anything over MaxPacket is dropped before any decryption.</summary>
    [Fact]
    public void OversizedPacketIsRejectedUnopened()
    {
        using var a = new SessionCrypto(Secret);
        using var b = new SessionCrypto(Secret);
        var big = a.Seal(a.Group, 1, new byte[PacketKey.MaxPacket]);
        Assert.False(SessionCrypto.TryPeek(big, out _, out _));
        Assert.False(b.Group.TryOpen(big, out _, out _, out _));
        Assert.True(b.Group.TryOpen(a.Seal(a.Group, 1, new byte[PacketKey.MaxPacket - SessionCrypto.Overhead]), out _, out _, out _));
    }

    /// <summary>A token holder can seal under the group key with any sender id; the replay record keeps the most recent
    /// senders only, so made-up ids can't grow it without bound, and a recent sender's replay is still refused.</summary>
    [Fact]
    public void GroupReplayRecordIsBounded()
    {
        using var a = new SessionCrypto(Secret);
        using var b = new SessionCrypto(Secret);
        for (ulong id = 1; id <= 5000; id++) Assert.True(b.Group.TryOpen(a.Group.Seal(id, 1, 1, Message), out _, out _, out _));
        Assert.True(b.Group.Senders <= PacketKey.MaxSenders);
        Assert.False(b.Group.TryOpen(a.Group.Seal(5000, 1, 1, Message), out _, out _, out _));
    }

    [Fact]
    public void WrongKeyIsRejected()
    {
        using var a = new SessionCrypto(Secret);
        using var other = SessionCrypto.CreateNew();
        Assert.False(other.Group.TryOpen(a.Seal(a.Group, 1, Message), out _, out _, out _));
        Assert.False(other.Group.TryOpen(new byte[10], out _, out _, out _));
    }

    static readonly byte[] Cookie = [.. Enumerable.Range(1, SessionCrypto.CookieSize).Select(i => (byte)i)];

    /// <summary>The host and a member derive the same pair key; a third token holder can derive neither.</summary>
    [Fact]
    public void Only_the_two_ends_share_a_pair_key()
    {
        using var host = new SessionCrypto(Secret);
        using var member = new SessionCrypto(Secret);
        using var mallory = new SessionCrypto(Secret);
        using var atHost = host.Pair(member.PublicKey, Cookie)!;
        using var atMember = member.Pair(host.PublicKey, Cookie)!;
        using var malloryWithHost = mallory.Pair(host.PublicKey, Cookie)!;

        Assert.True(atHost.TryOpen(member.Seal(atMember, 1, Message), out _, out _, out var body));
        Assert.Equal(Message, body);
        Assert.True(atMember.TryOpen(host.Seal(atHost, 1, Message), out _, out _, out _));
        Assert.False(atHost.TryOpen(mallory.Seal(malloryWithHost, 1, Message), out _, out _, out _));
        Assert.False(atHost.TryOpen(mallory.Seal(mallory.Group, 1, Message), out _, out _, out _));
    }

    /// <summary>The Welcome's cookie is part of the pair key: without it, the right key pair isn't enough.</summary>
    [Fact]
    public void The_pair_key_depends_on_the_cookie()
    {
        using var host = new SessionCrypto(Secret);
        using var member = new SessionCrypto(Secret);
        using var atHost = host.Pair(member.PublicKey, Cookie)!;
        using var guessed = member.Pair(host.PublicKey, new byte[SessionCrypto.CookieSize])!;
        Assert.False(atHost.TryOpen(member.Seal(guessed, 1, Message), out _, out _, out _));
    }

    [Fact]
    public void A_public_key_that_is_not_a_curve_point_gives_no_pair_key()
    {
        using var host = new SessionCrypto(Secret);
        Assert.Null(host.Pair(new byte[SessionCrypto.PublicKeySize], Cookie));
        Assert.Null(host.Pair(host.PublicKey.AsSpan(1), Cookie));
        Assert.Null(host.Pair([], Cookie));
    }

    [Fact]
    public void TokenRoundTrip()
    {
        using var crypto = new SessionCrypto(Secret);
        var host = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 47800);
        var pin = SessionCrypto.Pin(crypto.PublicKey);
        var token = SessionCrypto.EncodeToken(host, Secret, pin);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.True(SessionCrypto.TryParseToken(" " + token + "\n", out var parsed, out var secret, out var parsedPin));
        Assert.Equal(host, parsed);
        Assert.Equal(Secret, secret);
        Assert.Equal(pin, parsedPin);

        Assert.False(SessionCrypto.TryParseToken("not a token!", out _, out _, out _));
        Assert.False(SessionCrypto.TryParseToken(token[..^4], out _, out _, out _));
    }

    /// <summary>The socket is IPv4 only, so an IPv6 host could never be reached.</summary>
    [Fact]
    public void An_IPv6_host_is_not_a_token()
    {
        Assert.Throws<ArgumentException>(() => SessionCrypto.EncodeToken(new IPEndPoint(IPAddress.IPv6Loopback, 47800), Secret, new byte[SessionCrypto.PinSize]));
        var raw = new byte[1 + 16 + 2 + SessionCrypto.SecretSize + SessionCrypto.PinSize];
        raw[0] = 16;
        raw[18] = 1;   // port 1
        Assert.False(SessionCrypto.TryParseToken(Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_'), out _, out _, out _));
    }

    [Fact]
    public void NonceNeverRepeatsFor100kPackets()
    {
        using var a = new SessionCrypto(Secret);
        var seen = new HashSet<UInt128>();
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> wide = stackalloc byte[16];
        for (int i = 0; i < 100_000; i++)
        {
            var p = a.Seal(a.Group, 1, []);
            SessionCrypto.Nonce(BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(1)), BinaryPrimitives.ReadUInt64BigEndian(p.AsSpan(9)), nonce);
            wide.Clear();
            nonce.CopyTo(wide);
            Assert.True(seen.Add(BinaryPrimitives.ReadUInt128LittleEndian(wide)));
        }
    }

    [Fact]
    public void DisposeZeroesTheSecret()
    {
        var a = new SessionCrypto(Secret);
        a.Dispose();
        Assert.All(a.Secret.ToArray(), b => Assert.Equal(0, b));
    }
}
