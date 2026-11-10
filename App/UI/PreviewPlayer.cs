using System.Windows.Threading;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>▶ 실행해 보기 for a preview's renderers: stopped, they show the sample frozen; running, a <see cref="PreviewSim"/>
/// drives them, and a clicked box acts as if used (<see cref="Click"/>).</summary>
sealed class PreviewPlayer
{
    readonly OverlayRenderer[] _targets;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    Player? _sample;
    PreviewSim? _sim;

    public PreviewPlayer(params OverlayRenderer[] targets)
    {
        _targets = targets;
        _timer.Tick += (_, _) => Push();
    }

    public bool Running => _sim is not null;

    /// <summary>How often the sample is pushed to the boxes (50 ms; the 설정 → 표시 scene slower).</summary>
    public TimeSpan Every { get => _timer.Interval; set => _timer.Interval = value; }

    /// <summary>The sample for the class shown; a running play starts over with it.</summary>
    public void Show(Player sample)
    {
        _sample = sample;
        if (_sim is null) Freeze();
        else Start();
    }

    public void Start()
    {
        if (_sample is null) return;
        _sim = new PreviewSim(_sample.Snapshot, Clock.Now());
        _timer.Start();
        Push();
    }

    public void Stop()
    {
        _timer.Stop();
        _sim = null;
        Freeze();
    }

    /// <summary>초기화: every box ready, the buffs off, the gauge empty.</summary>
    public void Reset()
    {
        _sim?.Reset(Clock.Now());
        if (_sim is not null) Push();
    }

    /// <summary>While running: <paramref name="part"/> acts as if used (the gauge as <paramref name="gauge"/> says).</summary>
    public void Click(Part part, GaugePlay gauge)
    {
        if (_sim is null) return;
        _sim.Click(part, Clock.Now(), gauge);
        Push();
    }

    /// <summary>While running: a looping window (디버프, 단심, 암수) jumps to its next phase (<see cref="PreviewSim.Skip"/>).</summary>
    public void Skip(OverlayPart window)
    {
        if (_sim is null) return;
        _sim.Skip(window, Clock.Now());
        Push();
    }

    /// <summary>자동: the boxes are used in turn without clicks (<see cref="PreviewSim.Auto"/>); the value gives the gauge's
    /// section count. Null: only clicks use them.</summary>
    public Func<GaugePlay>? Auto { get; set; }

    void Push()
    {
        if (_sim is null) return;
        var now = Clock.Now();
        if (Auto is { } gauge) _sim.Auto(now, gauge());
        var p = new Player("", _sim.At(now));
        foreach (var t in _targets) t.Update(p);
    }

    void Freeze()
    {
        if (_sample is null) return;
        foreach (var t in _targets) t.Update(_sample, _sample.Snapshot.At);
    }

    /// <summary>Sample boxes for the active class, or for <paramref name="target"/> (the 직업각인 picked in 설정): its class (for the
    /// gauge's splits and glow) and 아덴 slots.</summary>
    public static Player SampleFor(CharacterService c, PreviewTarget? target = null)
    {
        var (self, _) = DemoSnapshots.Sample(Clock.Now(), target?.AdenSlots ?? c.AdenSlots);
        return new Player(self.Name, self.Snapshot with { ClassId = target?.ClassId ?? c.ClassId });
    }

    /// <summary>How the active class's (or <paramref name="target"/>'s) gauge fills and is spent: a click fills one section.</summary>
    public static GaugePlay Gauge(Settings s, CharacterService c, PreviewTarget? target = null)
    {
        var (cls, sub) = target is not null ? (c.Classes.FirstOrDefault(k => k.Name == target.Class), target.Subclass) : (c.ActiveClass, c.Active?.Subclass);
        if (cls is null || sub is null) return GaugePlay.Plain;
        var rule = cls.Subclasses.GetValueOrDefault(sub);
        return new(GaugeBar.Resolve(s, cls.Name, sub, rule).Splits, rule);
    }
}
