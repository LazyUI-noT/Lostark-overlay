using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>업데이트 채널 (user, 2026-11-03): 베타 offers vX.Y.Z-beta.N pre-releases to try new features; 안정 offers only
/// stable releases, and takes a beta install back to the latest stable one.</summary>
public class UpdateChannelTests
{
    const string Base = "https://github.com/LazyUI-noT/Lostark-overlay/releases/download/";

    static string Entry(string tag, bool pre = false, bool draft = false) => $$"""
        {"tag_name":"{{tag}}","body":"","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"assets":[
          {"name":"GaugeOverlay-{{tag}}.zip","browser_download_url":"{{Base}}{{tag}}/GaugeOverlay-{{tag}}.zip"},
          {"name":"GaugeOverlay-{{tag}}.zip.sha256","browser_download_url":"{{Base}}{{tag}}/GaugeOverlay-{{tag}}.zip.sha256"}]}
        """;

    static string List(params string[] entries) => "[" + string.Join(",", entries) + "]";

    static IReadOnlyList<Release> Releases() => UpdateCheck.ParseList(List(
        Entry("v0.16.0-beta.2", pre: true), Entry("v0.16.0-beta.1", pre: true), Entry("v0.15.3"), Entry("v0.15.2"),
        Entry("alpha-1.3.2", pre: true), Entry("v0.17.0", draft: true)));

    [Theory]
    [InlineData("v0.15.3", 0, 15, 3, 0)]
    [InlineData("v0.16.0-beta.2", 0, 16, 0, 2)]
    public void A_tag_gives_its_version_and_beta_number(string tag, int major, int minor, int patch, int beta)
    {
        var t = UpdateCheck.ParseTag(tag);
        Assert.NotNull(t);
        Assert.Equal(new Version(major, minor, patch), t.Value.Version);
        Assert.Equal(beta, t.Value.Beta);
    }

    [Theory]
    [InlineData("alpha-1.3.2")]
    [InlineData("latest")]
    [InlineData("v0.16.0-rc.1")]
    [InlineData("v0.16.0-beta.0")]
    [InlineData("v0.16")]
    public void Other_tags_are_not_releases(string tag) => Assert.Null(UpdateCheck.ParseTag(tag));

    [Theory]
    [InlineData("v0.16.0-beta.1", "v0.15.3", 1)]       // a beta of the next version is after the stable one before it
    [InlineData("v0.16.0", "v0.16.0-beta.9", 1)]       // the stable release is after its betas
    [InlineData("v0.16.0-beta.2", "v0.16.0-beta.10", -1)]   // beta numbers compare as numbers
    [InlineData("v0.15.3", "v0.15.3", 0)]
    public void Tags_are_ordered_by_version_then_stable_after_its_betas(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(UpdateCheck.CompareTags(a, b)));

    [Fact]
    public void The_list_keeps_stable_releases_and_beta_prereleases_only()
    {
        Assert.Equal(["v0.16.0-beta.2", "v0.16.0-beta.1", "v0.15.3", "v0.15.2"], Releases().Select(r => r.Tag));
        Assert.True(Releases()[0].IsBeta);
        Assert.False(Releases()[2].IsBeta);
    }

    [Fact]
    public void A_prerelease_must_carry_a_beta_tag()
    {
        Assert.Empty(UpdateCheck.ParseList(List(Entry("v0.16.0", pre: true))));
        Assert.True(UpdateCheck.Parse(Entry("v0.16.0-beta.1", pre: false))!.IsBeta);   // the tag says beta, whatever the flag
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"message\":\"Not Found\"}")]
    public void A_broken_list_is_empty(string json) => Assert.Empty(UpdateCheck.ParseList(json));

    [Fact]
    public void Stable_offers_the_latest_stable_release_when_newer() =>
        Assert.Equal("v0.15.3", UpdateCheck.Offer(UpdateChannel.Stable, "v0.15.2", Releases())?.Tag);

    [Fact]
    public void Stable_on_the_latest_stable_release_offers_nothing() =>
        Assert.Null(UpdateCheck.Offer(UpdateChannel.Stable, "v0.15.3", Releases()));

    [Fact]
    public void Beta_offers_the_newest_beta() =>
        Assert.Equal("v0.16.0-beta.2", UpdateCheck.Offer(UpdateChannel.Beta, "v0.15.3", Releases())?.Tag);

    [Fact]
    public void Beta_offers_the_stable_release_once_it_is_out()
    {
        var list = UpdateCheck.ParseList(List(Entry("v0.16.0"), Entry("v0.16.0-beta.2", pre: true)));
        Assert.Equal("v0.16.0", UpdateCheck.Offer(UpdateChannel.Beta, "v0.16.0-beta.2", list)?.Tag);
    }

    [Fact]
    public void Beta_on_the_newest_beta_offers_nothing() =>
        Assert.Null(UpdateCheck.Offer(UpdateChannel.Beta, "v0.16.0-beta.2", Releases()));

    [Fact]
    public void Stable_on_a_beta_offers_the_latest_stable_release_back()
    {
        var back = UpdateCheck.Offer(UpdateChannel.Stable, "v0.16.0-beta.2", Releases());
        Assert.Equal("v0.15.3", back?.Tag);
        Assert.True(UpdateCheck.IsDowngrade(back!, "v0.16.0-beta.2"));
        Assert.False(UpdateCheck.IsDowngrade(back!, "v0.15.2"));
    }

    [Fact]
    public void The_channel_is_stable_by_default() => Assert.Equal(UpdateChannel.Stable, new UpdateSettings().Channel);

    [Theory]
    [InlineData("0.15.3", "v0.15.3")]
    [InlineData("0.16.0-beta.1+8f3c2d1", "v0.16.0-beta.1")]   // the SDK appends the commit
    public void The_running_tag_comes_from_the_informational_version(string info, string tag) =>
        Assert.Equal(tag, UpdateCheck.TagOf(info));
}
