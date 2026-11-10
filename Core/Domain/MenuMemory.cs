namespace GaugeOverlay.Core.Domain;

/// <summary>What the game menu looked like, by pixels: the title, and each card's name strip with what the OCR read there. The
/// menu never moves and its cards sit on a fixed grid (user, 2026-11-04), so a strip seen once needs no OCR again. For one screen
/// size (Clear on another); in memory only. Read by the engine thread and filled from the OCR's: locked.</summary>
public sealed class MenuMemory
{
    /// <summary>Mean grey difference per pixel: the same strip ≤ 2.1, another ≥ 6.7 (goal.png, a live 1080p capture, homework_tracker.mp4).</summary>
    public const double SameStrip = 4;
    /// <summary>A pixel this much greyer or lighter changed for real: between frames of the same strip none do (homework_minor.mp4).</summary>
    const int Changed = 40;
    /// <summary>At most this share of a strip's pixels changed, or it is another strip. Names a letter apart (너무아파요살살,
    /// 너무아파용살살: 94 of 4180, mean 3.1) pass <see cref="SameStrip"/>, and one card stood for both (user, 2026-11-08).</summary>
    const double MaxChangedShare = 0.005;
    /// <summary>The title open ≤ 3.2, closed or covered ≥ 25 (the same).</summary>
    public const double SameTitle = 8;
    /// <summary>Plenty for a roster and its hover looks; a character's strips come back on the next read.</summary>
    public const int MaxStrips = 64;

    readonly object _gate = new();
    readonly List<(byte[] Strip, MenuCard Card, bool Playing)> _strips = [];
    byte[]? _title;

    public int Count { get { lock (_gate) return _strips.Count; } }

    public bool TitleKnown { get { lock (_gate) return _title is not null; } }

    public void Clear()
    {
        lock (_gate) { _strips.Clear(); _title = null; }
    }

    public void LearnTitle(byte[] title)
    {
        lock (_gate) _title = title;
    }

    public bool IsTitle(byte[] title)
    {
        lock (_gate) return _title is not null && Difference(_title, title) <= SameTitle;
    }

    /// <summary>Keeps what was read at this strip; a later read of the same strip replaces it, so a misread doesn't stay.</summary>
    public void Learn(byte[] strip, MenuCard card, bool playing)
    {
        lock (_gate)
        {
            _strips.RemoveAll(s => SameAs(s.Strip, strip));
            if (_strips.Count >= MaxStrips) _strips.RemoveAt(0);
            _strips.Add((strip, card, playing));
        }
    }

    public (MenuCard Card, bool Playing)? Recall(byte[] strip)
    {
        lock (_gate)
        {
            foreach (var s in _strips)
                if (SameAs(s.Strip, strip)) return (s.Card, s.Playing);
            return null;
        }
    }

    static bool SameAs(byte[] a, byte[] b)
    {
        if (Difference(a, b) > SameStrip) return false;
        int changed = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i] - b[i]) > Changed) changed++;
        return changed <= a.Length * MaxChangedShare;
    }

    static double Difference(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return double.MaxValue;
        long sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return (double)sum / a.Length;
    }
}

/// <summary>When the open menu's panel is read by OCR. Cards known by their pixels need no read, so it runs only to meet new ones and
/// to hear who is played: after a change on screen (twice: names count after two alike reads) and every <see cref="Quiet"/> for
/// "잠시만 기다려주세요." (stays ~4 s). A screen that never settles falls back to once a second. Pure; the engine thread's.</summary>
public sealed class MenuPace
{
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150), MinGap = TimeSpan.FromMilliseconds(500), Quiet = TimeSpan.FromSeconds(3);
    static readonly TimeSpan Busy = TimeSpan.FromSeconds(1);
    const int ReadsAfterChange = 2;

    int _left;
    TimeSpan? _lastRead;
    TimeSpan _lastChange, _movingSince;

    public void Open(TimeSpan now) => (_left, _lastRead, _lastChange, _movingSince) = (ReadsAfterChange, null, now - Settle, now - Settle);

    public void Changed(TimeSpan now)
    {
        if (now - _lastChange >= Settle) _movingSince = now;
        (_left, _lastChange) = (ReadsAfterChange, now);
    }

    public void Read(TimeSpan now)
    {
        _lastRead = now;
        if (_left > 0) _left--;
    }

    public bool Due(TimeSpan now)
    {
        if (_lastRead is not { } last) return true;
        var gap = now - last;
        if (_left == 0) return gap >= Quiet;
        return now - _lastChange >= Settle ? gap >= MinGap : gap >= Busy && now - _movingSince >= Busy;
    }
}
