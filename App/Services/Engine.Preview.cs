using System.IO;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>수동 조정's live preview: while the debug window is open, the fit it sends replaces the one in use, and the window gets
/// pictures of the bottom band and the boss's band ~5 times a second from the same capture as the reads. Nothing is saved while
/// previewing (calibration is neither read nor written); <see cref="EndPreview"/> drops the edit or keeps it (적용).</summary>
public sealed partial class Engine
{
    /// <summary>UI → engine: the fit to show for preview session <see cref="Gen"/> (null: none yet, just start one) and the game's
    /// 버프 크기 to read both rows at meanwhile (null: 설정's). Immutable.</summary>
    public sealed record PreviewAsk(int Gen, Fit? Fit, int? BuffSize = null);

    int? _previewBuffSize;   // engine thread only: the session's 버프 크기 until 적용 stores it in 설정

    /// <summary>A preview session began: its number, the game area, the fit it starts from, the buff size, 테스트 모드.</summary>
    public event Action<int, Viewport, Fit, int, bool>? PreviewStarted;
    /// <summary>The session's pictures: the bottom band and the boss band, each with where it is on screen.</summary>
    public event Action<int, Image, PixelRect, Image, PixelRect>? PreviewFrame;
    /// <summary>적용 kept: the slots calibrated as ready (-1: the game wasn't in front), 테스트 모드 (then only the video's fit has it).</summary>
    public event Action<int, bool>? PreviewCommitted;

    /// <summary>테스트 모드: 위치 조정 began with no fit found, from <see cref="BareHud"/>.</summary>
    public event Action? PreviewWithoutFit;
    /// <summary>테스트 모드: the window picked from the tray no longer exists (raised each step until App ends the mode).</summary>
    public event Action<IntPtr>? PickedWindowClosed;

    bool _bareNoticed;   // engine thread only: PreviewWithoutFit told in this session

    const double BareHud = 100;
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
    public void EndPreview(bool keep, Fit? fit = null)
    {
        Volatile.Write(ref _ask, null);
        Volatile.Write(ref _kept, keep ? fit : null);
        Interlocked.Exchange(ref _end, keep ? 2 : 1);
    }

    Fit? _kept;

    /// <summary>After <see cref="EnsureFit"/>: ends, starts or follows the preview. A drop leaves <see cref="_fit"/> null, so the
    /// saved fit and its calibration come back next step.</summary>
    void PreviewStep()
    {
        int end = Interlocked.Exchange(ref _end, 0);
        if (end != 0) (_bareNoticed, _previewBuffSize) = (false, null);
        if (end != 0 && _previewing)
        {
            _previewing = false;
            var kept = Interlocked.Exchange(ref _kept, null);
            // A drop, or an 적용 made for a game area that has changed since (its fit is for the old one): back to the saved fit.
            if (end == 1 || _vp != _previewVp) { _fit = null; return; }
            var fit = kept ?? _fit!;
            if (_testMode) _testFits.Set(_vp, fit);
            else _adjusted = true;   // App has stored it in Settings.Adjusts; its calibration is the …_adj files
            // The ready slots are read off the game: App brings it to the front, so wait for it as 스킬창 맞추기 does.
            for (int i = 0; i < FitWaitSteps && !_foreground; i++) Thread.Sleep(100);
            int ready = -1;
            if (_foreground) ready = Recalibrate(fit);
            else
            {
                if (!_testMode) foreach (var file in OwnCalibrationFiles().ToList()) File.Delete(file);   // none stale from an older 수동 조정
                ApplyFit(fit);
            }
            PreviewCommitted?.Invoke(ready, _testMode);
        }
        if (Volatile.Read(ref _ask) is not { } ask) return;
        if (!_previewing || _vp != _previewVp)
        {
            (_previewing, _previewVp, _lastPreview, _previewBuffSize) = (true, _vp, TimeSpan.MinValue, null);
            PreviewStarted?.Invoke(++_gen, _vp, _fit!, _config.DebuffSize, _testMode);
            return;
        }
        if (ask.Gen != _gen) return;
        _previewBuffSize = ask.BuffSize;
        if (ask.Fit is { } f && f != _fit) ApplyFit(f);
    }
}
