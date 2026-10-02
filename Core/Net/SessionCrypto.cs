using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace GaugeOverlay.Core.Net;

/// <summary>One member's keys for a party session (PLAN §5 Networking).
/// Packet: <c>[1B type][8B senderId][8B counter] + ciphertext + 16B tag</c>; the 17-byte header is the AAD.
/// <para>Two kinds of key. The <see cref="Group"/> key comes from the token secret, which every member holds, so it
/// carries only the handshake (Hello, Welcome, and a Refuse before a Welcome). Everything after that uses a
/// <see cref="Pair"/> key that only the host and that one member can compute: ECDH between their session key pairs,
/// salted with the secret and bound to a random cookie the host sends in the Welcome (so a Hello from a spoofed address
/// can't be completed by someone who never sees the Welcome). The host's public key is pinned in the token (<see cref="Pin"/>), so another token holder
/// can't pose as the host either. So nobody who holds only the token can forge, replay or block another member's
/// packets.</para>
/// <para>Nonce (12 bytes) = first 4 bytes of senderId ‖ 8-byte big-endian counter. The counter starts at 1 and only
/// increments, across all of a member's keys, so one sender never repeats a nonce. senderId is random per member
/// per session, and <see cref="Membership"/> refuses a member whose 4-byte prefix equals another member's, so two
/// senders never share a nonce under one key. A new session always has a new secret and new key pairs.</para></summary>
public sealed class SessionCrypto : IDisposable
{
    public const int SecretSize = 32, PinSize = 16, PublicKeySize = 64, CookieSize = 16, HeaderSize = 17, TagSize = 16, Overhead = HeaderSize + TagSize;
    const int PointSize = 32;
    static readonly byte[] GroupInfo = "gauge-v6 group"u8.ToArray(), PairInfo = "gauge-v6 pair"u8.ToArray();

    readonly byte[] _secret;
    readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    ulong _counter;
    readonly Lock _lock = new();

    /// <summary>This member's random id for the session (packet header, nonce prefix).</summary>
    public ulong SenderId { get; }

    /// <summary>The raw secret (for the host's token). Zeroed on dispose.</summary>
    public ReadOnlySpan<byte> Secret => _secret;

    /// <summary>This member's P-256 public key for the session (X ‖ Y): sent in a Hello, or by the host in a Welcome.</summary>
    public byte[] PublicKey { get; }

    /// <summary>The handshake key every token holder shares.</summary>
    public PacketKey Group { get; }

    public SessionCrypto(ReadOnlySpan<byte> secret)
    {
        if (secret.Length != SecretSize) throw new ArgumentException("secret must be 32 bytes", nameof(secret));
        _secret = secret.ToArray();
        Group = new PacketKey(Derive(_secret, [], GroupInfo));
        var q = _ecdh.ExportParameters(false).Q;
        PublicKey = [.. q.X!, .. q.Y!];
        SenderId = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));
    }

    /// <summary>A new session with a fresh random secret.</summary>
    public static SessionCrypto CreateNew()
    {
        var s = RandomNumberGenerator.GetBytes(SecretSize);
        try { return new SessionCrypto(s); }
        finally { CryptographicOperations.ZeroMemory(s); }
    }

    /// <summary>The short hash of the host's public key that the token carries.</summary>
    public static byte[] Pin(ReadOnlySpan<byte> publicKey) => SHA256.HashData(publicKey)[..PinSize];

    /// <summary>The key shared with the member whose public key this is, for the join the host gave
    /// <paramref name="cookie"/> to (in its Welcome, so only whoever receives the Welcome can derive the key);
    /// null if the key isn't a P-256 point.</summary>
    public PacketKey? Pair(ReadOnlySpan<byte> otherPublicKey, ReadOnlySpan<byte> cookie)
    {
        if (otherPublicKey.Length != PublicKeySize) return null;
        byte[]? shared = null;
        try
        {
            using var other = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = otherPublicKey[..PointSize].ToArray(), Y = otherPublicKey[PointSize..].ToArray() },
            });
            shared = _ecdh.DeriveRawSecretAgreement(other.PublicKey);
            return new PacketKey(Derive(shared, _secret, [.. PairInfo, .. cookie]));
        }
        // Windows CNG reports a point that isn't on the curve as PlatformNotSupportedException.
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException) { return null; }
        finally { if (shared is not null) CryptographicOperations.ZeroMemory(shared); }
    }

    /// <summary>Encrypts one message under <paramref name="key"/> with this member's next counter.</summary>
    public byte[] Seal(PacketKey key, byte type, ReadOnlySpan<byte> plaintext)
    {
        ulong counter;
        lock (_lock) counter = ++_counter;
        return key.Seal(SenderId, counter, type, plaintext);
    }

    /// <summary>A packet's type and claimed sender, unauthenticated: only to pick the key that opens it.</summary>
    public static bool TryPeek(ReadOnlySpan<byte> packet, out byte type, out ulong sender)
    {
        bool sized = packet.Length is >= Overhead and <= PacketKey.MaxPacket;
        (type, sender) = sized ? (packet[0], BinaryPrimitives.ReadUInt64LittleEndian(packet[1..])) : ((byte)0, 0UL);
        return sized;
    }

    /// <summary>The 12-byte GCM nonce for a packet header's sender and counter.</summary>
    public static void Nonce(ulong sender, ulong counter, Span<byte> nonce)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(nonce, NoncePrefix(sender));
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], counter);
    }

    /// <summary>Nonce prefix of a sender id; two members must not share it (see class summary).</summary>
    public static uint NoncePrefix(ulong senderId) => (uint)senderId;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_secret);
        Group.Dispose();
        _ecdh.Dispose();
    }

    static byte[] Derive(byte[] input, byte[] salt, byte[] info) => HKDF.DeriveKey(HashAlgorithmName.SHA256, input, 32, salt, info);

    // ── Token: base64url([1B address length = 4][IPv4 address][2B port][32B secret][16B host key pin]) ─────────────

    public static string EncodeToken(IPEndPoint host, ReadOnlySpan<byte> secret, ReadOnlySpan<byte> pin)
    {
        if (host.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("the party socket is IPv4 only", nameof(host));
        var addr = host.Address.GetAddressBytes();
        var raw = new byte[1 + addr.Length + 2 + SecretSize + PinSize];
        raw[0] = (byte)addr.Length;
        addr.CopyTo(raw, 1);
        BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(1 + addr.Length), (ushort)host.Port);
        secret.CopyTo(raw.AsSpan(3 + addr.Length));
        pin.CopyTo(raw.AsSpan(3 + addr.Length + SecretSize));
        try { return Base64Url.EncodeToString(raw); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    /// <summary>Parses a token; the caller owns (and zeroes) the returned secret.</summary>
    public static bool TryParseToken(string token, out IPEndPoint host, out byte[] secret, out byte[] pin)
    {
        host = new IPEndPoint(IPAddress.None, 0); secret = []; pin = [];
        byte[] raw;
        try { raw = Base64Url.DecodeFromChars(token.Trim()); }
        catch (FormatException) { return false; }
        try
        {
            const int n = 4;
            if (raw.Length != 1 + n + 2 + SecretSize + PinSize || raw[0] != n) return false;
            int port = BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(1 + n));
            if (port == 0) return false;
            host = new IPEndPoint(new IPAddress(raw.AsSpan(1, n)), port);
            secret = raw.AsSpan(3 + n, SecretSize).ToArray();
            pin = raw.AsSpan(3 + n + SecretSize).ToArray();
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
}

/// <summary>One AES-256-GCM key, and per sender the last counter it accepted under that key (replay check).</summary>
public sealed class PacketKey : IDisposable
{
    /// <summary>The largest packet accepted: real messages are a few hundred bytes (a full State ~250).</summary>
    public const int MaxPacket = 1400;
    /// <summary>Senders whose last counter is kept per key: the oldest is forgotten past this. Only the group key sees
    /// more than one or two, and a token holder can make up any number of ids under it; a forgotten id's replayed
    /// Hello meets the host's list of admitted ids instead (refused, one reply per packet).</summary>
    public const int MaxSenders = 256;

    readonly AesGcm _aes;
    readonly Dictionary<ulong, ulong> _lastSeen = [];
    readonly Queue<ulong> _order = [];   // _lastSeen's ids, oldest first

    internal int Senders { get { lock (_lock) return _lastSeen.Count; } }
    readonly Lock _lock = new();

    /// <summary>Takes <paramref name="key"/> and zeroes it.</summary>
    internal PacketKey(byte[] key)
    {
        _aes = new AesGcm(key, SessionCrypto.TagSize);
        CryptographicOperations.ZeroMemory(key);
    }

    /// <summary>Only <see cref="SessionCrypto.Seal"/> picks the counter; anything else could repeat a nonce.</summary>
    internal byte[] Seal(ulong sender, ulong counter, byte type, ReadOnlySpan<byte> plaintext)
    {
        const int H = SessionCrypto.HeaderSize;
        var packet = new byte[SessionCrypto.Overhead + plaintext.Length];
        packet[0] = type;
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(1), sender);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(9), counter);
        Span<byte> nonce = stackalloc byte[12];
        SessionCrypto.Nonce(sender, counter, nonce);
        _aes.Encrypt(nonce, plaintext, packet.AsSpan(H, plaintext.Length), packet.AsSpan(H + plaintext.Length), packet.AsSpan(0, H));
        return packet;
    }

    /// <summary>Decrypts and authenticates a packet. False for anything short, tampered, under another key, or
    /// replayed (counter not above the last one accepted from that sender under this key). A counter is recorded only
    /// once the packet is authentic, so a forged one can't push it up. A sender's last counter is kept after it leaves
    /// (ids are never reused: a rejoin gets a new id), up to <see cref="MaxSenders"/> ids. Anything shorter than
    /// the header and tag or longer than <see cref="MaxPacket"/> is dropped before any allocation.</summary>
    public bool TryOpen(ReadOnlySpan<byte> packet, out byte type, out ulong sender, out byte[] plaintext)
    {
        type = 0; sender = 0; plaintext = [];
        if (packet.Length is < SessionCrypto.Overhead or > MaxPacket) return false;
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(packet[1..]);
        ulong counter = BinaryPrimitives.ReadUInt64BigEndian(packet[9..]);
        lock (_lock)
            if (_lastSeen.TryGetValue(id, out var last) && counter <= last) return false;
        var body = new byte[packet.Length - SessionCrypto.Overhead];
        Span<byte> nonce = stackalloc byte[12];
        SessionCrypto.Nonce(id, counter, nonce);
        const int H = SessionCrypto.HeaderSize, T = SessionCrypto.TagSize;
        try { _aes.Decrypt(nonce, packet.Slice(H, body.Length), packet[^T..], body, packet[..H]); }
        catch (AuthenticationTagMismatchException) { return false; }
        lock (_lock)
        {
            if (_lastSeen.TryGetValue(id, out var last) && counter <= last) return false;
            if (!_lastSeen.ContainsKey(id))
            {
                if (_order.Count >= MaxSenders) _lastSeen.Remove(_order.Dequeue());
                _order.Enqueue(id);
            }
            _lastSeen[id] = counter;
        }
        (type, sender, plaintext) = (packet[0], id, body);
        return true;
    }

    public void Dispose() => _aes.Dispose();
}
