using System.Text.RegularExpressions;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Data;

/// <summary>A row of a 직업각인's _NEED.md (reference/classes/&lt;클래스&gt;/&lt;직업각인&gt;/, generated from the gauge manifest).
/// <paramref name="FullScreen"/>: a whole screenshot, so the skill bar must be found in it.</summary>
public sealed record NeedRow(string File, bool Required, bool FullScreen);

/// <summary>A file of a submission as the App found it: whether it is really a PNG (by its bytes, not its name), and the
/// decoded picture (null when it isn't one).</summary>
public sealed record SubmissionFile(string Name, bool Png, Image? Image);

/// <summary>One finding. <paramref name="FollowUp"/>: the Korean line to send back, null when nothing is asked.</summary>
public sealed record SubmissionNote(string File, bool Ok, string Text, string? FollowUp = null);

/// <summary>--check-submission: what a user sent for one 직업각인, checked against its _NEED.md before anything is built
/// (CLASS_ADDING.md, Receiving a submission). The lines asked back are CLASS_ADDING.md's Follow-up lines.</summary>
public static partial class Submission
{
    public const string AskPng = "스크린샷이 압축되어 화질이 떨어졌습니다. 원본 PNG 파일 그대로 올려 주세요.";
    public const string AskSkillBar = "스크린샷에서 스킬창을 찾지 못했습니다. 수련장에서 재사용 대기시간을 초기화해 모든 스킬이 준비된 상태로, 화면 전체를 다시 찍어 주세요.";
    public const string AskGaugePair = "아덴 게이지가 완전히 빈 화면과 가득 찬 화면을 각각 한 장씩 보내 주세요. 다른 것은 바뀌지 않게, 같은 자리에서 찍어 주세요.";
    public const string AskIcon = "초각스 아이콘이 보이는 스킬 창 스크린샷이 빠졌습니다.";

    static readonly string[] Clips = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    [GeneratedRegex(@"^\|\s*`([^`]+)`\s*\|\s*(yes|optional)\s*\|\s*(.*?)\s*\|\s*$", RegexOptions.Multiline)]
    private static partial Regex Row();

    /// <summary>The file table of a _NEED.md.</summary>
    public static IReadOnlyList<NeedRow> ParseNeed(string markdown) =>
        [.. Row().Matches(markdown).Select(m => new NeedRow(m.Groups[1].Value, m.Groups[2].Value == "yes",
            m.Groups[3].Value.Contains("full-screen", StringComparison.OrdinalIgnoreCase)))];

    /// <summary>The name in <paramref name="known"/> that <paramref name="folder"/> spells, ignoring spaces (the intake folders
    /// came from the manifest's spelling, "광전사의 비기"; Profiles and classes.json write "광전사의비기"); null for none.</summary>
    public static string? Canonical(string folder, IEnumerable<string> known)
    {
        static string Bare(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));
        return known.FirstOrDefault(k => Bare(k) == Bare(folder));
    }

    /// <summary>A note per listed file (present or missing) and per clip; files the list doesn't name are left out.</summary>
    public static IReadOnlyList<SubmissionNote> Check(IReadOnlyList<SubmissionFile> files, IReadOnlyList<NeedRow> need)
    {
        var notes = new List<SubmissionNote>();
        foreach (var row in need)
        {
            var sent = files.Where(f => Names(f.Name, row.File)).ToList();
            if (sent.Count == 0) notes.Add(Missing(row));
            else notes.AddRange(sent.Select(f => Present(f, row)));
        }
        foreach (var clip in files.Where(f => Clips.Contains(Path.GetExtension(f.Name).ToLowerInvariant())))
            notes.Add(new(clip.Name, true, "영상: 효과 확인은 --trace-gauge로 (게이지를 만든 뒤)"));
        return notes;
    }

    /// <summary>Whether <paramref name="name"/> is <paramref name="listed"/> or a repeat of it (_NEED.md: "add -1, -2 for repeats").</summary>
    static bool Names(string name, string listed) =>
        Regex.IsMatch(name, $"^{Regex.Escape(Path.GetFileNameWithoutExtension(listed))}(-[0-9]+)?{Regex.Escape(Path.GetExtension(listed))}$", RegexOptions.IgnoreCase);

    static SubmissionNote Missing(NeedRow row) => !row.Required
        ? new(row.File, true, "없음 (선택)")
        : new(row.File, false, "없음 (필수)", row.File.StartsWith("gauge_", StringComparison.Ordinal) ? AskGaugePair
            : row.File.StartsWith("icon_", StringComparison.Ordinal) ? AskIcon : $"{row.File} 파일이 빠졌습니다.");

    static SubmissionNote Present(SubmissionFile file, NeedRow row)
    {
        if (!file.Png || file.Image is null) return new(file.Name, false, "PNG 원본이 아님", AskPng);
        if (row.FullScreen && ReferenceShot.Find(file.Image) is null) return new(file.Name, false, "스킬창을 찾지 못함", AskSkillBar);
        return new(file.Name, true, row.FullScreen ? "PNG, 스킬창 찾음" : "PNG");
    }
}
