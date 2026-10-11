using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App;

/// <summary>위치 조정 (수동 조정): the debug window's live preview runs only while the window is open; 적용 stores the fit as this
/// game area's 수동 조정 (테스트 모드: the video's fit only) and turns that mode on.</summary>
public partial class App
{
    PreviewFrameData? _previewFrame;   // latest, drawn once per dispatch

    sealed record PreviewFrameData(int Gen, Image Bottom, PixelRect BottomRect, Image Top, PixelRect TopRect);

    void WireAdjust(Engine engine, Settings settings, Action configure, Action save)
    {
        _debug.AdjustOpenChanged += () =>
        {
            if (_debug.AdjustOpen) engine.Preview(new(0, null));
            else { engine.EndPreview(keep: false); _debug.StopAdjust(); }   // folded or closed without 적용: back to the saved fit
        };
        engine.PreviewStarted += (gen, vp, fit, buffSize, test) => Dispatcher.BeginInvoke(() => _debug.StartAdjust(gen, vp, fit, buffSize, test));
        engine.PreviewFrame += (gen, bottom, bottomRect, top, topRect) =>
        {
            // Coalesced: a slow UI thread skips frames instead of queueing them.
            if (Interlocked.Exchange(ref _previewFrame, new(gen, bottom, bottomRect, top, topRect)) is not null) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (Interlocked.Exchange(ref _previewFrame, null) is { } f) _debug.ShowFrame(f.Gen, f.Bottom, f.BottomRect, f.Top, f.TopRect);
            });
        };
        _debug.AdjustChanged += (gen, fit, buffSize) => engine.Preview(new(gen, fit, buffSize));
        _debug.ApplyRequested += (vp, fit, buffSize, test) =>
        {
            // The game's 버프 크기 is a 설정 value, stored in either mode; the fit is stored only for the game (not 테스트 모드's video).
            bool sized = settings.Buffs.Size != buffSize;
            if (sized) settings.Buffs.Size = buffSize;
            if (!test) settings.ApplyAdjust(Geometry.ViewportKey(vp), fit);
            if (sized || !test)
            {
                configure();
                save();
                _settingsWindow.Reload();
            }
            GameWindow.Activate();   // the ready slots are calibrated from the game in front
            engine.EndPreview(keep: true, fit);
            engine.Preview(new(0, null));   // the window stays open on the applied fit
        };
        engine.PreviewCommitted += (ready, test) => Dispatcher.BeginInvoke(() =>
        {
            var text = (test ? "테스트 모드: " : "") + (ready >= 0
                ? $"수동 조정을 적용했습니다 (준비된 칸 {ready}/{SlotInfo.All.Length}개 기준 저장)."
                : "수동 조정을 적용했습니다. 게임이 앞에 없어 칸 기준은 저장하지 못했습니다. 게임을 앞에 두고 다시 적용해 주세요.");
            _debug.Committed(text);
            _tray.Notify(text);
        });
        engine.PreviewWithoutFit += () => Dispatcher.BeginInvoke(() =>
            _tray.Notify("테스트 모드: 스킬창을 찾지 못해 HUD 100% 위치에서 시작합니다. 디버그 창에서 맞게 옮긴 뒤 적용하세요."));
        _settingsWindow.AdjustRequested += _debug.ShowAdjust;
    }
}
