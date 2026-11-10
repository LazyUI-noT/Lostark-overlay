using System.Collections.Immutable;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>감지 중 (Phase 5): each reader's ✓ ? ! from what this step already read (DetectBoard), into the snapshot.</summary>
public sealed partial class Engine
{
    ImmutableDictionary<string, Detection> _detect = DetectBoard.Empty;
    ImmutableDictionary<string, PixelRect> _detectAreas = ImmutableDictionary<string, PixelRect>.Empty;
    ImmutableDictionary<string, DetectPicture> _detectPictures = ImmutableDictionary<string, DetectPicture>.Empty;
    DetectPicture? _detectLivePicture;
    volatile string? _detectLiveId;
    TimeSpan _lastLive = TimeSpan.MinValue;
    /// <summary>Live: the one box, twice a second (pyosi-map.plan.md, Live's cost).</summary>
    static readonly TimeSpan LiveEvery = TimeSpan.FromMilliseconds(500);
    readonly List<(string Id, bool Live)> _pictureWanted = [];
    bool _pictureTaken;

    volatile FrameGaps? _gaps;
    /// <summary>리소스 검사's frame-gap check (null: off): each captured frame's time goes in with the parts that ran since the last one.</summary>
    public void WatchFrames(FrameGaps? gaps) => _gaps = gaps;

    /// <summary>The 감지 page's Live box (null: off, also when paused or the page closes). From the UI thread.</summary>
    public void DetectLive(string? id) => _detectLiveId = id;

    /// <summary>The pictures StepDetect asked for, each reader's area plus its margin: they ride on the next step's capture (a
    /// capture of their own would wait for a new frame and take it from the reads).</summary>
    List<(string Id, PixelRect Area, bool Live)> PictureRects()
    {
        var rects = _pictureWanted.Select(w => (w.Id, Area: _detectAreas.TryGetValue(w.Id, out var a) ? DetectBoard.Around(a, _vp) : null, w.Live))
            .Where(w => w.Area is not null).Select(w => (w.Id, w.Area!.Value, w.Live)).ToList();
        _pictureWanted.Clear();
        return rects;
    }

    /// <summary>No frame this step: a mark's moment is asked for again (Live asks on its own).</summary>
    void PicturesMissed(List<(string Id, PixelRect Area, bool Live)> wanted) =>
        _pictureWanted.AddRange(wanted.Where(w => !w.Live).Select(w => (w.Id, false)));

    /// <summary>Keeps the captured pictures (their own images, not the reused buffers).</summary>
    void TakePictures(List<(string Id, PixelRect Area, bool Live)> wanted, List<Image> images, int at, TimeSpan t)
    {
        for (int i = 0; i < wanted.Count; i++)
        {
            int k = i;
            if (wanted[k].Live) Perf.Run("감지 Live", () => Keep(k));   // timed for 리소스 검사's Live row
            else Keep(k);
        }

        void Keep(int i)
        {
            var img = images[at + i];
            for (int b = 3; b < img.Bgra.Length; b += 4) img.Bgra[b] = 255;   // the desktop's alpha is undefined
            var p = new DetectPicture(img, wanted[i].Area, t, wanted[i].Id);
            if (wanted[i].Live) _detectLivePicture = _detectLiveId == wanted[i].Id ? p : null;
            else
            {
                _detectPictures = _detectPictures.SetItem(wanted[i].Id, p);
                System.Diagnostics.Trace.WriteLine($"[detect] picture {wanted[i].Id} {img.Width}x{img.Height}");
            }
            _pictureTaken = true;
        }
    }

    /// <summary>Where each reader looks now (screen pixels), for the 감지 page: the areas the reads already use.</summary>
    ImmutableDictionary<string, PixelRect> Areas(Config config)
    {
        var areas = ImmutableDictionary.CreateBuilder<string, PixelRect>();
        if (_fit is not { } fit) return areas.ToImmutable();
        var slots = SlotInfo.All.Select(s => Geometry.Slot(s, _vp, fit)).ToList();
        int left = slots.Min(r => r.X), top = slots.Min(r => r.Y);
        areas["skill"] = new PixelRect(left, top, slots.Max(r => r.X + r.Width) - left, slots.Max(r => r.Y + r.Height) - top);
        areas["buff"] = Geometry.BuffArea(_vp, fit, config.DebuffSize, config.BuffCount);
        areas[DetectPlace.DebuffRow] = Geometry.DebuffArea(_vp, fit, config.DebuffSize);   // no map box: 위치 조정 moves it, the table reads it
        areas["boss"] = Geometry.BossTextRow(_vp, fit);
        areas["amsu"] = Geometry.BossDebuffStrip(_vp, fit, config.BossSize);
        areas["judg"] = Geometry.JudgmentBand(_vp);
        if (ShownPanel(config) is { } panel) areas["g-jeonbun"] = panel;
        double s = Geometry.Scale(_vp, fit);
        areas["dungeon"] = new PixelRect(_vp.X, _vp.Y + (int)(RefDungeonPanel.Y * s), (int)(RefDungeonPanel.Width * s), (int)(RefDungeonPanel.Height * s));
        areas["char"] = Geometry.MenuArea(_vp, Geometry.RefMenuPanel);   // the ESC 게임 메뉴's panel, read while it is open
        return areas.ToImmutable();
    }
    /// <summary>The 전분 box the 감지 page draws: the session's while placing, else the spot in use, else with the meter on the usual spot, so
    /// the page can pick the box and place it. Null: the meter is off with no spot, nothing to show.</summary>
    PixelRect? ShownPanel(Config config) => _previewPanel ?? PanelSpot(config) ?? (_meterOn ? Geometry.UsualPanel(_vp) : null);

    (Viewport, Fit?) _detectLayout;
    /// <summary>The menu is read about 1/s while open: a name this recent still counts as finding.</summary>
    static readonly TimeSpan CharacterNamedFor = TimeSpan.FromSeconds(2.5);
    object? _areasKey;

    /// <returns>Publish: a mark changed.</returns>
    bool StepDetect(Config config, TimeSpan t)
    {
        // Another window, size or fit: start over. Not while 위치 조정 moves the fit: the pictures a box is placed on stay.
        var layout = _previewing && _detectLayout.Item1 == _vp ? _detectLayout : (_vp, _fit);
        if (layout != _detectLayout)
        {
            (_detectLayout, _detect, _detectPictures, _detectLivePicture) = (layout, DetectBoard.Empty, ImmutableDictionary<string, DetectPicture>.Empty, null);
            _pictureWanted.Clear();
        }
        var on = new HashSet<string>();
        if (!config.Reads.VoteOnly) on.Add("skill");
        if (_buffsOn) on.Add("buff");
        if (_gaugeOn) on.Add("aden");
        if (_meterOn) { on.Add("g-jeonbun"); on.Add("dungeon"); }
        if (_meterOn || BossHpPlan.Wanted(config.Raids, config.RaidGates ?? [], config.HomeworkRead)) on.Add("boss");
        if (_amsuOn) on.Add("amsu");
        if (_judgmentOn) on.Add("judg");
        if (Ocr.Value is not null) on.Add("char");   // the game menu read has no switch: on wherever Windows OCR is
        // Without a fit the skill reader already fails at once and there is no strip, so 암수 isn't failed for size too.
        var inputs = new DetectInputs(Seen, _fit is not null, _hud.Visible, _buffs.Any(b => b.Active), _gauge is not null,
            _presence.InDungeon(t), _presence.BossUp(t), _meter.PanelFound, _bossHp.View is not null, _dungeon is not null,
            _fit is not { } fit || Amsu.Readable(_vp, fit, config.BossSize), _amsu.Up.Count > 0, _judgment.IconSeen,
            Interlocked.Read(ref _menuNamedTicks) is var named && named > 0 && t - TimeSpan.FromTicks(named) <= CharacterNamedFor);
        var next = DetectBoard.Step(_detect, inputs, on, t);
        bool changed = next.Count != _detect.Count || next.Any(kv => _detect.GetValueOrDefault(kv.Key)?.Mark != kv.Value.Mark);
        foreach (var id in DetectBoard.Moments(_detect, next)) _pictureWanted.Add((id, false));   // the moment a mark turns, next step
        _detectPictures = _detectPictures.RemoveRange(_detectPictures.Keys.Where(id => !next.ContainsKey(id)).ToList());   // switched off
        if (_detectLiveId is not { } live) { changed |= _detectLivePicture is not null; _detectLivePicture = null; }
        else if (Seen && Elapsed(t, _lastLive, LiveEvery)) { _lastLive = t; _pictureWanted.Add((live, true)); }
        changed |= _pictureTaken;
        _pictureTaken = false;
        foreach (var (id, d) in next)
            if (_detect.GetValueOrDefault(id)?.Mark != d.Mark && d.Mark != DetectMark.Waiting)
                System.Diagnostics.Trace.WriteLine($"[detect] {id} {(d.Mark == DetectMark.Concrete ? "✓" : "!")} at {t.TotalSeconds:0.0}s{(d.Why is { } why ? $": {why}" : "")}");
        _detect = next;
        var areasKey = (_vp, _fit, config.DebuffSize, config.BuffCount, config.BossSize, ShownPanel(config));
        if (areasKey.Equals(_areasKey)) return changed;
        (_areasKey, _detectAreas) = (areasKey, Areas(config));   // only when the layout moves
        return true;
    }
}
