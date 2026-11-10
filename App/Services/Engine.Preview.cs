using System.IO;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App.Services;

/// <summary>수동 조정's live preview: while the debug window is open, the fit it sends replaces the one in use, and the window gets
/// pictures of the bottom band and the boss's band ~5 times a second from the same capture as the reads. Nothing is saved while
/// previewing (calibration is neither read nor written); <see cref="EndPreview"/> drops the edit or keeps it (적용).</summary>
public sealed partial class Engine
{
    /// <summary>UI → engine: the fit to show for preview session <see cref="Gen"/> (null: none yet, just start one) and the game's
    /// 버프 크기 to read both rows at meanwhile (null: 설정's), and the 전분 box, only looked at for its green: the meter keeps reading
    /// its own spot until 적용. Immutable.</summary>
    public sealed record PreviewAsk(int Gen, Fit? Fit, int? BuffSize = null, PixelRect? Panel = null);

    int? _previewBuffSize;   // engine thread only: the session's 버프 크기 until 적용 stores it in 설정
    PixelRect? _previewPanel;   // engine thread only: the session's 전분 box

    /// <summary>A preview session began: its number, the game area, the fit it starts from, the buff size, 테스트 모드, the 전분 spot in use.</summary>
    public event Action<int, Viewport, Fit, int, bool, PixelRect?>? PreviewStarted;
    /// <summary>The session's read states (what the adjust layer draws green), every <see cref="PreviewEvery"/>.</summary>
    public event Action<int, AdjustReads>? PreviewReads;
    /// <summary>적용 kept: the slots calibrated as ready (-1: the game wasn't in front), 테스트 모드 (then only the video's fit has it).</summary>
    public event Action<int, bool>? PreviewCommitted;

    /// <summary>위치 조정 began with no fit found, from <see cref="PreviewStart.BareHud"/>; true in 테스트 모드.</summary>
    public event Action<bool>? PreviewWithoutFit;
    /// <summary>테스트 모드: the window picked from the tray no longer exists (raised each step until App ends the mode).</summary>
    public event Action<IntPtr>? PickedWindowClosed;

    bool _previewFit;    // engine thread only: _fit was put by a preview (StartBare or an edit), not found or saved
    bool _bareNoticed;   // engine thread only: PreviewWithoutFit told in this session

    static readonly TimeSpan PreviewEvery = TimeSpan.FromMilliseconds(200);
    PreviewAsk? _ask;
    int _end;            // set by EndPreview: 1 drop the edit, 2 keep it
    bool _previewing;    // engine thread only
    int _gen;
    Viewport _previewVp;
    TimeSpan _lastPreview = TimeSpan.MinValue;

    /// <summary>Starts a preview (<c>new(0, null)</c>) or shows a fit in the current one; any thread.</summary>
    public void Preview(PreviewAsk ask) => Volatile.Write(ref _ask, ask);

    /// <summary>The debug window closed (<paramref name="keep"/> false: back to the saved fit) or 적용 (true, with the fit applied:
    /// the last edit may not have reached the engine yet); any thread.</summary>
    /// <param name="testBuffSize">테스트 모드's 적용: the video's 버프 크기, kept for the test session instead of 설정.</param>
    /// <param name="panel">적용 of a moved 전분 box: read from now on, also with <paramref name="keep"/> false (only the box moved: the fit stays as saved).</param>
    public void EndPreview(bool keep, Fit? fit = null, int? testBuffSize = null, PixelRect? panel = null)
    {
        _keptPanel = panel;
        Volatile.Write(ref _ask, null);
        Volatile.Write(ref _kept, keep ? fit : null);
        _keptBuffSize = keep ? testBuffSize : null;
        Interlocked.Exchange(ref _end, keep ? 2 : 1);
    }

    Fit? _kept;
    volatile object? _keptBuffSize;   // boxed int: 테스트 모드's 적용 buff size, read with the end
    volatile object? _keptPanel;      // boxed PixelRect: 적용's 전분 spot, read with the end

    /// <summary>After <see cref="EnsureFit"/>: ends, starts or follows the preview. A drop leaves <see cref="_fit"/> null, so the
    /// saved fit and its calibration come back next step.</summary>
    void PreviewStep()
    {
        int end = Interlocked.Exchange(ref _end, 0);
        if (end != 0) (_bareNoticed, _previewBuffSize, _previewPanel) = (false, null, null);
        if (end != 0 && _previewing)
        {
            _previewing = false;
            // Over any spot found earlier, which would otherwise win (PanelSpot), and at once: a fight already running reads it next.
            // 테스트 모드's stays here, unsaved, until the mode ends.
            if (Interlocked.Exchange(ref _keptPanel, null) is PixelRect placed && _vp == _previewVp)
                (_foundPanel, _panelMisses) = ((Geometry.ViewportKey(_vp), placed), 0);
            var kept = Interlocked.Exchange(ref _kept, null);
            // A drop, or an 적용 made for a game area that has changed since (its fit is for the old one): back to the saved fit.
            if (end == 1 || _vp != _previewVp) { _fit = null; return; }
            var fit = kept ?? _fit!;
            if (_testMode)
            {
                _testFits.Set(_vp, fit);
                _testFits.Lock((_client.Width, _client.Height));
                if (Interlocked.Exchange(ref _keptBuffSize, null) is int size) _testFits.BuffSize = size;
                _vpPinned = true;
            }
            else _adjusted = true;   // App has stored it in Settings.Adjusts; its calibration is the …_adj files
            // The ready slots are read off the game: App brings it to the front, so wait for it as 스킬창 맞추기 does.
            for (int i = 0; i < FitWaitSteps && !CanRead; i++) Thread.Sleep(100);
            int ready = -1;
            if (CanRead) ready = Recalibrate(fit);
            else
            {
                if (!_testMode) foreach (var file in OwnCalibrationFiles().ToList()) File.Delete(file);   // none stale from an older 수동 조정
                ApplyFit(fit);
            }
            PreviewCommitted?.Invoke(ready, _testMode);
        }
        // A fit only a preview made (HUD 100, or an edit) with no preview running: the window closed before its session began.
        // Never read or calibrate with it.
        if (_previewFit && !_previewing && Volatile.Read(ref _ask) is null) { (_fit, _previewFit) = (null, false); return; }
        if (Volatile.Read(ref _ask) is not { } ask) return;
        if (!_previewing || _vp != _previewVp)
        {
            (_previewing, _previewVp, _lastPreview, _previewBuffSize, _previewPanel) = (true, _vp, TimeSpan.MinValue, null, null);
            PreviewStarted?.Invoke(++_gen, _vp, _fit!, (_testMode ? _testFits.BuffSize : null) ?? _config.DebuffSize, _testMode, PanelSpot(_config));
            return;
        }
        if (ask.Gen != _gen) return;
        (_previewBuffSize, _previewPanel) = (ask.BuffSize, ask.Panel);
        if (ask.Fit is { } f && f != _fit) { ApplyFit(f); _previewFit = true; }
    }
}
