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
        nameof(SubclassHistory), nameof(SeenSupport), nameof(RailSeen), nameof(Fits), nameof(DetectedScreen), nameof(Adjusts),
        nameof(Homework),   // characters read from the game and picks raised by clears
        nameof(BuffUnlockKey),   // the install's, not an option: undo and [Test] never relock it
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
        // The window spot came with other's character (PlaceKey); the active character is the live one: put its spot on,
        // or leaving 테스트 모드 saved "no character" with a 직업각인's spot on screen, put to 기본's at the next start.
        SyncPlace();
    }

    /// <summary>자동's choice. 던전 needs the dungeon read, which is the meter's: turned on if off, without its window.</summary>
    public void ChooseOverlayAuto(OverlayAuto auto)
    {
        Overlay.SetAuto(auto);
        if (auto == OverlayAuto.던전 && !Meter.On) Meter.SetMode(ThreeWay.표시안함);
    }

    /// <summary>[Test]: the live object takes <paramref name="other"/>'s options and places, as an undo does, except what
    /// belongs to the install: 표시 화면 and 창 밖에서도 읽기, and the hotkeys (the one that leaves 테스트 모드 among them).</summary>
    public void SwapIn(Settings other)
    {
        var (screens, hotkeys) = (Screens, Hotkeys);
        CopyFrom(other);
        (Screens, Hotkeys) = (screens, hotkeys);
    }

    /// <summary>Properties that only spell another one out for the file (<see cref="SavedTheme"/>, <see cref="Palette"/>:
    /// <see cref="Theme"/>): copied through it.</summary>
    static readonly HashSet<string> SavedForms = [nameof(SavedTheme), nameof(Palette)];
}
