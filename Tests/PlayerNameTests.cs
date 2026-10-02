using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

public class PlayerNameTests
{
    [Theory]
    [InlineData("가나다라마바사아", "가나다라마바사")]          // 7 Korean
    [InlineData("abcdefghijklmnop", "abcdefghijklmn")]          // 14 English
    [InlineData("가나다abcdefghi", "가나다abcdefgh")]            // 3×2 + 8
    [InlineData("가나다라마바a사", "가나다라마바a")]             // a wide character never crosses the limit
    [InlineData("abcdefghijklm😀", "abcdefghijklm")]          // never splits a surrogate pair
    [InlineData("짧은이름", "짧은이름")]
    [InlineData("", "")]
    public void Names_are_cut_at_seven_korean_or_fourteen_english_characters(string name, string shown) =>
        Assert.Equal(shown, PlayerName.Clip(name));

    [Theory]
    [InlineData("가나다라마바사", 6, "가나다")]      // 이름 길이 6: 3 Korean
    [InlineData("abcdefgh", 6, "abcdef")]
    [InlineData("가a나다", 3, "가a")]
    [InlineData("가나다", 0, "가")]                  // below the smallest length: the smallest (2 units)
    [InlineData("가나다라마바사아", 99, "가나다라마바사")]   // above the largest: the largest
    public void Names_can_be_cut_shorter(string name, int max, string shown) =>
        Assert.Equal(shown, PlayerName.Clip(name, max));
}
