using System.Text.Json;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

/// <summary>preview-images.plan.md §3: the 게임 화면 preview's crop, zoom, pan, minimap and 창 위치 옮기기.</summary>
public class PreviewViewTests
{
    static readonly ViewRect Screen = new(0, 0, 1920, 1080);
    const double W = 320, H = 200;

    static PreviewView Centred(ViewRect window) { var v = new PreviewView(Screen, W, H); v.CentreOn(window); return v; }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void Crop_AtEachZoom_IsTheViewOverZoomAroundTheWindow(double zoom)
    {
        var v = Centred(new(900, 500, 120, 80));   // centre (960, 540), far from the edges
        v.ZoomBy(zoom);

        Assert.Equal(new ViewRect(960 - W / 2 / zoom, 540 - H / 2 / zoom, W / zoom, H / zoom), v.Crop);
        Assert.Equal((zoom, W / 2 - 960 * zoom, H / 2 - 540 * zoom), v.Transform);
    }

    [Fact]
    public void Zoom_StartsAtActualSize_AndStaysInItsLimits()
    {
        var v = new PreviewView(Screen, W, H);
        Assert.Equal(1, v.Zoom);
        for (int i = 0; i < 40; i++) v.ZoomBy(PreviewView.ZoomStep);
        Assert.Equal(PreviewView.MaxZoom, v.Zoom);
        for (int i = 0; i < 40; i++) v.ZoomBy(1 / PreviewView.ZoomStep);
        Assert.Equal(PreviewView.MinZoom, v.Zoom);
    }

    [Fact]
    public void Pan_DragOff_SlidesTheViewWithThePointer()
    {
        var v = Centred(new(900, 500, 120, 80));
        v.ZoomBy(2);
        v.Pan(40, -20);   // the picture follows the pointer: the view's centre goes the other way, at the zoom
        Assert.Equal((940, 550), (v.CentreX, v.CentreY));
    }

    [Fact]
    public void CentreOn_AfterPanning_ReturnsToTheWindow()
    {
        var window = new ViewRect(900, 500, 120, 80);
        var v = Centred(window);
        v.Pan(150, 90);
        v.CentreOn(window);   // 창으로 돌아가기
        Assert.Equal((960, 540), (v.CentreX, v.CentreY));
    }

    [Fact]
    public void Edge_AWindowInTheCorner_ShowsThePictureNotBlank()
    {
        var v = Centred(new(0, 0, 40, 30));
        // at most EdgeRoom view px of the border past the picture's edge, never the window centred over blank
        Assert.Equal(-PreviewView.EdgeRoom, v.Crop.X);
        Assert.Equal(-PreviewView.EdgeRoom, v.Crop.Y);
        v.Pan(-5000, -5000);
        Assert.Equal(Screen.Width + PreviewView.EdgeRoom, v.Crop.X + v.Crop.Width, 6);
        Assert.Equal(Screen.Height + PreviewView.EdgeRoom, v.Crop.Y + v.Crop.Height, 6);
    }

    [Fact]
    public void Edge_APictureSmallerThanTheView_IsCentred()
    {
        var v = new PreviewView(new(100, 100, 200, 100), W, H);
        v.Pan(500, 500);
        Assert.Equal((200, 150), (v.CentreX, v.CentreY));
    }

    [Fact]
    public void Minimap_ClickJumpsThere_AndTheBoxDragPans()
    {
        var v = Centred(new(900, 500, 120, 80));
        v.JumpTo(0.25, 0.5);
        Assert.Equal((480, 540), (v.CentreX, v.CentreY));
        v.PanMinimap(0.1, -0.1);   // the box dragged by a tenth of the minimap
        Assert.Equal((672, 432), (v.CentreX, v.CentreY));
        var box = v.MinimapBox(192, 108);   // a tenth of the picture's size
        Assert.Equal(new ViewRect((672 - W / 2) / 10, (432 - H / 2) / 10, W / 10, H / 10), box);
    }

    [Fact]
    public void Resize_KeepsTheCentre()
    {
        var v = Centred(new(900, 500, 120, 80));
        v.Resize(500, 300);
        Assert.Equal((960, 540), (v.CentreX, v.CentreY));
        Assert.Equal(500, v.Crop.Width);
    }

    [Fact]
    public void ToWorld_ADragOn_MovesTheWindowByViewPixelsOverZoom()
    {
        var v = new PreviewView(Screen, W, H);
        v.ZoomBy(2);
        Assert.Equal((20, -10), v.ToWorld(40, -20));
    }

    [Fact]
    public void PictureRect_SavedShotWithArea_IsItsAreaInDip()
    {
        var rect = PreviewView.PictureRect(new PixelRect(300, 150, 2880, 1620), saved: true, 1.5, 1.5, 2560, 1440);
        Assert.Equal(new ViewRect(200, 100, 1920, 1080), rect);
    }

    [Fact]
    public void PictureRect_NoAreaPicture_IsABackdropWithNoCrop()
    {
        Assert.Null(PreviewView.PictureRect(null, saved: true, 1, 1, 1920, 1080));
    }

    [Fact]
    public void PictureRect_EmptyArea_IsABackdropWithNoCrop()
    {
        Assert.Null(PreviewView.PictureRect(new PixelRect(0, 0, 0, 1080), saved: true, 1, 1, 1920, 1080));
    }

    [Fact]
    public void PictureRect_SampleOrColour_IsTheScreen()
    {
        Assert.Equal(new ViewRect(0, 0, 1920, 1080), PreviewView.PictureRect(null, saved: false, 1, 1, 1920, 1080));
    }

    // ── 창 위치 옮기기 on: the window's spot ──────────────────────────────────────

    [Theory]
    [InlineData(OverlayPart.Skills)]
    [InlineData(OverlayPart.Debuffs)]
    [InlineData(OverlayPart.Judgment)]
    [InlineData(OverlayPart.Amsu)]
    [InlineData(OverlayPart.BarCount)]
    [InlineData(OverlayPart.Meter)]
    [InlineData(OverlayPart.Party)]
    [InlineData(OverlayPart.Buffs)]
    [InlineData(OverlayPart.Gauge)]
    public void WindowSpot_Set_MovesThatWindowOnly(OverlayPart part)
    {
        var s = new Settings();
        string before = JsonSerializer.Serialize(new Settings(), SettingsStore.Json);

        WindowSpot.Set(s, part, 123, 456);

        Assert.Equal((123, 456), WindowSpot.Get(s, part));
        Assert.NotEqual(before, JsonSerializer.Serialize(s, SettingsStore.Json));
        WindowSpot.Set(s, part, double.NaN, double.NaN);   // back to unset: nothing else was touched
        Assert.Equal(before, JsonSerializer.Serialize(s, SettingsStore.Json));
    }

    [Fact]
    public void WindowSpot_ButtonsInMyWindow_MoveMyWindow()
    {
        var s = new Settings();
        s.Buffs.Detached = false;
        s.Apart = false;
        WindowSpot.Set(s, OverlayPart.Buffs, 10, 20);
        Assert.Equal((10, 20), (s.Overlay.Left, s.Overlay.Top));
        WindowSpot.Set(s, OverlayPart.Gauge, 30, 40);
        Assert.Equal((30, 40), (s.Overlay.Left, s.Overlay.Top));
    }

    [Fact]
    public void WindowSpot_PartyApart_MovesTheFirstMember()
    {
        var s = new Settings();
        s.Party.Together = false;
        WindowSpot.Set(s, OverlayPart.Party, 70, 80);
        Assert.Equal((70, 80), (s.Party.ShowOthersBySlot[0].Left, s.Party.ShowOthersBySlot[0].Top));
    }

    [Fact]
    public void DragOn_IsOneUndoStep_ThatPutsTheWindowBack()
    {
        var s = new Settings();
        var history = new SettingsHistory();
        string Snap() => JsonSerializer.Serialize(s, SettingsStore.Json);
        var recorder = new ChangeRecorder(history, Snap);
        recorder.Reset();

        recorder.Step();   // the press (SettingsWindow.RecordStep), then the spot written at release
        WindowSpot.Set(s, OverlayPart.Skills, 500, 600);

        Assert.Equal(1, history.UndoCount);
        var back = JsonSerializer.Deserialize<Settings>(history.Undo(Snap())!, SettingsStore.Json)!;
        Assert.True(double.IsNaN(back.Overlay.Left));
    }

    // ── 다른 오버레이 표시 ──────────────────────────────────────────────────────────

    [Fact]
    public void Others_AreOnlyTheShownWindowsNotOnThePage()
    {
        var on = new HashSet<OverlayPart> { OverlayPart.Skills, OverlayPart.Gauge, OverlayPart.Debuffs, OverlayPart.Party, OverlayPart.Meter };

        var others = SettingsPreview.Others([OverlayPart.Skills, OverlayPart.Gauge], on.Contains);

        Assert.Equal([OverlayPart.Debuffs, OverlayPart.Party], others);   // 미터기 isn't on the canvas
    }
}
