using System.Diagnostics;
using System.IO;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Cooldown;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>The game window, its viewport and 스킬창 맞추기: finding the window, fitting the skill bar (auto or 직접 입력), recalibrating.</summary>
public sealed partial class Engine
{
    bool EnsureGame()
    {
        // In test mode the window can change at any click; a switch of mode drops the cached one either way.
        bool test = GameWindow.TestMode;
        var hwnd = !test && test == _testMode && _hwnd != 0 && GameWindow.ClientRect(_hwnd) is not null ? _hwnd : GameWindow.Find(test);
        // The window picked for 테스트 모드 is gone (also when it closed while minimised, already not running): App ends the mode.
        if (test && GameWindow.Picked is var picked && picked != 0 && !GameWindow.Exists(picked)) PickedWindowClosed?.Invoke(picked);
        if (test != _testMode)
        {
            // In or out of 테스트 모드: the video's fit and calibration (memory only) and the game's saved ones don't mix.
            _testFitsBy.Clear();
            ResetSource();   // 테스트 모드 reads the video's window itself (CaptureFactory), the game the screen
            _pictureNoticed = false;
            (_vp, _fit, _fitNeededRaised, _lastViewport, _vpPinned) = (default, null, false, TimeSpan.MinValue, false);
            Array.Clear(_cal);
            for (int i = 0; i < _states.Length; i++) _states[i] = SlotState.Unknown(SlotInfo.All[i]);
            _buffs = [.. Enum.GetValues<BuffKind>().Select(k => BuffState.Absent(k, TimeSpan.Zero))];
            (_debuffs, _gauge, _gaugeSpent) = (DebuffStatus.None, null, false);
            _gaugeFilter.Reset();
            // What casts add to the gauge: the game's is saved before a video plays, and what the video taught is dropped after.
            if (!_testMode) SaveGains();
            _gains = new(_gainsFor is not null ? _savedGains?.GetValueOrDefault(_gainsFor) : null);
            _gainsSaved = _gains.Version;
        }
        _testMode = test;
        // 테스트 모드: a minimised window pauses everything (its capture would be stale), as if it weren't there.
        bool running = hwnd != 0 && (!test || GameWindow.ClientRect(hwnd) is not null);
        if (running != _running) { _running = running; GameRunningChanged?.Invoke(running); }
        if (!running) { ResetSource(); return false; }
        // Desktop Duplication covers one monitor: a window moved to another one needs a new source.
        var monitor = GameWindow.MonitorOf(hwnd);
        if (hwnd != _hwnd || monitor != _monitor || _source is null)
        {
            ResetSource();
            if (hwnd != _hwnd) (_vpPinned, _pictureNoticed) = (false, false);   // another video in test mode (each keeps its own TestFits)
            (_hwnd, _monitor) = (hwnd, monitor);
            _source = CaptureFactory.Create(hwnd, test);
            _seesBehind = test && _source.SeesBehind;   // the game keeps its front waits (Windows 11's borderless WGC too)
            _lastViewport = TimeSpan.MinValue;
        }
        return true;
    }

    /// <param name="fitting">스킬창 맞추기 is about to run: the game area as it is now wins over a kept one.</param>
    void UpdateViewport(TimeSpan now, bool fitting = false)
    {
        // 테스트 모드 while 위치 조정 is open or after its 적용: the boxes stay on the screen pixels they were put on. A video's
        // dark scene reads as black bars, which moved the area, restarted the preview and looked for a fit again.
        // Refit (fitting), another window, a resize or a move releases them: the area (and the 위치 조정 layer over it) follows the window.
        if (GameWindow.ClientRect(_hwnd) is not { } client) return;
        bool sameSize = (client.Width, client.Height) == (_client.Width, _client.Height), samePlace = client == _client;
        if (fitting || !sameSize || !samePlace) _vpPinned = false;
        // So does a live area 위치 조정 opened with no saved fit (a cutscene's bars would restart it from HUD 100).
        else if (_vp.Height > 0 && (_testMode && (_vpPinned || _previewing) || _previewFit && _previewing)) { _lastViewport = now; return; }
        if (!samePlace) _vp = default;   // moved: found again where the window is now (the kept-fit rule below would hold the old spot)
        var manual = _testMode ? null : _config.Manual(client);   // 직접 입력 describes the game, not a video of it
        Viewport vp;
        if (_testMode)
        {
            // The video picture in the player window (fullscreen or not), never the game's 직접 입력 or black-bar probe.
            vp = TestArea(client, now, fitting);
        }
        else if (manual is { } m) vp = Geometry.ManualViewport(client, m.Screen);   // 직접 입력: no black-bar probe
        else
        {
            var probe = Letterbox.Probe(client);
            var column = Buffer(probe);
            if (!_source!.TryCapture([probe], [column], TimeSpan.FromMilliseconds(100), out _)) return;
            vp = Letterbox.Viewport(client, column);
        }
        _lastViewport = now;
        bool resized = (client.Width, client.Height) != (_client.Width, _client.Height);
        _client = client;
        if (vp == _vp) return;
        // Cutscenes add black bars too: on the same window size, keep a fitted viewport unless the new one has its own fit.
        // So does a 직접 입력 area: it replaces a fitted one once a fit is found there (스킬창 맞추기 adopts it to look).
        // In 테스트 모드 the fits are the video's (a fade to black or a dark scene in it is the same case).
        var fits = _config.Fits;
        bool Fitted(Viewport v) => _testMode ? _testFits.For(v) is not null
            : fits.ContainsKey(Geometry.ViewportKey(v)) || _config.Adjusts?.ContainsKey(Geometry.ViewportKey(v)) == true;
        if (!fitting && !resized && Fitted(_vp) && !Fitted(vp)) return;
        _vp = vp;
        Volatile.Write(ref _areaKey, Geometry.ViewportKey(vp));
        _fit = null;          // re-resolved for the new size
        _fitNeededRaised = false;
    }

    bool EnsureFit()
    {
        if (_vp.Height == 0) return false;
        // 설정 switched to or from 수동 조정 (or one was dropped): the fit in use isn't the one the mode now gives.
        if (_fit is not null && !_testMode && !_previewing && (_config.Adjusts?.ContainsKey(Geometry.ViewportKey(_vp)) == true) != _adjusted) _fit = null;
        if (_fit is not null) return true;
        if (_testMode)
        {
            // The video in front: its own fit, looked for on it while it is in front (TestFits), never the game's saved one.
            if (_testFits.For(_vp) is { } found) { ApplyFit(found); return true; }
            if (CanRead && _testFits.ShouldTry(_vp, Clock.Now()))
            {
                _quietFit = true;
                try { RunFit(); }
                finally { _quietFit = false; }
            }
            if (_fit is null) StartBare();
            return _fit is not null;
        }
        // 수동 조정 applied for this game area wins; its slot calibration is kept apart (CalibrationFile).
        Fit? adjust = null;
        _adjusted = _config.Adjusts?.TryGetValue(Geometry.ViewportKey(_vp), out adjust) == true;
        if (adjust is not null) { ApplyFit(adjust); return true; }
        if (!_config.Fits.TryGetValue(Geometry.ViewportKey(_vp), out var fit))
        {
            if (!_fitNeededRaised) { _fitNeededRaised = true; FitNeeded?.Invoke(_vp); }
            StartBare();
            return _fit is not null;
        }
        ApplyFit(fit);
        return true;
    }

    /// <summary>위치 조정 opened with nothing found: start from where HUD 100 puts the bar, for the user to move it (a drop clears it
    /// again; 적용 keeps it as 수동 조정, or as the video's fit in 테스트 모드).</summary>
    void StartBare()
    {
        if (PreviewStart.Fit(null, Volatile.Read(ref _ask) is not null) is not { } bare) return;
        ApplyFit(bare);
        _previewFit = true;
        if (!_bareNoticed) { _bareNoticed = true; PreviewWithoutFit?.Invoke(_testMode); }   // once per session, not per game-area change
    }

    void ApplyFit(Fit fit)
    {
        _previewFit = false;   // StartBare and the preview's own edits set it again
        Array.Clear(_cal);
        _fit = fit;
        _digits = new DigitReader(Geometry.Scale(_vp, fit));
        _buffers.Clear();
        for (int i = 0; i < _cal.Length; i++) _cal[i] = LoadCalibration(SlotInfo.All[i], _config);    }

    /// <summary>스킬창 맞추기; true when a fit was found.</summary>
    bool RunFit()
    {
        try { return FitNow(); }
        catch (Exception ex)
        {
            Trace.WriteLine($"[engine] fit failed: {ex}");
            return Failed($"스킬창을 맞추지 못했습니다: {ex.Message}");
        }
    }

    /// <summary>A fit not found, told to App unless it was looked for unasked (테스트 모드, <see cref="_quietFit"/>).</summary>
    bool Failed(string message)
    {
        if (!_quietFit) FitFinished?.Invoke(_vp, null, message, null, _testMode, false);
        return false;
    }

    bool FitNow()
    {
        if (_vp.Height == 0) return Failed("게임 화면을 찾지 못했습니다. 게임을 창 모드(전체 창)로 실행해 주세요.");
        // The request came from the tray menu or 설정, which leave the taskbar over the skill bar: App brings the game to
        // the front, and the capture waits for it and for the taskbar to slide away.
        for (int i = 0; i < FitWaitSteps && !CanRead; i++) Thread.Sleep(100);
        if (!CanRead) return Failed("게임 창을 앞으로 가져오지 못했습니다. 게임 화면을 한 번 누른 뒤 다시 시도해 주세요.");
        // A quiet try runs with the video already in front, and a window capture sees through the taskbar: nothing to wait out.
        if (!_quietFit && !_seesBehind) Thread.Sleep(TaskbarSettleMs);
        var band = FitSolver.Band(_vp);
        var img = new Image(band.Width, band.Height);
        if (!_source!.TryCapture([band], [img], TimeSpan.FromMilliseconds(500), out _))
            return Failed("화면을 캡처하지 못했습니다. 게임 창이 보이는 상태에서 다시 시도해 주세요.");
        // 테스트 모드: always read off the video (직접 입력 describes the game), and its calibration stays in memory.
        var (fit, note, detected) = !_testMode && _config.Manual(_client) is { } m ? SolveManual(img, band, m.Hud, m.Screen) : SolveAuto(img, band);
        if (fit is null) return Failed(note);

        // 테스트 모드, asked for (not the quiet try): the video's bar fine-tuned; 되돌리기 puts back the fit in use before.
        bool refined = false;
        if (_testMode && !_quietFit && FitSolver.Refine(img, band, _vp, fit) is { } tuned)
        {
            _testFits.SetFound(_vp, tuned, _testFits.For(_vp) ?? fit);
            (fit, refined) = (tuned, true);
        }
        else if (_testMode) _testFits.Set(_vp, fit);
        if (_testMode) _testFits.Lock((_client.Width, _client.Height));   // a fit found on the picture: it stays where it is
        _adjusted = false;   // found on screen (App drops a 수동 조정 of this area before asking)
        int ready = Recalibrate(fit);
        string what = refined ? TestFits.Describe(fit, Geometry.Scale(_vp, fit)) : $"스킬창을 맞췄습니다 (HUD {fit.Hud:0}%";
        FitFinished?.Invoke(_vp, fit, $"{what}{(refined ? " (" : ", ")}준비된 칸 {ready}/{SlotInfo.All.Length}개 기준 저장).{note}", detected, _testMode, refined);
        return true;
    }

    /// <summary>테스트 모드's 되돌리기: the fit kept by the last fit request, recalibrated when the video is in front (App brings it).</summary>
    void RevertFit()
    {
        _previewing = false;   // an open 위치 조정 starts over on the fit put back, as after a refit
        if (!_testMode || _testFits.Revert(_vp) is not { } back)
        {
            // Test mode ended, 위치 조정 applied another fit, or the game area changed since: say so, and the button goes.
            FitFinished?.Invoke(_vp, null, "되돌릴 맞춤이 없습니다. 테스트 모드가 끝났거나 위치를 다시 맞췄습니다.", null, _testMode, false);
            return;
        }
        for (int i = 0; i < FitWaitSteps && !CanRead; i++) Thread.Sleep(100);
        int ready = CanRead ? Recalibrate(back) : -1;
        if (ready < 0) ApplyFit(back);
        FitFinished?.Invoke(_vp, back, $"되돌렸습니다: {TestFits.Offsets(back, Geometry.Scale(_vp, back))}" +
            (ready >= 0 ? $" (준비된 칸 {ready}/{SlotInfo.All.Length}개 기준 저장)." : "."), null, true, false);
    }

    /// <summary>Puts <paramref name="fit"/> to use and calibrates every slot that reads ready now (cooldowns reset), replacing the
    /// saved calibration of the fit in use (수동 조정's or the others', <see cref="OwnCalibrationFiles"/>; none in 테스트 모드).
    /// Returns how many slots were ready.</summary>
    int Recalibrate(Fit fit)
    {
        if (!_testMode) foreach (var file in OwnCalibrationFiles().ToList()) File.Delete(file);
        ApplyFit(fit);
        var slots = SlotInfo.All;
        var rects = slots.Select(s => Rect(s, fit, _config)).ToList();
        var crops = rects.Select(r => new Image(r.Width, r.Height)).ToList();
        int ready = 0;
        if (_source!.TryCapture(rects, crops, TimeSpan.FromMilliseconds(500), out long ts))
            for (int i = 0; i < slots.Length; i++)
                if (SlotClassifier.Read(slots[i], crops[i], Clock.FromTimestamp(ts), null, _digits!, Dark(slots[i], _config)).Kind == ReadKind.Ready)
                {
                    SaveCalibration(slots[i], crops[i], trusted: true, _config);
                    ready++;
                    // A count on a ready icon: a stack skill with uses left (no ordinary icon in the fixtures shows one). Not in
                    // 테스트 모드: the video's 직업각인 or build may not be the active character's.
                    if (!_testMode && _config.Character is { } who && _config.StackSkills?.Contains(slots[i]) != true && _digits!.ShowsCount(crops[i]))
                        StackSkillLearned?.Invoke(who, slots[i]);
                }
        return ready;
    }

    (Fit? Fit, string Note, ManualScreen? Detected) SolveAuto(Image band, PixelRect rect)
    {
        if (FitSolver.Solve(band, rect, _vp, _testMode) is not { } fit)
            return (null, "HUD 크기를 찾지 못했습니다. HP 바가 보이는지 확인하거나, 설정 → 스킬창에서 직접 입력을 켜 보세요. 저장된 맞춤은 그대로 둡니다.", null);
        return (fit, "", Geometry.Detected(_vp, _client));
    }

    /// <summary>직접 입력: the bar is checked where the given values put it, then searched wider; not found, nothing is guessed.</summary>
    (Fit? Fit, string Note, ManualScreen? Detected) SolveManual(Image band, PixelRect rect, double hud, ManualScreen screen)
    {
        string given = $"HUD {hud:0}%, {screen.Width}×{screen.Height}{(screen.Wide ? ", 21:9" : "")}";
        var r = FitSolver.SolveManual(band, rect, _vp, hud);
        if (r.Fit is null)
            return (null, $"직접 입력한 값({given})으로 스킬창을 찾지 못했습니다. 값을 확인하거나 자동으로 맞추기를 눌러 주세요. 저장된 맞춤은 그대로 둡니다.", null);
        string note = r.Moved > 0 ? $" 입력한 값의 자리에서 {r.Moved}px 옮겨 맞췄습니다." : "";
        if ((_client.Width, _client.Height) != (screen.Width, screen.Height))
            note += $" 게임 창({_client.Width}×{_client.Height})이 입력한 해상도와 달라 창 크기에 맞춰 계산했습니다.";
        return (r.Fit, note, null);
    }
}
