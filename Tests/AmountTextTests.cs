using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public sealed class AmountTextTests
{
    [Theory]
    [InlineData(5e7, "0.5억")]
    [InlineData(1.315e10, "131.5억")]
    [InlineData(3e8, "3.0억")]
    [InlineData(2e12, "20,000.0억")]
    [InlineData(0, "0.0억")]
    public void Dps_is_always_in_eok_with_one_decimal(double value, string text) =>
        Assert.Equal(text, AmountText.Dps(value));

    [Theory]
    [InlineData(1.54e10, "154억")]
    [InlineData(5.43e9, "54.3억")]
    [InlineData(5.432e8, "5.43억")]
    [InlineData(5e7, "0.5억")]
    [InlineData(1.5e10, "150억")]
    [InlineData(9.99e10, "999억")]
    [InlineData(9.996e10, "0.1조")]   // rounds to 1000억: 조 from there
    [InlineData(1e11, "0.1조")]
    [InlineData(1.543e11, "0.154조")]
    [InlineData(1.54e12, "1.54조")]
    [InlineData(1.543e13, "15.4조")]
    [InlineData(0, "0억")]
    public void Damage_has_three_digits_and_turns_to_jo_from_1000_eok(double value, string text) =>
        Assert.Equal(text, AmountText.Damage(value));
}
