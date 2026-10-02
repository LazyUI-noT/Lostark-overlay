using System.Security.Cryptography;
using System.Text;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

/// <summary>업데이트 확인: GitHub's latest release of Lostark-overlay, compared with the running version, and the zip checked
/// against its .sha256 before it's installed.</summary>
public class UpdateCheckTests
{
    const string Base = "https://github.com/LazyUI-noT/Lostark-overlay/releases/download/v1.0.6/";

    static string Json(string tag = "v1.0.6", bool draft = false, bool pre = false, bool sha = true, string zipUrl = Base + "GaugeOverlay-v1.0.6.zip",
        string body = "") =>
        $$"""
        {"tag_name":"{{tag}}","body":{{System.Text.Json.JsonSerializer.Serialize(body)}},"draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},"assets":[
          {"name":"GaugeOverlay-v1.0.6.zip","browser_download_url":"{{zipUrl}}"}
          {{(sha ? $$""",{"name":"GaugeOverlay-v1.0.6.zip.sha256","browser_download_url":"{{Base}}GaugeOverlay-v1.0.6.zip.sha256"}""" : "")}}
        ]}
        """;

    [Fact]
    public void The_latest_release_gives_its_version_and_both_download_links()
    {
        var r = UpdateCheck.Parse(Json());
        Assert.NotNull(r);
        Assert.Equal(new Version(1, 0, 6), r.Version);
        Assert.Equal("v1.0.6", r.Tag);
        Assert.Equal(Base + "GaugeOverlay-v1.0.6.zip", r.ZipUrl);
        Assert.Equal(Base + "GaugeOverlay-v1.0.6.zip.sha256", r.ShaUrl);
    }

    [Fact]
    public void The_release_notes_come_with_the_release() =>
        Assert.Equal("- **아이콘** 저장\r\n- 버그 수정", UpdateCheck.Parse(Json(body: "- **아이콘** 저장\r\n- 버그 수정"))!.Notes);

    [Fact]
    public void Release_notes_are_shown_as_plain_bullets_without_markdown() =>
        Assert.Equal("• 아이콘 다시 읽기: 모든 칸\n• 버그 수정", UpdateCheck.PlainNotes("- **아이콘 다시 읽기**: 모든 칸\r\n\r\n* 버그 수정\r\n"));

    [Fact]
    public void Release_note_headings_become_bracketed_groups_with_a_blank_line_between() =>
        Assert.Equal("[새 기능]\n• 업데이트 확인\n\n[버그 수정]\n• 편집 모드 뒤 오버레이",
            UpdateCheck.PlainNotes("### 새 기능\r\n- 업데이트 확인\r\n\r\n### 버그 수정\r\n- 편집 모드 뒤 오버레이\r\n"));

    [Fact]
    public void Very_long_release_notes_are_cut_short()
    {
        var notes = UpdateCheck.PlainNotes(string.Join("\n", Enumerable.Repeat("- " + new string('가', 100), 100)));
        Assert.True(notes.Length <= UpdateCheck.MaxNotes + 1);
        Assert.EndsWith("…", notes);
    }

    [Fact]
    public void Empty_release_notes_stay_empty() => Assert.Equal("", UpdateCheck.PlainNotes("  \r\n "));

    [Theory]
    [InlineData(true, false, true, "v1.0.6")]    // draft
    [InlineData(false, true, true, "v1.0.6")]    // prerelease
    [InlineData(false, false, false, "v1.0.6")]  // no .sha256
    [InlineData(false, false, true, "latest")]   // tag isn't a version
    public void A_release_that_cant_be_installed_safely_is_none(bool draft, bool pre, bool sha, string tag) =>
        Assert.Null(UpdateCheck.Parse(Json(tag, draft, pre, sha)));

    [Fact]
    public void A_download_from_anywhere_but_the_releases_of_Lostark_overlay_is_refused() =>
        Assert.Null(UpdateCheck.Parse(Json(zipUrl: "https://example.com/GaugeOverlay-v1.0.6.zip")));

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"message\":\"Not Found\"}")]
    public void A_broken_or_error_answer_is_none(string json) => Assert.Null(UpdateCheck.Parse(json));

    [Theory]
    [InlineData("1.0.6", "1.0.5.0", true)]
    [InlineData("1.1.0", "1.0.9.0", true)]
    [InlineData("1.0.5", "1.0.5.0", false)]
    [InlineData("1.0.4", "1.0.5.0", false)]
    public void Only_a_higher_version_is_an_update(string latest, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void The_zip_must_match_its_sha256_file()
    {
        var zip = Encoding.UTF8.GetBytes("zip bytes");
        string hex = Convert.ToHexString(SHA256.HashData(zip));
        Assert.True(UpdateCheck.HashMatches(new MemoryStream(zip), $"{hex.ToLowerInvariant()}  GaugeOverlay-v1.0.6.zip\n"));
        Assert.True(UpdateCheck.HashMatches(new MemoryStream(zip), hex));
        Assert.False(UpdateCheck.HashMatches(new MemoryStream(zip), new string('0', 64)));
        Assert.False(UpdateCheck.HashMatches(new MemoryStream(zip), ""));
    }
}
