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

    /// <summary>While running: <paramref name="part"/> acts as if used (the gauge fills one of <paramref name="sections"/>).</summary>
    public void Click(Part part, int sections)
    {
        if (_sim is null) return;
        _sim.Click(part, Clock.Now(), sections);
        Push();
    }

    void Push()
    {
        if (_sim is null) return;
        var p = new Player("", _sim.At(Clock.Now()));
        foreach (var t in _targets) t.Update(p);
    }

    void Freeze()
    {
        if (_sample is null) return;
        foreach (var t in _targets) t.Update(_sample, _sample.Snapshot.At);
    }

    /// <summary>Sample boxes for the active class: its class (for the gauge's splits and glow) and 아덴 slots.</summary>
    public static Player SampleFor(CharacterService c)
    {
        var (self, _) = DemoSnapshots.Sample(Clock.Now(), c.AdenSlots);
        return new Player(self.Name, self.Snapshot with { ClassId = c.ClassId });
    }

    /// <summary>How many sections the active class's gauge has: a click fills one.</summary>
    public static int Sections(Settings s, CharacterService c) =>
        c.Active is { } a && c.ActiveClass is { } k ? GaugeBar.Resolve(s, a.Class, a.Subclass, k.Subclasses.GetValueOrDefault(a.Subclass)).Splits.Count + 1 : 1;
}
