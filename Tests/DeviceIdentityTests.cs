using GaugeOverlay.Core.Net;

namespace GaugeOverlay.Tests;

public sealed class DeviceIdentityTests
{
    static readonly byte[] SessionKey = [.. Enumerable.Range(1, SessionCrypto.PublicKeySize).Select(i => (byte)i)];
    const ulong Sender = 0x1234_5678_9ABC;
    static readonly byte[] Pin = [.. Enumerable.Range(200, SessionCrypto.PinSize).Select(i => (byte)i)];

    [Fact]
    public void Signature_verifies_for_its_session_key_and_pin()
    {
        using var device = DeviceIdentity.Create();
        var sig = device.Sign(Sender, SessionKey, Pin);
        Assert.Equal(DeviceIdentity.SignatureSize, sig.Length);
        Assert.True(DeviceIdentity.Verify(device.PublicKey, sig, Sender, SessionKey, Pin));
    }

    [Fact]
    public void Signature_fails_for_another_session_key_pin_or_device()
    {
        using var device = DeviceIdentity.Create();
        using var other = DeviceIdentity.Create();
        var sig = device.Sign(Sender, SessionKey, Pin);
        byte[] otherKey = [.. SessionKey.Reverse()];
        byte[] otherPin = [.. Pin.Reverse()];
        Assert.False(DeviceIdentity.Verify(device.PublicKey, sig, Sender + 1, SessionKey, Pin));   // a replay under another sender id
        Assert.False(DeviceIdentity.Verify(device.PublicKey, sig, Sender, otherKey, Pin));
        Assert.False(DeviceIdentity.Verify(device.PublicKey, sig, Sender, SessionKey, otherPin));
        Assert.False(DeviceIdentity.Verify(other.PublicKey, sig, Sender, SessionKey, Pin));
        Assert.False(DeviceIdentity.Verify(device.PublicKey, [], Sender, SessionKey, Pin));
        Assert.False(DeviceIdentity.Verify(new byte[DeviceIdentity.KeySize], sig, Sender, SessionKey, Pin));   // not a curve point
    }

    [Fact]
    public void Export_then_import_keeps_the_key()
    {
        using var device = DeviceIdentity.Create();
        using var back = DeviceIdentity.Import(device.Export())!;
        Assert.Equal(device.PublicKey, back.PublicKey);
        Assert.True(DeviceIdentity.Verify(device.PublicKey, back.Sign(Sender, SessionKey, Pin), Sender, SessionKey, Pin));
    }

    [Fact]
    public void Garbage_import_returns_null()
    {
        Assert.Null(DeviceIdentity.Import([1, 2, 3]));
        Assert.Null(DeviceIdentity.Import([]));
    }

    [Fact]
    public void The_fingerprint_is_stable_and_differs_per_device()
    {
        using var a = DeviceIdentity.Create();
        using var b = DeviceIdentity.Create();
        Assert.Equal(DeviceIdentity.Fingerprint(a.PublicKey), DeviceIdentity.Fingerprint(a.PublicKey));
        Assert.NotEqual(DeviceIdentity.Fingerprint(a.PublicKey), DeviceIdentity.Fingerprint(b.PublicKey));
        Assert.Equal(32, DeviceIdentity.Fingerprint(a.PublicKey).Length);
    }
}
