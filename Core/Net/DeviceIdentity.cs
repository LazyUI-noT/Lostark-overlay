using System.Security.Cryptography;

namespace GaugeOverlay.Core.Net;

/// <summary>This install's lasting identity for party access (참가 요청 승인, 신뢰한 기기, 내보내기): a P-256 signing key
/// kept beside the settings. A joiner's Hello carries the public key and a signature over its session key and the
/// token's host pin, so a host can recognise the device across sessions, and another token holder can't claim it: a
/// copied Hello names a session key whose private half it doesn't have, so it never gets past the pair key.</summary>
public sealed class DeviceIdentity : IDisposable
{
    public const int KeySize = 64, SignatureSize = 64;
    const int PointSize = 32;
    const string P256 = "1.2.840.10045.3.1.7";
    static readonly byte[] Context = "gauge-v10 hello"u8.ToArray();

    readonly ECDsa _key;

    /// <summary>X ‖ Y of the public key, as the Hello carries it.</summary>
    public byte[] PublicKey { get; }

    DeviceIdentity(ECDsa key)
    {
        _key = key;
        var q = key.ExportParameters(false).Q;
        PublicKey = [.. q.X!, .. q.Y!];
    }

    public static DeviceIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>A key saved with <see cref="Export"/>; null if the bytes aren't a P-256 private key.</summary>
    public static DeviceIdentity? Import(byte[] pkcs8)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            if (key.ExportParameters(false).Curve.Oid?.Value == P256) return new DeviceIdentity(key);
        }
        catch (CryptographicException) { }
        key.Dispose();
        return null;
    }

    /// <summary>The private key as PKCS#8, for the key file.</summary>
    public byte[] Export() => _key.ExportPkcs8PrivateKey();

    /// <summary>Signs a Hello's sender id and session key for the host the token pins (64 bytes, r ‖ s). The sender id is
    /// signed too, so a copied Hello can't be re-sent under another id.</summary>
    public byte[] Sign(ulong sender, ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> pin) =>
        _key.SignData(Message(sender, sessionKey, pin), HashAlgorithmName.SHA256);

    /// <summary>True when <paramref name="signature"/> is <paramref name="deviceKey"/>'s over this sender, session key and pin.</summary>
    public static bool Verify(byte[] deviceKey, byte[] signature, ulong sender, ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> pin)
    {
        if (deviceKey.Length != KeySize || signature.Length != SignatureSize) return false;
        try
        {
            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = deviceKey[..PointSize], Y = deviceKey[PointSize..] },
            });
            return key.VerifyData(Message(sender, sessionKey, pin), signature, HashAlgorithmName.SHA256);
        }
        // Windows CNG reports a point that isn't on the curve as PlatformNotSupportedException.
        catch (Exception e) when (e is CryptographicException or PlatformNotSupportedException) { return false; }
    }

    /// <summary>How the trusted and blocked lists name a device: hex of the first 16 bytes of SHA-256 of its key.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> deviceKey) => Convert.ToHexString(SHA256.HashData(deviceKey)[..16]);

    static byte[] Message(ulong sender, ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> pin) =>
        [.. Context, .. BitConverter.GetBytes(sender), .. sessionKey, .. pin];

    public void Dispose() => _key.Dispose();
}
