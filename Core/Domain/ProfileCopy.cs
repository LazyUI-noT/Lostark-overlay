namespace GaugeOverlay.Core.Domain;

/// <summary>The three ways to copy (class-profiles.plan.md C): ① the current class to every class of its profile, ② profile X
/// class A to profile Y class B, ③ profile X whole to profile Y.</summary>
public enum CopyMode { AllClasses, ClassToClass, WholeProfile }

/// <summary>A 복사. Classes are <see cref="Settings.CharacterKey"/>s; null or "" is the profile's shared 기본. ① has the same profile on
/// both sides. <paramref name="Looks"/> and <paramref name="ClassOnly"/> are ignored by ③, which copies everything.</summary>
public sealed record CopyRequest(CopyMode Mode, int FromProfile, string? FromClass, int ToProfile, string? ToClass,
    IReadOnlySet<LookGroup> Looks, bool ClassOnly);

/// <summary>What 되돌리기 puts back: the whole profile (<paramref name="ClassKey"/> null: ① ③), the shared entry alone (<see cref="ProfileCopy.SharedKey"/>: ② into 기본) or one class's entry
/// (and its 게이지 bars and 아덴 boxes when <paramref name="GaugeBefore"/> and <paramref name="AdenBefore"/> are set), as JSON; "null" JSON means it was absent.</summary>
public sealed record CopyUndo(int Profile, string? ClassKey, byte[] Before, byte[]? GaugeBefore, byte[]? AdenBefore = null);

/// <summary>The rules and texts of 복사 (the work is <see cref="Settings.Copy"/>).</summary>
public static class ProfileCopy
{
    /// <summary><see cref="CopyUndo.ClassKey"/> of a copy into 기본: undo restores the profile's shared entry only.</summary>
    public const string SharedKey = "";

    internal static string? Normalize(string? cls) => string.IsNullOrEmpty(cls) ? null : cls;

    /// <summary>클래스 전용 goes along only for ② between the same, known class (in two profiles).</summary>
    public static bool ClassOnlyAllowed(CopyRequest r) =>
        r.Mode == CopyMode.ClassToClass && Normalize(r.FromClass) is { } c && c == Normalize(r.ToClass);

    /// <summary>Why 클래스 전용 is greyed; "" when it is allowed.</summary>
    public static string ClassOnlyReason(CopyRequest r) => r.Mode switch
    {
        _ when ClassOnlyAllowed(r) => "",
        CopyMode.ClassToClass => "클래스 전용 설정은 같은 클래스끼리만 복사합니다.",
        CopyMode.AllClasses => "클래스 전용 설정은 다른 프로필의 같은 클래스로 복사할 때만 복사합니다.",
        _ => "프로필 전체 복사는 클래스 전용 설정도 함께 복사합니다.",
    };

    /// <summary>Whether <paramref name="r"/> can run on <paramref name="profiles"/> profiles and has something to copy.</summary>
    public static bool Valid(CopyRequest r, int profiles)
    {
        if (r.FromProfile < 0 || r.FromProfile >= profiles || r.ToProfile < 0 || r.ToProfile >= profiles) return false;
        return r.Mode switch
        {
            CopyMode.WholeProfile => r.FromProfile != r.ToProfile,
            CopyMode.AllClasses => r.FromProfile == r.ToProfile && r.Looks.Count > 0,
            _ => (r.FromProfile != r.ToProfile || Normalize(r.FromClass) != Normalize(r.ToClass))
                && (r.Looks.Count > 0 || r.ClassOnly && ClassOnlyAllowed(r)),
        };
    }

    /// <summary>The confirm line: what gets overwritten. <paramref name="targetLabel"/> names ②'s target class (null: 기본).</summary>
    public static string Confirm(CopyRequest r, string? targetLabel)
    {
        const string Undo = " 되돌리면 그 뒤에 바꾼 것도 사라집니다.";
        if (r.Mode == CopyMode.WholeProfile)
            return $"프로필 {r.ToProfile + 1}의 모든 설정을 덮어씁니다. 화면(스크린샷)만 남습니다.{Undo}";
        var what = string.Join(", ", r.Looks.Order().Select(LookShare.Name));
        if (r.Mode == CopyMode.AllClasses)
            return $"프로필 {r.ToProfile + 1}의 모든 클래스에서 {what} 모양을 덮어씁니다. 창 위치와 크기는 그대로입니다.{Undo}";
        what = ClassOnlyAllowed(r) && r.ClassOnly ? (what.Length == 0 ? "클래스 전용 설정" : $"{what} 모양과 클래스 전용 설정") : $"{what} 모양";
        return $"프로필 {r.ToProfile + 1} · {targetLabel ?? "기본"}의 {what}을(를) 덮어씁니다. 창 위치와 크기는 그대로입니다.{Undo}";
    }
}
