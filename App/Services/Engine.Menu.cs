using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Windows.Graphics.Imaging;

namespace GaugeOverlay.App.Services;

/// <summary>The ESC 게임 메뉴 (homework.plan.md): who is being played, the 다른 캐릭터 접속 grid's cards and a switch about to
/// happen. Closed, only the title strip is read (<see cref="MenuProbeEvery"/>); once "게임 메뉴" is seen, the panel with its word
/// boxes and the waiting text in the bottom-right corner (<see cref="MenuReadEvery"/>), until it is gone. Windows OCR on the
/// thread pool, one read at a time, sharing the dungeon panel's engine.</summary>
public sealed partial class Engine
{
    // The open menu's panel is a big read (3× of 1540×540 at 1080p): once a second; "잠시만 기다려주세요." stays ~4 s (the video).
    static readonly TimeSpan MenuProbeEvery = TimeSpan.FromSeconds(1), MenuReadEvery = TimeSpan.FromSeconds(1), MenuOpenHold = TimeSpan.FromSeconds(2.5);
    // The card names are ~13 px tall at 1080p. Measured on goal.png and the video's frames (--read-menu, 2026-11-04): 3× with the
    // contrast stretched from 60 and inverted reads the portrait, the header and the waiting text right on every frame; 2×, or
    // no stretch, misreads them; a floor of 100 loses the cards. The small card names stay unreliable at any setting.
    const double MenuUpscale = 3;
    const int ContrastFloor = 60;
    TimeSpan _lastMenu = TimeSpan.MinValue;
    long _menuOpenTicks = long.MinValue;   // when "게임 메뉴" was last seen (pool thread writes, engine thread reads)
    int _menuBusy;

    /// <summary>A read of the open menu (or <see cref="MenuRead.Closed"/> once it is gone), on a pool thread, with the viewport the
    /// reference positions map to (Geometry.MenuArea).</summary>
    public event Action<MenuRead, Viewport, TimeSpan>? MenuSeen;

    /// <summary>"게임 메뉴" seen within <see cref="MenuOpenHold"/> (never seen: closed; the subtraction would overflow).</summary>
    bool MenuOpen(TimeSpan now) => Interlocked.Read(ref _menuOpenTicks) is var seen && seen != long.MinValue && now.Ticks - seen < MenuOpenHold.Ticks;

    /// <summary>The title strip while closed, else the panel and the waiting corner; null when no read is due.</summary>
    (PixelRect? Title, PixelRect? Panel, PixelRect? Waiting) MenuRects(TimeSpan now)
    {
        if (Ocr.Value is null || _vp.Width <= 0 || Volatile.Read(ref _menuBusy) == 1) return (null, null, null);
        bool open = MenuOpen(now);
        if (!Elapsed(now, _lastMenu, open ? MenuReadEvery : MenuProbeEvery)) return (null, null, null);
        if (!open)
        {
            var title = Geometry.MenuArea(_vp, Geometry.RefMenuTitle);
            return title.Inside(_vp) ? (title, null, null) : (null, null, null);
        }
        var (panel, waiting) = (Geometry.MenuArea(_vp, Geometry.RefMenuPanel), Geometry.MenuWaiting(_vp));
        return panel.Inside(_vp) && waiting.Inside(_vp) ? (null, panel, waiting) : (null, null, null);
    }

    /// <summary>Starts the OCR of what was captured (copies: the capture buffers are reused next step).</summary>
    void StartMenu(Image? title, Image? panel, Image? waiting, TimeSpan t)
    {
        if (Interlocked.CompareExchange(ref _menuBusy, 1, 0) != 0) return;
        _lastMenu = t;
        var (a, b, c, vp) = (title?.Copy(), panel?.Copy(), waiting?.Copy(), _vp);
        _ = Task.Run(() => ReadMenu(a, b, c, vp, t));
    }

    async Task ReadMenu(Image? title, Image? panel, Image? waiting, Viewport vp, TimeSpan t)
    {
        long t0 = Stopwatch.GetTimestamp();
        try
        {
            if (title is not null)
            {
                if (GameMenu.IsTitle((await RecognizeWords(title)).Select(l => string.Join(' ', l.Select(w => w.Text)))))
                    Interlocked.Exchange(ref _menuOpenTicks, t.Ticks);   // the panel is read from the next step
                return;
            }
            if (panel is null || waiting is null) return;
            double s = vp.Height / Geometry.ReferenceHeight;
            var lines = (await RecognizeWords(panel)).Select(l => (IReadOnlyList<MenuWord>)[.. l.Select(w => w with
            {
                X = Geometry.RefMenuPanel.X + w.X / s, Y = Geometry.RefMenuPanel.Y + w.Y / s, W = w.W / s, H = w.H / s,
            })]).ToList();
            var waits = (await RecognizeWords(waiting)).Select(l => string.Join(' ', l.Select(w => w.Text))).ToList();
            var read = GameMenu.Parse(lines, waits);
            if (read.Open) Interlocked.Exchange(ref _menuOpenTicks, t.Ticks);
            else Interlocked.Exchange(ref _menuOpenTicks, long.MinValue);   // back to the title strip
            if (read.Waiting) Trace.WriteLine($"[menu] waiting: {read.Picked} picked (playing {read.Current})");
            MenuSeen?.Invoke(read, vp, t);
        }
        catch (Exception ex)
        {
            var e = ex.GetBaseException();
            Trace.WriteLine($"[menu] OCR failed: {e.GetType().Name} 0x{e.HResult:X8} {e.Message}");
        }
        finally { _menuBusy = 0; Perf.Add("게임 메뉴 (Windows OCR)", Stopwatch.GetTimestamp() - t0); }
    }

    /// <summary>Grey, contrast stretched from <see cref="ContrastFloor"/>, inverted: the menu's light text dark on light.</summary>
    static Image Contrasted(Image img)
    {
        var px = (byte[])img.Bgra.Clone();
        for (int i = 0; i < px.Length; i += 4)
        {
            int lum = (px[i] * 29 + px[i + 1] * 150 + px[i + 2] * 77) >> 8;
            byte v = (byte)(255 - Math.Clamp((lum - ContrastFloor) * 255 / (255 - ContrastFloor), 0, 255));
            (px[i], px[i + 1], px[i + 2]) = (v, v, v);
        }
        return new Image(img.Width, img.Height, px);
    }

    /// <summary>Dev (--read-menu): a whole-screen picture's panel and waiting corner, cut and read as the loop reads them.</summary>
    internal static async Task<(MenuRead Read, IReadOnlyList<string> Lines)> ReadPicture(Image screen)
    {
        var vp = new Viewport(0, 0, screen.Width, screen.Height);
        double s = vp.Height / Geometry.ReferenceHeight;
        var words = await RecognizeWords(ImageOps.Crop(screen, Geometry.MenuArea(vp, Geometry.RefMenuPanel)));
        var lines = words.Select(l => (IReadOnlyList<MenuWord>)[.. l.Select(w => w with
        {
            X = Geometry.RefMenuPanel.X + w.X / s, Y = Geometry.RefMenuPanel.Y + w.Y / s, W = w.W / s, H = w.H / s,
        })]).ToList();
        var waits = (await RecognizeWords(ImageOps.Crop(screen, Geometry.MenuWaiting(vp)))).Select(l => string.Join(' ', l.Select(w => w.Text))).ToList();
        var title = (await RecognizeWords(ImageOps.Crop(screen, Geometry.MenuArea(vp, Geometry.RefMenuTitle)))).Select(l => string.Join(' ', l.Select(w => w.Text)));
        var shown = lines.Select(l => string.Join(' ', l.Select(w => $"{w.Text}@{w.X:0},{w.Y:0}"))).Append($"waiting: {string.Join(" | ", waits)}")
            .Append($"title strip: {string.Join(" | ", title)} → {(GameMenu.IsTitle(title) ? "open" : "closed")}");
        return (GameMenu.Parse(lines, waits), [.. shown]);
    }

    /// <summary>The OCR's lines as words with their boxes, in the image's own pixels (read at <see cref="MenuUpscale"/>).</summary>
    static async Task<IReadOnlyList<IReadOnlyList<MenuWord>>> RecognizeWords(Image img)
    {
        var big = Contrasted(ImageOps.Resample(img, new PixelRect(0, 0, img.Width, img.Height), MenuUpscale));
        using var bmp = SoftwareBitmap.CreateCopyFromBuffer(big.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, big.Width, big.Height, BitmapAlphaMode.Premultiplied);
        await OcrGate.WaitAsync();
        try
        {
            var result = await Ocr.Value!.RecognizeAsync(bmp);
            return [.. result.Lines.Select(l => (IReadOnlyList<MenuWord>)[.. l.Words.Select(w => new MenuWord(w.Text,
                w.BoundingRect.X / MenuUpscale, w.BoundingRect.Y / MenuUpscale, w.BoundingRect.Width / MenuUpscale, w.BoundingRect.Height / MenuUpscale))])];
        }
        catch
        {
            Ocr = NewOcr();   // under the gate: the next recognition makes a fresh engine
            throw;
        }
        finally { OcrGate.Release(); }
    }
}
