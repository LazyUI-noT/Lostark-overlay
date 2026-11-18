using System.Collections.Immutable;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>읽는 자리's saved pictures (reading-place.plan.md B): 캐릭터 선택 화면 찍기 / 레이드 화면 찍기 save one, the engine saves a
/// raid one when 전분 or 암수 turns !, and a picker puts one behind the boxes, showing only the boxes that picture holds.</summary>
public partial class SettingsWindow
{
    ReadingShotStore? _readingStore;
    /// <summary>The saved pictures, from App (null in the demo before wiring: no takes, no list).</summary>
    public ReadingShotStore? ReadingStore
    {
        get => _readingStore;
        set
        {
            _readingStore = value;
            if (value is not null) value.Changed += () => Dispatcher.BeginInvoke(() => { if (_secDetect.IsVisible) DrawReadingBar(); });
        }
    }

    readonly WrapPanel _readingBar = new();
    readonly TextBlock _readingNote = new() { TextWrapping = TextWrapping.Wrap };
    Button? _readingClear;   // 지우기: on while a picture is shown
    ReadingShot? _readingShown;   // the picture behind the boxes; null: none (the boxes on the plain game area)
    Viewport _readingListedFor;   // the game area the picker was listed and the picture placed for

    /// <summary>The take buttons and the picker, at the top of 읽는 자리.</summary>
    void ReadingRows(StackPanel p)
    {
        var select = new Button { Content = "캐릭터 선택 화면 찍기", Margin = new(0, 0, Tokens.Space.Gap, 0) };
        var raid = new Button { Content = "레이드 화면 찍기" };
        select.Click += async (_, _) => await TakeReading(ReadingShotKind.Select);
        raid.Click += async (_, _) => await TakeReading(ReadingShotKind.Raid);
        p.Children.Add(Row(HelpLabel("화면 찍기",
            "3초 뒤 게임 화면을 찍어 이 PC에만 저장합니다. 캐릭터 선택 화면은 ESC 게임 메뉴를 연 채로 찍으세요. 전분이나 암수가 !가 되면 레이드 화면을 저절로 저장하고, 레이드 화면은 최근 3장만 남깁니다."), new WrapPanel { Children = { select, raid } }));
        Detach(_readingBar);
        Detach(_readingNote);
        p.Children.Add(Row("보이는 화면", _readingBar));
        p.Children.Add(_readingNote);
        DrawReadingBar();
    }

    /// <summary>The picker: 없음, then the pictures saved for this game-area size, then 지우기 for the one shown.</summary>
    void DrawReadingBar()
    {
        _readingBar.Children.Clear();
        _readingListedFor = _detectView;
        var shots = _readingStore is { } store && _detectView.Width > 0 ? store.List(_detectView) : [];
        if (_readingShown is { } shown && !shots.Contains(shown)) ShowReading(null);   // deleted, or another game-area size
        if (shots.Count == 0)   // nothing to pick: plain text, not a lit 없음 button as the loudest thing on the page (ui-audit P3)
        {
            _readingClear = null;
            _readingBar.Children.Add(Muted(new TextBlock { Text = "없음", VerticalAlignment = VerticalAlignment.Center }));
            if (_detectView.Width > 0) _readingNote.Text = "이 해상도로 저장한 화면이 없습니다.";   // no game: the screen's empty state says so
            return;
        }
        var none = Toggle("없음", "reading-shot", _readingShown is null);
        none.Checked += (_, _) => ShowReading(null);
        _readingBar.Children.Add(none);
        int raidNo = 0;
        foreach (var shot in shots)
        {
            string label = shot.Kind == ReadingShotKind.Select ? "캐릭터 선택" : $"레이드 {++raidNo} ({shot.At:HH:mm})";
            var pick = Toggle(label, "reading-shot", shot == _readingShown);
            pick.Checked += (_, _) => ShowReading(shot);
            _readingBar.Children.Add(pick);
        }
        var clear = _readingClear = UiBuild.Danger(WhyTip(new Button { Content = "지우기", IsEnabled = _readingShown is not null, Margin = new(0, 0, 0, Tokens.Space.Gap) }, "지울 화면을 먼저 고르세요."));
        clear.Click += (_, _) => DeleteReading();
        _readingBar.Children.Add(clear);
    }

    /// <summary>Puts <paramref name="shot"/> behind the boxes (null: none).</summary>
    void ShowReading(ReadingShot? shot)
    {
        _readingShown = shot;
        (_detectShot, _detectShotImage) = (null, null);
        _readingNote.Text = "";
        if (shot is not null && _readingStore is { } store)
        {
            if (ReadingShotStore.Load(store.PathOf(_detectView, shot), _detectView) is var (picture, pixels))
                (_detectShot, _detectShotImage) = ((picture, new PixelRect(_detectView.X, _detectView.Y, _detectView.Width, _detectView.Height)), pixels);
            else
            {
                _readingShown = null;
                _readingNote.Text = "저장한 화면을 열지 못했습니다. 지우고 다시 찍으세요.";
            }
        }
        if (_readingClear is { } clear) clear.IsEnabled = _readingShown is not null;
        RefreshGreen();
        DrawDetect();
    }

    /// <summary>캐릭터 선택 / 레이드 화면 찍기: the countdown, the game area cut out, saved, and shown.</summary>
    async Task TakeReading(ReadingShotKind kind)
    {
        if (_readingStore is not { } store) { _readingNote.Text = "로스트아크 실행 중에만 찍을 수 있습니다."; return; }
        if (await CountdownShot(text => _readingNote.Text = text) is not var (img, area)) return;
        var vp = _detectView;
        if (ReadingShots.GameArea(img, area, vp) is not { } game)
        {
            _readingNote.Text = "게임 화면이 다 찍히지 않았습니다. 게임 창 전체가 모니터 안에 보이게 하고 다시 찍으세요.";
            return;
        }
        var failed = await Task.Run(() => kind == ReadingShotKind.Select ? store.SaveSelect(vp, game) : store.SaveRaid(vp, game, DateTime.Now));
        if (failed is not null) { _readingNote.Text = failed; return; }
        if (vp != _detectView) { DrawReadingBar(); return; }   // the game area changed meanwhile: the picture is under its old size
        _readingShown = store.List(vp).FirstOrDefault(s => s.Kind == kind);   // a raid list is newest first
        ShowReading(_readingShown);
        DrawReadingBar();
    }

    void DeleteReading()
    {
        if (_readingShown is not { } shot || _readingStore is not { } store) return;
        string what = shot.Kind == ReadingShotKind.Select ? "캐릭터 선택 화면" : $"레이드 화면 ({shot.At:HH:mm})";
        if (MessageBox.Show(this, $"{what}을 지울까요?", SecDetect, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (store.Delete(_detectView, shot) is { } failed) { _readingNote.Text = failed; return; }
        ShowReading(null);
        DrawReadingBar();
    }

    /// <summary>The game area changed: another size lists its own pictures; a moved one places the shown picture again.</summary>
    void ReadingViewChanged()
    {
        if (_readingListedFor == _detectView) return;
        bool sameSize = (_readingListedFor.Width, _readingListedFor.Height) == (_detectView.Width, _detectView.Height);
        DrawReadingBar();
        if (sameSize && _readingShown is { } shown) ShowReading(shown);
    }
}

/// <summary>--demo --tab=읽는 자리 --reading=&lt;1920×1080 png&gt;: a game area with its boxes and grid lines and the picture saved as
/// 캐릭터 선택 and as a raid one in a temp folder (for shot.ps1; the demo has no game, so the page otherwise shows its empty state).</summary>
public partial class SettingsWindow
{
    internal void DemoReading(string png, string? show, string? open = null, bool sample = false)
    {
        var (vp, fit) = (new Viewport(0, 0, 1920, 1080), new Fit(110.05));
        const int buffSize = 120, buffCount = 10, bossSize = 100;
        // ponytail: the engine's Areas (Engine.Detect.cs) restated for the demo; it only has to look right in a screenshot
        var panel = Geometry.UsualPanel(vp);
        var slots = SlotInfo.All.Select(s => Geometry.Slot(s, vp, fit)).ToList();
        int left = slots.Min(r => r.X), top = slots.Min(r => r.Y);
        var areas = new Dictionary<string, PixelRect>
        {
            ["skill"] = new(left, top, slots.Max(r => r.Right) - left, slots.Max(r => r.Bottom) - top),
            ["buff"] = Geometry.BuffArea(vp, fit, buffSize, buffCount),
            [DetectPlace.DebuffRow] = Geometry.DebuffArea(vp, fit, buffSize),
            ["boss"] = Geometry.BossTextRow(vp, fit),
            ["amsu"] = Geometry.BossDebuffStrip(vp, fit, bossSize),
            ["g-jeonbun"] = panel,
            ["judg"] = Geometry.JudgmentBand(vp),
        }.ToImmutableDictionary();
        var grid = areas.Keys.ToImmutableDictionary(id => id, id => ReadingGrid.Of(id, vp, fit, buffSize, buffCount, bossSize, panel));
        var store = ReadingStore = new ReadingShotStore(Path.Combine(Path.GetTempPath(), "GaugeOverlay-reading-demo"));
        if (ReadingShotStore.Load(png, vp) is var (_, pixels))
        {
            store.SaveSelect(vp, pixels);
            store.SaveRaid(vp, pixels, DateTime.Now);
        }
        (_detectAreas, _detectView, _detectGrid) = (areas, vp, grid);
        _readingShown = show switch
        {
            "select" => store.List(vp).FirstOrDefault(s => s.Kind == ReadingShotKind.Select),
            "raid" => store.List(vp).FirstOrDefault(s => s.Kind == ReadingShotKind.Raid),
            _ => null,
        };
        FillDetect();
        if (_readingShown is { } shot) ShowReading(shot);
        if (open is null) return;
        _ = Dispatcher.InvokeAsync(async () =>   // --reading-open=id: that box in the zoom window, once --tab has shown the page
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            _detectPicked = open;
            OpenPlace();
            if (!sample) return;
            PlaceStarted(0, vp, fit, buffSize, false, buffCount, bossSize, panel);   // the demo has no engine to start the session
            _placeWindow?.DemoSample();
        });
    }
}
