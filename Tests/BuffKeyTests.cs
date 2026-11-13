using System.Security.Cryptography;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>The buff lock's unlock key (buff-lock.design.md): signed over this PC's code, the time made and the reset number.</summary>
public class BuffKeyTests
{
    static readonly ECDsa Signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    static readonly byte[] Public = BuffKey.PublicOf(Signer);
    static readonly string Pc = BuffKey.PcCode("4c4c4544-0042-3510-8051-b3c04f4e3432");
    static readonly DateTimeOffset Made = new(2026, 11, 12, 3, 0, 0, TimeSpan.Zero);

    static BuffKeyResult Check(string? key, DateTimeOffset now, bool entering = true, string? pc = null, ushort reset = BuffKey.Reset) =>
        BuffKey.Check(key, pc ?? Pc, now, entering, Public, reset);

    [Fact]
    public void Pc_code_is_four_groups_and_stable_per_machine()
    {
        Assert.Matches("^[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}-[A-Z2-7]{4}$", Pc);
        Assert.Equal(Pc, BuffKey.PcCode("4C4C4544-0042-3510-8051-B3C04F4E3432 "));   // case and spaces from the registry
        Assert.NotEqual(Pc, BuffKey.PcCode("11111111-0042-3510-8051-b3c04f4e3432"));
    }

    [Fact]
    public void A_fresh_key_for_this_pc_opens()
    {
        var key = BuffKey.Make(Signer, Pc, Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.열림, Check(key, Made.AddHours(9.9)));
    }

    [Fact]
    public void Pc_code_is_read_however_it_is_pasted()
    {
        var key = BuffKey.Make(Signer, Pc.ToLowerInvariant().Replace("-", ""), Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.열림, Check("  " + key + "\n", Made));
    }

    [Fact]
    public void Entered_after_10_hours_it_fails_but_a_kept_key_still_opens_at_start()
    {
        var key = BuffKey.Make(Signer, Pc, Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.시간지남, Check(key, Made.AddHours(10.1)));
        Assert.Equal(BuffKeyResult.열림, Check(key, Made.AddDays(90), entering: false));
    }

    [Fact]
    public void A_key_made_in_the_future_is_refused_at_entry()
    {
        var key = BuffKey.Make(Signer, Pc, Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.열림, Check(key, Made.AddMinutes(-9)));
        Assert.Equal(BuffKeyResult.미래, Check(key, Made.AddMinutes(-11)));
    }

    [Fact]
    public void A_key_for_another_pc_fails()
    {
        var key = BuffKey.Make(Signer, BuffKey.PcCode("other"), Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.다른PC, Check(key, Made));
    }

    [Fact]
    public void A_key_signed_by_another_private_key_fails()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(BuffKeyResult.다른PC, Check(BuffKey.Make(other, Pc, Made, BuffKey.Reset), Made));
    }

    [Fact]
    public void A_reset_release_relocks_older_keys()
    {
        var key = BuffKey.Make(Signer, Pc, Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.초기화됨, Check(key, Made, entering: false, reset: BuffKey.Reset + 1));
    }

    [Fact]
    public void A_changed_byte_fails()
    {
        var key = BuffKey.Make(Signer, Pc, Made, BuffKey.Reset).ToCharArray();
        key[3] = key[3] == 'A' ? 'B' : 'A';   // inside the time
        Assert.Equal(BuffKeyResult.다른PC, Check(new string(key), Made));
    }

    [Theory]
    [InlineData(null, BuffKeyResult.없음)]
    [InlineData("", BuffKeyResult.없음)]
    [InlineData("   ", BuffKeyResult.없음)]
    [InlineData("not a key!", BuffKeyResult.형식)]
    [InlineData("QUJD", BuffKeyResult.형식)]
    public void No_key_or_garbage_stays_locked(string? key, BuffKeyResult expected) => Assert.Equal(expected, Check(key, Made));

    [Fact]
    public void A_broken_public_key_fails_closed()
    {
        var key = BuffKey.Make(Signer, Pc, Made, BuffKey.Reset);
        Assert.Equal(BuffKeyResult.다른PC, BuffKey.Check(key, Pc, Made, true, new byte[64], BuffKey.Reset));
        Assert.Equal(BuffKeyResult.다른PC, BuffKey.Check(key, Pc, Made, true, [1, 2, 3], BuffKey.Reset));
    }

    [Fact]
    public void The_shipped_public_key_is_a_P256_point()
    {
        Assert.Equal(64, BuffKey.Shipped.Length);
        // A key from a test signer never opens the shipped key.
        Assert.Equal(BuffKeyResult.다른PC, BuffKey.Check(BuffKey.Make(Signer, Pc, Made, BuffKey.Reset), Pc, Made, true));
    }

    [Fact]
    public void Every_result_has_a_message()
    {
        foreach (var r in Enum.GetValues<BuffKeyResult>()) Assert.False(string.IsNullOrWhiteSpace(BuffKey.Message(r)));
    }
}
