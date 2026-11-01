namespace GaugeOverlay.Core.Domain;

/// <summary>설정's undo (<see cref="CopyFrom"/>).</summary>
public sealed partial class Settings
{
    /// <summary>What an undo leaves as it is (<see cref="CopyFrom"/>): what the app writes on its own (detection, fits,
    /// learned stacks, first run, update checks), and the theme, a choice for the
    /// whole window seen at once.</summary>
    static readonly HashSet<string> AppWritten =
    [
        nameof(Theme),
        nameof(FirstRunDone), nameof(Updates), nameof(Characters), nameof(ActiveCharacter), nameof(Cores), nameof(StackSkills),
        nameof(SubclassHistory), nameof(SeenSupport), nameof(Fits), nameof(DetectedScreen), nameof(Adjusts),
    ];

    /// <summary>설정's undo: the live object, which the windows hold, takes <paramref name="other"/>'s values — every public
    /// read-write property, so a new setting needs nothing here — except what the app wrote meanwhile (<see cref="AppWritten"/>,
    /// and the party's 신뢰한 기기).</summary>
    public void CopyFrom(Settings other)
    {
        var trusted = Party.TrustedDevices;
        var learnedHp = Raids.Gates.Select(g => (Id: g.Key, g.Value.LearnedHp, g.Value.PendingHp)).ToList();   // HP 변경 감지
        var shots = Profiles.Select(p => p.Shot).ToList();   // a screenshot is a file, kept or removed at once: no undo step
        foreach (var p in typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0 && !AppWritten.Contains(p.Name) && !SavedForms.Contains(p.Name))
                p.SetValue(this, p.GetValue(other));
        Party.TrustedDevices = trusted;
        foreach (var g in Raids.Gates.Values) (g.LearnedHp, g.PendingHp) = (null, null);
        foreach (var (id, learned, pending) in learnedHp.Where(g => g.LearnedHp is not null || g.PendingHp is not null))
            (Raids.Gate(id).LearnedHp, Raids.Gate(id).PendingHp) = (learned, pending);
        for (int i = 0; i < Math.Min(shots.Count, Profiles.Count); i++) Profiles[i].Shot = shots[i];
    }

    /// <summary>Properties that only spell another one out for the file (<see cref="SavedTheme"/>, <see cref="Palette"/>:
    /// <see cref="Theme"/>): copied through it.</summary>
    static readonly HashSet<string> SavedForms = [nameof(SavedTheme), nameof(Palette)];
}
