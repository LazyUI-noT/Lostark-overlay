using System.Diagnostics;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>DPS 미터기 (project C): once a second while it is on, the boss HP text ("current/max") and the 전분 panel at its spot
/// for this game-area size; <see cref="MeterTracker"/> keeps the record and the boss damage. Nothing while it is off.
/// 전분 위치 찾기: with no spot, or one that hasn't read for <see cref="SearchAfterMisses"/> seconds (the panel moved, a first run),
/// the whole game area is searched every <see cref="SearchEvery"/> (<see cref="JeonbunFinder"/>); a spot found is used at once
/// and handed to the app to keep (<see cref="MeterPanelFound"/>).</summary>
public sealed partial class Engine
{
    static readonly TimeSpan MeterEvery = TimeSpan.FromSeconds(1);   // 전분 updates once a second
    static readonly TimeSpan SearchEvery = TimeSpan.FromSeconds(3);
    const int SearchAfterMisses = 3;
    MeterStatus _meter = MeterStatus.None;
    bool _meterOn;
    TimeSpan _lastMeter = TimeSpan.MinValue, _lastSearch = TimeSpan.MinValue;
    int _panelMisses;
    (string Key, PixelRect Rect)? _foundPanel;   // found here, until the settings carry it

    /// <summary>A 전분 spot found for a game-area size (the key of MeterPanels); on the engine thread.</summary>
    public event Action<string, PixelRect>? MeterPanelFound;

    /// <summary>What to capture for the meter this step: the HP text and the 전분 panel (null: no spot for this size).</summary>
    (PixelRect? Hp, PixelRect? Panel) MeterRects(Config config, TimeSpan now)
    {
        if (config.MeterOn != _meterOn)
        {
            (_meterOn, _meter, _panelMisses) = (config.MeterOn, MeterStatus.None, 0);
            Publish(now);
        }
        // The dungeon panel: for the meter's records, and for 자동 인식 (the boss name is read as text only in a raid it names).
        if ((_meterOn || _raidWaits) && StepDungeon(now)) Publish(now);
        if (!_meterOn || !Elapsed(now, _lastMeter, MeterEvery)) return (null, null);
        var panel = PanelSpot(config);
        if ((panel is null || _panelMisses >= SearchAfterMisses) && Elapsed(now, _lastSearch, SearchEvery)) panel = Perf.Run("전분 위치 찾기", () => SearchPanel(now)) ?? panel;
        return (Geometry.BossTextRow(_vp, _fit!), panel);   // the bar's whole text row: the HP wherever 21:9 or the HUD puts it
    }

    /// <summary>The spot for this game-area size: one just found, else the saved one. A spot outside the game area would fail
    /// the shared capture (and every other read with it): left out.</summary>
    PixelRect? PanelSpot(Config config)
    {
        string key = Geometry.ViewportKey(_vp);
        PixelRect? p = _foundPanel is { } f && f.Key == key ? f.Rect
            : config.MeterPanels?.TryGetValue(key, out var saved) == true ? saved : null;
        return p is { } r && r.Inside(_vp) ? r : null;
    }

    /// <summary>Looks for the panel over the whole game area, at the 1080p size and the one for this screen's height.</summary>
    PixelRect? SearchPanel(TimeSpan now)
    {
        _lastSearch = now;
        var area = new PixelRect(_vp.X, _vp.Y, _vp.Width, _vp.Height);
        var img = new Image(area.Width, area.Height);
        if (!_source!.TryCapture([area], [img], TimeSpan.FromMilliseconds(100), out _)) { Trace.WriteLine("[meter] 전분 search: no frame"); return null; }
        foreach (double scale in new[] { 1.0, _vp.Height / 1080.0 }.Distinct())
            if (JeonbunFinder.Find(img, scale) is { } r)
            {
                var spot = new PixelRect(area.X + r.X, area.Y + r.Y, r.Width, r.Height);
                string key = Geometry.ViewportKey(_vp);
                (_foundPanel, _panelMisses) = ((key, spot), 0);
                Trace.WriteLine($"[meter] 전분 found at {spot} ({key}, scale {scale:0.##})");
                MeterPanelFound?.Invoke(key, spot);
                return spot;
            }
        Trace.WriteLine($"[meter] 전분 search: not on screen ({area})");
        return null;
    }

    /// <summary>Reads the captured HP text and panel (null: no spot) and steps the meter. True: publish.</summary>
    bool ReadMeter(Image hp, Image? panel, TimeSpan t)
    {
        _lastMeter = t;
        double scale = Geometry.Scale(_vp, _fit!);
        var reading = panel is null ? null : JeonbunReader.Read(panel);
        _panelMisses = reading is null ? _panelMisses + 1 : 0;
        var read = BossDigits.ReadBar(hp, scale).Hp;   // a shield in brackets counts as HP (its breaking is damage)
        var next = MeterTracker.Step(_meter, read is var (cur, max, _) ? (cur, max) : null, reading, reading is not null, t, read?.Shield ?? 0);
        bool changed = next != _meter;
        _meter = next;
        return changed;
    }
}
