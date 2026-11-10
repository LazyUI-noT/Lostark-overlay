using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App;

/// <summary>위치 조정 (수동 조정): 설정's button asks the engine for a preview; its start opens the layer over the game area, and the
/// preview runs while the layer is open. 적용 stores the fit as this game area's 수동 조정 (테스트 모드: the video's fit only) and
/// turns that mode on, and a moved 전분 box as this game area's spot; with only the box moved the fit is left as it was.</summary>
public partial class App
{
    /// <summary>Brings the game to the front before a read that needs it (fit, revert, 적용, detection), unless 테스트 모드 reads the
    /// video's window while covered: there the player stays as it is, so its controls don't pop up over the skill bar.</summary>
    static void FrontForRead(Engine engine)
    {
        if (!(GameWindow.TestMode && engine.CanCaptureBehind)) GameWindow.Activate();
    }

    void WireAdjust(Engine engine, Settings settings, Action configure, Action save)
    {
        // Wanted from the button until the layer is closed: a session start already queued when it closed doesn't open it again.
        // The 감지 page's placing (placeWanted) uses the same engine session: only one of the two at a time.
        bool wanted = false, placeWanted = false;
        _settingsWindow.AdjustRequested += () =>
        {
            if (placeWanted) { placeWanted = false; _settingsWindow.PlaceStopped(); engine.EndPreview(keep: false); }   // its session ends first, the layer starts a new one
            wanted = true;
            engine.Preview(new(0, null));   // the layer opens when the session starts
        };
        _layer.Dismissed += () => { wanted = false; engine.EndPreview(keep: false); };               // closed without 적용: back to the saved fit
        engine.PreviewStarted += (gen, vp, fit, buffSize, test, panel) => Dispatcher.BeginInvoke(() =>
        {
            if (wanted) _layer.Open(gen, vp, fit, buffSize, test, settings.Buffs.Count, settings.Debuffs.BossIconSize, panel);
            else if (placeWanted) _settingsWindow.PlaceStarted(gen, vp, fit, buffSize, test, settings.Buffs.Count, settings.Debuffs.BossIconSize, panel);
        });
        engine.GameRunningChanged += running => Dispatcher.BeginInvoke(() =>
        {
            if (running) return;
            // Nothing left to adjust: close the layer, and drop a request whose session never started (it would open on the next game).
            if (_layer.IsVisible) _layer.Dismiss();
            else if (wanted) { wanted = false; engine.EndPreview(keep: false); }
            if (placeWanted) { placeWanted = false; _settingsWindow.PlaceStopped("게임이 꺼져서 옮기기를 멈췄습니다."); engine.EndPreview(keep: false); }
        });
        engine.PreviewReads += (gen, reads) => Dispatcher.BeginInvoke(() => { _layer.ShowReads(gen, reads); _settingsWindow.PlaceReads(gen, reads); });
        _layer.AdjustChanged += (gen, a) => engine.Preview(new(gen, a.Current, a.BuffSize, a.Panel));
        void Report(string text)
        {
            _layer.Committed(text);
            _settingsWindow.PlaceCommitted(text);
            _tray.Notify(text);
        }
        void Apply(FitAdjust a, bool test)
        {
            PixelRect? panel = a.PanelMoved ? a.Panel : null;
            bool panelOnly = panel is not null && !a.FitChanged;   // 수동 조정 stays as it is: no fit to store, no slots to calibrate
            // The game's fit and 버프 크기 go to 설정; 테스트 모드's stay with the video until test mode ends (the engine keeps them).
            if (!test)
            {
                string key = Geometry.ViewportKey(a.Vp);
                if (panel is { } spot) settings.Meter.Panels[key] = spot;
                if (!panelOnly)
                {
                    settings.Buffs.Size = a.BuffSize;
                    settings.ApplyAdjust(key, a.Current);
                }
                configure();
                save();
                _settingsWindow.Reload();
            }
            if (panelOnly)
            {
                engine.EndPreview(keep: false, panel: panel);
                Report(test ? "테스트 모드: 전분 위치를 이 영상에만 적용했습니다." : "전분 위치를 저장했습니다.");
                return;
            }
            FrontForRead(engine);   // the ready slots are calibrated from the game in front
            engine.EndPreview(keep: true, a.Current, test ? a.BuffSize : null, panel);
        }
        _layer.ApplyRequested += (a, test) =>
        {
            Apply(a, test);
            engine.Preview(new(0, null));   // the layer stays open on what was applied
        };
        _settingsWindow.PlaceRequested += () =>
        {
            if (_layer.IsVisible) { _settingsWindow.PlaceStopped("위치 조정 화면을 닫은 뒤에 옮길 수 있습니다."); return; }
            placeWanted = true;
            engine.Preview(new(0, null));   // the page starts its edit when the session starts
        };
        _settingsWindow.PlaceEdited += (gen, a) => engine.Preview(new(gen, a.Current, a.BuffSize, a.Panel));
        _settingsWindow.PlaceApplied += (a, test) => { placeWanted = false; Apply(a, test); };   // the page's session ends with 적용
        _settingsWindow.PlaceEnded += () => { placeWanted = false; engine.EndPreview(keep: false); };
        engine.PreviewCommitted += (ready, test) => Dispatcher.BeginInvoke(() =>
        {
            Report((test ? "테스트 모드: " : "") + (ready >= 0
                ? $"수동 조정을 적용했습니다 (준비된 칸 {ready}/{SlotInfo.All.Length}개 기준 저장)."
                : "수동 조정을 적용했습니다. 게임이 앞에 없어 칸 기준은 저장하지 못했습니다. 게임을 앞에 두고 다시 적용해 주세요."));
        });
        engine.TestBuffSizeFound += size => Dispatcher.BeginInvoke(() =>
            _tray.Notify($"테스트 모드: 영상의 버프 크기 {size}%로 읽습니다 (설정은 그대로 둡니다)."));
        engine.PictureNotFound += () => Dispatcher.BeginInvoke(() =>
            _tray.Notify("테스트 모드: 영상 화면을 찾지 못해 창 전체를 게임 화면으로 씁니다. 위치 조정에서 맞춰 주세요."));
        engine.PreviewWithoutFit += test => Dispatcher.BeginInvoke(() =>
            _tray.Notify((test ? "테스트 모드: 스킬창을 찾지 못해" : "맞춘 스킬창이 없어") + " HUD 100% 위치에서 시작합니다. 위치 조정 화면에서 맞게 옮긴 뒤 적용하세요."));
    }
}
