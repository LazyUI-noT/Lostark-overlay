using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>설정 → 레이드 (project B): the boss's name above its HP bar picks the gate (자동 인식), and the bar count ("X n") feeds
/// the 줄 수 창 and its alerts. Each is read only while a window needs it (RaidReadPlan).</summary>
public sealed partial class Engine
{
    static readonly TimeSpan NameEvery = TimeSpan.FromSeconds(1);   // a gate changes only between fights
    RaidRecognitionState _raidRec = RaidRecognitionState.None;
    BarCountState _bar = BarCountState.None;
    string? _barGate;   // the gate _bar counts for
    RaidStatus? _raid;
    TimeSpan _lastName = TimeSpan.MinValue;
    HpAnchor? _hpAnchor;
    long? _lastMaxHp;   // the max HP the previous name step read
    HpLearner _hpLearn = HpLearner.None;   // HP 변경 감지: the values this raid uses (HpChange)
    bool _raidWaits;   // a window waits for 자동 인식: the dungeon panel is read for it too

    /// <summary>The gate 자동 인식 recognises changed (its id, null: none); on the engine thread.</summary>
    public event Action<string?>? RaidRecognised;
    /// <summary>HP 변경 감지: the values this raid uses changed (a new one, or cleared on leaving the raid); on the engine thread.</summary>
    public event Action<IReadOnlyList<FightHp>>? FightHpChanged;

    /// <summary>What to capture for 설정 → 레이드 this step: the name strip (once a second while a window on 자동 waits for it) and
    /// the count (every poll while the 줄 수 창 shows). A 줄 수 창 no longer shown is cleared here, since nothing is read for it.</summary>
    (PixelRect? Name, PixelRect? Count, PixelRect? Hp) RaidRects(Config config, bool poll, TimeSpan now)
    {
        var plan = config.Raids is { } s ? RaidReadPlan.Of(s, config.RaidGates ?? [], _raidRec.Gate) : default;
        _raidWaits = plan.ReadName;
        if (!plan.ReadCount && _raid is not null) { _raid = null; Publish(now); }
        if (!poll) return (null, null, null);
        // No boss to name outside a dungeon; the HP label (보스 체력 쉽게 보기) wants the name's end wherever the bar's HP is read.
        bool name = (plan.ReadName && _presence.InDungeon(now) || LabelWantsName(config)) && Elapsed(now, _lastName, NameEvery);
        // The boss's max HP (raids.json "hp") recognises a gate exactly, with its difficulty: read with the name. Both the count
        // and the HP are read from the bar's whole text row (BossDigits.ReadBar), wherever a 21:9 or HUD size puts them.
        var row = Geometry.BossTextRow(_vp, _fit!);
        bool count = plan.ReadCount && _presence.InDungeon(now);   // 줄 수 only in a dungeon; cleared on leaving (StepPresence)
        return (name ? Geometry.BossName(_vp, _fit!) : null, count ? row : null, name ? row : null);
    }

    /// <summary>The HP label is on and the bar's HP is read: it is placed after the name's end and shown only with a name
    /// (boss-label-name.plan.md).</summary>
    bool LabelWantsName(Config config) => config.Raids is { BossHpEasy: true, BossHpRead: true } && _bossHp.View is not null;

    /// <summary>How long the last name end places the label: a name read every second, so a few missed steps keep it.</summary>
    static readonly TimeSpan NameEndFor = TimeSpan.FromSeconds(2.5);
    int? _nameRight;   // the name's red text ends here (screen px), null: no name in the last read
    TimeSpan _nameRightAt = TimeSpan.MinValue;

    /// <summary>The boss HP label after the name, or null with no name read lately (cutscene, HUD hidden, 대난투, before the first
    /// read): it never sits where the name is read.</summary>
    BossHpView? LabelBesideName(TimeSpan t) =>
        _bossHp.View is { } v && _nameRight is { } right && t - _nameRightAt <= NameEndFor
            ? v with { Label = Geometry.BossHpLabelAfter(_vp, _fit!, right) } : null;

    /// <summary>HP 변경 감지's picture: the HP text row each value new to this raid's list was read from. The first read is kept
    /// (an existing file is never replaced); HpCrops drops it once its row leaves the list.</summary>
    void SaveHpCrops(Image row, IReadOnlyList<FightHp> before)
    {
        foreach (var f in _hpLearn.Fight.Where(f => !before.Contains(f)))
        {
            string file = HpCrops.PathOf(f.GateId, f.Difficulty, f.Hp);
            if (File.Exists(file)) continue;
            try
            {
                Directory.CreateDirectory(HpCrops.Dir);
                string part = file + ".part";   // a failed write never leaves a broken file that File.Exists would keep forever
                Platform.Png.Save(part, row);
                File.Move(part, file, overwrite: true);
            }
            catch (Exception e) { Trace.WriteLine($"[raid] HP crop not saved: {e.Message}"); }   // a picture is never worth the read
        }
    }

    /// <summary>The debuff window's raid this step: the gate's 디버프 창 (RaidReadPlan.DebuffRaid).</summary>
    Raid DebuffRaid(Config config, TimeSpan now) =>
        config.Raids is { } s ? RaidReadPlan.DebuffRaid(s, config.RaidGates ?? [], _raidRec.Gate, _presence.InDungeon(now), config.DebuffPick) : Raid.없음;

    /// <summary>Reads the captured name and count (either may be null) and moves recognition and the alerts. True when the
    /// 줄 수 창 changed.</summary>
    bool ReadRaid(Config config, Image? name, Image? count, Image? hp, TimeSpan t)
    {
        var s = config.Raids!;
        var gates = HpChange.Effective(config.RaidGates ?? [], s, _hpLearn.Fight);
        double scale = Geometry.Scale(_vp, _fit!);
        bool moved = false;
        if (name is not null)
        {
            _lastName = t;
            var end = BossName.TextEnd(name, scale) is { } e ? Geometry.BossName(_vp, _fit!).X + e : (int?)null;
            (moved, _nameRight, _nameRightAt) = (end != _nameRight, end, t);
        }
        if (name is not null && _raidWaits && _presence.InDungeon(t))   // recognition: a read for the label alone skips it
        {
            long? maxHp = hp is null ? null : BossBar(hp).Hp?.Max;
            if (maxHp is not null) Saw(meter: false, boss: true, t);   // the HP text read here is the boss bar too
            var byHp = RaidData.ByHp(gates, maxHp);
            // A boss the name read (OCR) found stays found while its max HP reads the same, with a check read every 30 s
            // instead of every 4 s (HpAnchor). Out of a raid the anchor is dropped.
            if (_dungeon is not { Kind: DungeonKind.Raid }) _hpAnchor = null;
            var anchored = HpAnchor.Match(_hpAnchor, maxHp);
            if (!_presence.BossUp(t)) Volatile.Write(ref _namePace, NameOcrPace.Start);   // a new pull or boss reads every second again
            string? ocr = null;
            string? byName = byHp is null ? BossName.Match(name, scale, config.BossNames ?? [])
                ?? (ocr = _presence.BossUp(t) ? NameByOcr(name, s, gates, scale, t, anchored is not null, maxHp) : null) : null;   // Windows OCR waits for the boss bar: none in town or the field
            var matched = byHp?.Gate.Id ?? byName ?? anchored;
            var was = _raidRec.Gate;
            _raidRec = RaidRecognition.Update(_raidRec, matched, t, daenantu: _shadow is not null);   // 대난투 shows no boss name
            if (byHp is null) _hpAnchor = HpAnchor.Next(_hpAnchor, _raidRec.Gate, ocr, maxHp, _lastMaxHp);
            _lastMaxHp = maxHp;
            var learned = _hpLearn.Fight;
            _hpLearn = _hpLearn.Step(config.RaidGates ?? [], s, _raidRec.Gate, maxHp, byName, _dungeon);
            if (!ReferenceEquals(learned, _hpLearn.Fight))
            {
                Trace.WriteLine("[raid] HP change: " + (_hpLearn.Fight.LastOrDefault() is { } f
                    ? $"{f.GateId} [{f.Difficulty}] {f.Hp} ({f.Trust})" : "cleared"));
                if (hp is not null) SaveHpCrops(hp, learned);
                FightHpChanged?.Invoke(_hpLearn.Fight);
            }
            if (_raidRec.Gate != was)
            {
                Trace.WriteLine($"[raid] recognised {_raidRec.Gate ?? "none"}" + (byHp is { } h ? $" by HP ({h.Difficulty})" : ""));
                RaidRecognised?.Invoke(_raidRec.Gate);
            }
            if (config.Debug) ExtraDebug?.Invoke(new("보스 이름", name.Copy(), matched ?? "없음", _raidRec.Gate ?? "인식 안 됨"));
        }
        var plan = RaidReadPlan.Of(s, gates, _raidRec.Gate);
        RaidStatus? next = null;
        if (plan is { ReadCount: true, Gate: { } gate })
        {
            if (gate.Id != _barGate) (_barGate, _bar) = (gate.Id, BarCountState.None);   // another gate: its own count
            if (count is not null)
            {
                var bar = BossBar(count, hp: false);
                if (bar.Count is not null) Saw(meter: false, boss: true, t);   // "X n" read: the boss bar is up
                _bar = BarAlert.Update(_bar, ShadowRead.BarLines(bar.Count, _shadow), t, BarAlert.Thresholds(s.AlertsOf(gate), s.OffsetOf(gate.Id)));
                if (config.Debug) ExtraDebug?.Invoke(new("줄 수", count.Copy(), bar.Count?.ToString() ?? "?", _bar.Shown?.ToString() ?? "?"));
            }
            bool glow = s.Glow && BarAlert.Glowing(_bar, s.GlowUntil, s.GlowSeconds, s.GlowBars, t);
            next = s.Shown(new(gate, _bar.Shown, glow));   // 빛날 때만 표시 hides it, the count above keeps tracking
        }
        bool changed = next != _raid || moved;
        _raid = next;
        return changed;
    }

    /// <summary>Once a gate is recognised by OCR, its name is read again this often to keep it (RaidRecognition.Keep is 10 s).</summary>
    static readonly TimeSpan NameOcrKeepEvery = TimeSpan.FromSeconds(4);
    /// <summary>While the max HP holds the gate (HpAnchor), the name is read this often to check it.</summary>
    static readonly TimeSpan NameOcrCheckEvery = TimeSpan.FromSeconds(30);

    /// <summary>The boss name read as text (Windows OCR, on the thread pool) when neither the HP nor a name template knows the
    /// boss: any gate in raids.json, at any resolution or mode. Each finished read is handed out once (so one read can't count as
    /// the two that RaidRecognition needs), and a new one starts: every name read (1 s) while nothing is recognised, slower while
    /// the same boss keeps missing (<see cref="NameOcrPace"/>), every <see cref="NameOcrKeepEvery"/> once something is.</summary>
    string? NameByOcr(Image name, RaidSettings s, IReadOnlyList<RaidGate> gates, double scale, TimeSpan t, bool anchored, long? maxHp)
    {
        var done = Interlocked.Exchange(ref _nameOcr, null);
        // Only inside a raid: the top-left dungeon panel names one (not in town, not between fights for long).
        var panel = _dungeon;
        // Held by the max HP: a check every NameOcrCheckEvery. Recognised: every NameOcrKeepEvery, but again a second later
        // after a garbled read (two in a row at 4 s outlast Keep).
        bool due = panel is { Kind: DungeonKind.Raid } && (anchored ? Elapsed(t, _lastNameOcr, NameOcrCheckEvery)
            : _raidRec.Gate is null || Volatile.Read(ref _nameOcrMissed)
                ? Elapsed(t, _lastNameOcr, Volatile.Read(ref _namePace).Every(maxHp) - NameEvery)   // the name is read every NameEvery
                : Elapsed(t, _lastNameOcr, NameOcrKeepEvery));
        // Only the red name, never the whole strip (the app's own labels with 방송·캡처에서 숨김 off); no red name, no read.
        if (due && Ocr.Value is not null && BossName.OcrStrip(name, scale) is { } copy
            && Interlocked.CompareExchange(ref _nameOcrBusy, 1, 0) == 0)
        {
            _lastNameOcr = t;
            // The panel's raid and difficulty narrow the gates (종막 2-1 vs EX 2-1); a boss still of two goes to the one with a window on.
            var candidates = BossNameText.Candidates(gates, panel);
            var on = (s.CountEnabled ? gates.Select(g => g.Id) : [])
                .Concat(s.DebuffsAuto ? gates.Where(g => g.DebuffRaid != Raid.없음).Select(g => g.Id) : []).ToHashSet();
            _ = Task.Run(() => ReadNameOcr(copy, candidates, on, scale, t, maxHp));
        }
        return done is { } r && t - r.At <= 2 * NameEvery ? r.Gate : null;
    }

    sealed record NameOcrRead(string? Gate, TimeSpan At);
    NameOcrRead? _nameOcr;
    int _nameOcrBusy;
    bool _nameOcrMissed;   // the last read gave no gate
    NameOcrPace _namePace = NameOcrPace.Start;   // written by the pool read (one at a time: _nameOcrBusy)
    TimeSpan _lastNameOcr = TimeSpan.MinValue;
    string? _nameOcrGate = "";   // the last result logged ("": none yet)

    async Task ReadNameOcr(Image img, IReadOnlyList<RaidGate> gates, IReadOnlyCollection<string> prefer, double scale, TimeSpan at, long? maxHp)
    {
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            // Small Hangul reads far better enlarged: at least 2×, more for a capture smaller than 1080p's.
            var big = ImageOps.Resample(img, new PixelRect(0, 0, img.Width, img.Height), Math.Clamp(2 / scale, 2, 4));
            using var bmp = SoftwareBitmap.CreateCopyFromBuffer(big.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, big.Width, big.Height, BitmapAlphaMode.Premultiplied);
            var lines = await RecognizeLines(bmp, "OCR 보스 이름");
            var gate = BossNameText.Match(lines, gates, prefer);
            Volatile.Write(ref _nameOcr, new NameOcrRead(gate, at));
            Volatile.Write(ref _nameOcrMissed, gate is null);
            Volatile.Write(ref _namePace, _namePace.Read(gate is null, maxHp));
            if (gate != _nameOcrGate)   // logged when the answer changes, not on every read
            {
                _nameOcrGate = gate;
                Trace.WriteLine($"[raid] name read \"{string.Join(" / ", lines)}\" → {gate ?? "no gate"}");
            }
        }
        catch (Exception ex) { Trace.WriteLine($"[raid] name OCR failed: {ex.Message}"); }
        finally { Volatile.Write(ref _nameOcrBusy, 0); Perf.Add("보스 이름 (Windows OCR)", Stopwatch.GetTimestamp() - t0); }
    }
}
