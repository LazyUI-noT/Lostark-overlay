using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace GaugeOverlay.Core.Domain;

/// <summary>Why a buff-lock key opens or not; each has a <see cref="BuffKey.Message"/>.</summary>
public enum BuffKeyResult { 열림, 없음, 형식, 다른PC, 초기화됨, 시간지남, 미래 }

/// <summary>The buff lock's unlock key (buff-lock.design.md): base64url of the time made (Unix seconds), the reset number and an
/// ECDSA P-256 signature over this PC's code, that time and that number. Made by hand with the tool outside the repos
/// (D:\AI\_keys\BuffKeyTool); the app holds the public key only. Checked at entry and at every start.</summary>
public static class BuffKey
{
    /// <summary>Bumped by a release that relocks every key handed out before it.</summary>
    public const ushort Reset = 1;

    /// <summary>A key is entered within this long of being made; a kept key is not timed again.</summary>
    public static readonly TimeSpan EnterWithin = TimeSpan.FromHours(10);

    /// <summary>A PC clock this far behind the tool's still takes a fresh key.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(10);

    /// <summary>The tool's public key (X ‖ Y); its private half never leaves D:\AI\_keys (outside every repo).</summary>
    public static readonly byte[] Shipped = Convert.FromBase64String(
        "1/HPl9E6hbLBX5WEco5QNiBL5nklOpzxL3kRnPOorMZViliRGtlERGt74vUrqWPaPYBIQrqvKyI2/L67d/BMjQ==");

    const int PcBytes = 10, TimeBytes = 8, ResetBytes = 2, SignatureBytes = 64, PointBytes = 32;
    const int KeyBytes = TimeBytes + ResetBytes + SignatureBytes;
    const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    static readonly byte[] Context = "gauge buff-lock v1"u8.ToArray();

    /// <summary>The code a user sends for a key: SHA-256 of the Windows MachineGuid (never shown itself), 10 bytes as base32 in
    /// four groups of four.</summary>
    public static string PcCode(string machineGuid)
    {
        var hash = SHA256.HashData([.. "gauge buff-lock pc"u8, .. Encoding.UTF8.GetBytes(machineGuid.Trim().ToLowerInvariant())]);
        var code = ToBase32(hash.AsSpan(0, PcBytes));
        return string.Join('-', Enumerable.Range(0, 4).Select(i => code.Substring(i * 4, 4)));
    }

    /// <summary>X ‖ Y of a P-256 key's public half, as <see cref="Shipped"/> holds it.</summary>
    public static byte[] PublicOf(ECDsa key)
    {
        var q = key.ExportParameters(false).Q;
        return [.. q.X!, .. q.Y!];
    }

    /// <summary>A key for <paramref name="pcCode"/> (as pasted: any case, dashes or not), made at <paramref name="issued"/>.
    /// Public on purpose: it only works with the private key, which lives outside every repo (D:\AI\_keys); the tests and the
    /// key tool share this one format.</summary>
    public static string Make(ECDsa signer, string pcCode, DateTimeOffset issued, ushort reset)
    {
        var pc = PcOf(pcCode) ?? throw new ArgumentException("not a PC code (16 letters A–Z, 2–7)", nameof(pcCode));
        long seconds = issued.ToUnixTimeSeconds();
        var signature = signer.SignData(Message(pc, seconds, reset), HashAlgorithmName.SHA256);
        var key = new byte[KeyBytes];
        BinaryPrimitives.WriteInt64BigEndian(key, seconds);
        BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(TimeBytes), reset);
        signature.CopyTo(key, TimeBytes + ResetBytes);
        return Base64Url.EncodeToString(key);
    }

    /// <summary>The shipped public key and reset number.</summary>
    public static BuffKeyResult Check(string? key, string pcCode, DateTimeOffset now, bool entering) =>
        Check(key, pcCode, now, entering, Shipped, Reset);

    /// <summary>Whether <paramref name="key"/> opens the lock on the PC with <paramref name="pcCode"/>. The time is checked only
    /// while <paramref name="entering"/>: a kept key opens at every start until <paramref name="reset"/> moves on.</summary>
    public static BuffKeyResult Check(string? key, string pcCode, DateTimeOffset now, bool entering, byte[] publicKey, ushort reset)
    {
        if (string.IsNullOrWhiteSpace(key)) return BuffKeyResult.없음;
        byte[] bytes;
        try { bytes = Base64Url.DecodeFromChars(key.Trim()); }
        catch (FormatException) { return BuffKeyResult.형식; }
        if (bytes.Length != KeyBytes) return BuffKeyResult.형식;
        long seconds = BinaryPrimitives.ReadInt64BigEndian(bytes);
        ushort keyReset = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(TimeBytes));
        if (PcOf(pcCode) is not { } pc || !Verify(publicKey, Message(pc, seconds, keyReset), bytes[(TimeBytes + ResetBytes)..]))
            return BuffKeyResult.다른PC;
        if (keyReset != reset) return BuffKeyResult.초기화됨;
        if (!entering) return BuffKeyResult.열림;
        var issued = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, DateTimeOffset.MaxValue.ToUnixTimeSeconds()));
        return now - issued > EnterWithin ? BuffKeyResult.시간지남
            : issued - now > Skew ? BuffKeyResult.미래
            : BuffKeyResult.열림;
    }

    public static string Message(BuffKeyResult r) => r switch
    {
        BuffKeyResult.열림 => "버프 잠금이 풀려 있습니다. 모든 버프 적용률이 보입니다.",
        BuffKeyResult.없음 => "버프 잠금 중입니다. 키를 넣으면 모든 버프 적용률이 보입니다.",
        BuffKeyResult.형식 => "키 형식이 아닙니다. 받은 키 한 줄을 그대로 붙여 넣어 주세요.",
        BuffKeyResult.다른PC => "이 PC의 키가 아닙니다. 위의 PC 코드로 받은 키인지 확인해 주세요.",
        BuffKeyResult.초기화됨 => "업데이트로 초기화된 키입니다. 새 키를 받아 주세요.",
        BuffKeyResult.시간지남 => "만든 지 10시간이 지난 키입니다. 새 키를 받아 주세요.",
        BuffKeyResult.미래 => "PC 시계가 맞지 않습니다. Windows 시간을 맞춘 뒤 다시 넣어 주세요.",
        _ => r.ToString(),
    };

    static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey.Length != 2 * PointBytes || signature.Length != SignatureBytes) return false;
        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = publicKey[..PointBytes], Y = publicKey[PointBytes..] },
            });
            return key.VerifyData(message, signature, HashAlgorithmName.SHA256);
        }
        // Windows CNG reports a point that isn't on the curve as PlatformNotSupportedException (DeviceIdentity).
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException) { return false; }
    }

    static byte[] Message(byte[] pc, long seconds, ushort reset)
    {
        var tail = new byte[TimeBytes + ResetBytes];
        BinaryPrimitives.WriteInt64BigEndian(tail, seconds);
        BinaryPrimitives.WriteUInt16BigEndian(tail.AsSpan(TimeBytes), reset);
        return [.. Context, .. pc, .. tail];
    }

    /// <summary>The code's 10 bytes, or null when it isn't 16 base32 letters (dashes, spaces and case ignored).</summary>
    static byte[]? PcOf(string code)
    {
        var letters = new string([.. code.ToUpperInvariant().Where(c => c is not ('-' or ' '))]);
        if (letters.Length != PcBytes * 8 / 5) return null;
        var bytes = new byte[PcBytes];
        int bits = 0, acc = 0, at = 0;
        foreach (char c in letters)
        {
            int v = Base32.IndexOf(c);
            if (v < 0) return null;
            acc = (acc << 5) | v;
            bits += 5;
            if (bits >= 8) { bytes[at++] = (byte)(acc >> (bits - 8)); bits -= 8; acc &= (1 << bits) - 1; }
        }
        return bytes;
    }

    static string ToBase32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        int bits = 0, acc = 0;
        foreach (byte b in data)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5) { sb.Append(Base32[(acc >> (bits - 5)) & 31]); bits -= 5; }
            acc &= (1 << bits) - 1;
        }
        return sb.ToString();
    }
}
