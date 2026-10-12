namespace GaugeOverlay.Core.Domain;

/// <summary>Korean particles (조사) after a word known only at run time: the class name, a slot, a version, a port. Picked by the
/// last sound: a Hangul syllable's 받침, or a trailing digit read aloud (0 영, 1 일, 3 삼… end in a consonant). A word ending
/// in anything else keeps both forms, "을(를)", as before.</summary>
public static class Josa
{
    /// <summary>을/를: "워로드를", "기본을".</summary>
    public static string Object(string word) => word + Pick(word, "을", "를");

    /// <summary>이/가: "v1.2.2가", "v1.3.0이".</summary>
    public static string Subject(string word) => word + Pick(word, "이", "가");

    /// <summary>과/와: "일반1과", "각성기와".</summary>
    public static string With(string word) => word + Pick(word, "과", "와");

    static string Pick(string word, string afterConsonant, string afterVowel) => EndsInConsonant(word) switch
    {
        true => afterConsonant,
        false => afterVowel,
        null => $"{afterConsonant}({afterVowel})",
    };

    /// <summary>Whether the word's last sound is a consonant; null when its last character can't be read.</summary>
    static bool? EndsInConsonant(string word)
    {
        if (word.Length == 0) return null;
        char last = word[^1];
        if (last is >= '가' and <= '힣') return (last - '가') % 28 != 0;   // 28 받침 slots per syllable, 0 = none
        return last switch
        {
            '0' or '1' or '3' or '6' or '7' or '8' => true,   // 영 일 삼 육 칠 팔
            '2' or '4' or '5' or '9' => false,                 // 이 사 오 구
            _ => null,
        };
    }
}
