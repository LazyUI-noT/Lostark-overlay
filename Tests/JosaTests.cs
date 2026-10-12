using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>Korean particles picked by the word's last sound, so on-screen text reads "워로드를", not "워로드을(를)".</summary>
public class JosaTests
{
    [Theory]
    [InlineData("워로드", "워로드를")]          // vowel ending
    [InlineData("버서커 · 광기", "버서커 · 광기를")]
    [InlineData("기본", "기본을")]              // consonant ending
    [InlineData("홀리나이트 · 심판자", "홀리나이트 · 심판자를")]
    public void Object_particle_follows_the_last_syllable(string word, string expected) =>
        Assert.Equal(expected, Josa.Object(word));

    [Theory]
    [InlineData("일반1", "일반1과")]           // 1 = 일, ends in a consonant
    [InlineData("일반2", "일반2와")]           // 2 = 이, a vowel
    [InlineData("아덴1, 각성기", "아덴1, 각성기와")]
    public void With_particle_reads_trailing_digits_aloud(string word, string expected) =>
        Assert.Equal(expected, Josa.With(word));

    [Theory]
    [InlineData("v1.2.2", "v1.2.2가")]         // 2 = 이
    [InlineData("v1.3.0", "v1.3.0이")]         // 0 = 영
    [InlineData("47800", "47800이")]
    public void Subject_particle_reads_versions_and_ports(string word, string expected) =>
        Assert.Equal(expected, Josa.Subject(word));

    [Theory]
    [InlineData("47800", "47800을")]
    [InlineData("47802", "47802를")]
    public void Object_particle_reads_port_numbers(string word, string expected) =>
        Assert.Equal(expected, Josa.Object(word));

    [Fact]
    public void A_word_it_cannot_read_keeps_both_forms() =>
        Assert.Equal("ABC을(를)", Josa.Object("ABC"));

    [Fact]
    public void An_empty_word_keeps_both_forms() =>
        Assert.Equal("이(가)", Josa.Subject(""));
}
