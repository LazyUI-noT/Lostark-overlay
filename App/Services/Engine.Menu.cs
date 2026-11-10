using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Windows.Graphics.Imaging;

namespace GaugeOverlay.App.Services;

/// <summary>The ESC 게임 메뉴 (homework.plan.md): who is being played, the 다른 캐릭터 접속 grid's cards and a switch about to
/// happen. The menu never moves and its cards sit on a fixed grid, so what the OCR read once is known by its pixels
/// (<see cref="MenuMemory"/>): the title strip is compared 10/s and, open, the watched area every step; a card seen before gets its
/// tags within a step or two (<see cref="MenuRecalled"/>). The panel's OCR (about 0.4 s, on the thread pool, one at a time, sharing the
/// dungeon panel's engine) runs as <see cref="MenuPace"/> says, to meet new cards and hear who is played. Until the title's pixels are
/// known its OCR is the check, once a second. The title's pixels are saved per screen size (<see cref="MenuTitleStore"/>, not in
/// 테스트 모드), and no title OCR runs while a fight does (<see cref="FightClock.Running"/>): perf-hotspots.plan.md step 2.</summary>
public sealed partial class Engine
{
    static readonly TimeSpan MenuProbeEvery = TimeSpan.FromSeconds(1), MenuTitleEvery = TimeSpan.FromMilliseconds(100), MenuRelearnEvery = TimeSpan.FromSeconds(5);
    static readonly MenuTitleStore MenuTitles = new(Path.GetDirectoryName(SettingsStore.DefaultPath)!);
    const int TitleMissesToClose = 2;
    // The card names are ~13 px tall at 1080p. Measured on goal.png and the video's frames (--read-menu, 2026-11-04): 3× with the
    // contrast stretched from 60 and inverted reads the portrait, the header and the waiting text right on every frame; 2×, or
    // no stretch, misreads them; a floor of 100 loses the cards. The small card names stay unreliable at any setting.
    const double MenuUpscale = 3;
    const int ContrastFloor = 60;
    readonly MenuMemory _menuMemory = new();
    readonly MenuPace _menuPace = new();
    Viewport _menuVp;
    TimeSpan _lastMenuTitle = TimeSpan.MinValue, _lastMenuProbe = TimeSpan.MinValue;
    volatile bool _menuOpen;   // the pool's read may close it while the title isn't known yet
    long _menuNamedTicks;   // engine-clock ticks of the last menu read that named the character played (감지 중), 0 = never
    byte[]? _menuCells;
    string? _menuKey;
    int _menuBusy, _titleOcrSeen, _titleMisses, _menuLearned;
    (IReadOnlyList<MenuCard> Cards, MenuCard? Playing) _menuHeld = ([], null);

    /// <summary>A read of the open menu (or <see cref="MenuRead.Closed"/> once it is gone), with the viewport the reference positions map
    /// to (Geometry.MenuArea). On a pool thread, or the engine's for Closed.</summary>
    public event Action<MenuRead, Viewport, TimeSpan>? MenuSeen;

    /// <summary>The cards known by their pixels, as the grid changed, for the tags only: no names to vote on. On the engine thread.</summary>
    public event Action<MenuRead, Viewport, TimeSpan>? MenuRecalled;

    /// <summary>What to capture: the title strip when due; open, the watched area, and the panel and the waiting corner when a read is due.</summary>
    (PixelRect? Title, PixelRect? Panel, PixelRect? Waiting, PixelRect? Watch) MenuRects(TimeSpan now)
    {
        if (Ocr.Value is null || _vp.Width <= 0) return (null, null, null, null);
        if (_vp != _menuVp)   // another screen size: other pixels
        {
            if (_menuOpen) MenuSeen?.Invoke(MenuRead.Closed, _vp, now);   // the tags go and the name votes start over
            (_menuVp, _menuOpen) = (_vp, false);
            _menuMemory.Clear();
            var word = Geometry.MenuArea(_vp, MenuSlots.RefTitleWord);
            if (!_testMode && MenuTitles.Load(_vp.Width, _vp.Height, word.Width * word.Height) is { } saved) _menuMemory.LearnTitle(saved);
        }
        if (Interlocked.Exchange(ref _titleOcrSeen, 0) == 1 && !_menuOpen) OpenMenu(now);
        bool known = _menuMemory.TitleKnown;
        var title = Geometry.MenuArea(_vp, Geometry.RefMenuTitle);
        bool titleDue = (known || !_menuOpen) && title.Inside(_vp) && Elapsed(now, _lastMenuTitle, known ? MenuTitleEvery : MenuProbeEvery);
        PixelRect? titleRect = titleDue ? title : null;
        if (!_menuOpen) return (titleRect, null, null, null);
        var (panel, waiting, watch) = (Geometry.MenuArea(_vp, Geometry.RefMenuPanel), Geometry.MenuWaiting(_vp), Geometry.MenuArea(_vp, MenuSlots.RefWatch));
        if (!panel.Inside(_vp) || !waiting.Inside(_vp) || !watch.Inside(_vp)) return (titleRect, null, null, null);
        bool read = Volatile.Read(ref _menuBusy) == 0 && _menuPace.Due(now);
        return (titleRect, read ? panel : null, read ? waiting : null, watch);
    }

    void OpenMenu(TimeSpan now)
    {
        (_menuOpen, _menuCells, _menuKey, _titleMisses, _menuHeld) = (true, null, null, 0, ([], null));
        _menuPace.Open(now);
    }

    /// <summary>The title strip: its pixels once known (open or closed at once, no OCR; every <see cref="MenuRelearnEvery"/> while closed
    /// its OCR too, should the game's look have changed), else its OCR.</summary>
    void StepMenuTitle(Image img, TimeSpan t)
    {
        _lastMenuTitle = t;
        bool fighting = FightClock.Running(_meter.Clock, t, _presence.BossUp(t));   // the menu is opened once the 전분 stopped
        if (!_menuMemory.TitleKnown) { if (!fighting) StartMenu(img, null, null, t); return; }
        bool same = _menuMemory.IsTitle(MenuSlots.Grey(img, Geometry.MenuArea(_vp, Geometry.RefMenuTitle), Geometry.MenuArea(_vp, MenuSlots.RefTitleWord)));
        if (same)
        {
            _titleMisses = 0;
            if (!_menuOpen) OpenMenu(t);
        }
        else if (_menuOpen)
        {
            if (++_titleMisses < TitleMissesToClose) return;
            _menuOpen = false;
            MenuSeen?.Invoke(MenuRead.Closed, _vp, t);
        }
        else if (!fighting && Elapsed(t, _lastMenuProbe, MenuRelearnEvery)) StartMenu(img, null, null, t);
    }

    /// <summary>Keeps the title's pixels, and saves them for this screen size (pool thread; not in 테스트 모드: a video's
    /// title isn't the game's on this PC).</summary>
    void LearnTitle(byte[] title, Viewport vp)
    {
        _menuMemory.LearnTitle(title);
        if (_testMode) return;
        try { MenuTitles.Save(vp.Width, vp.Height, title); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[menu] title not saved: {ex.Message}"); }
    }

    /// <summary>The watched area: when it changed, a read is called for and the cards it knows go out at once. The only source of the
    /// tags: a read takes 0.4 s and sometimes finds no cards at all (homework_minor.mp4), so it teaches this and never replaces them.</summary>
    void StepMenuWatch(Image img, TimeSpan t)
    {
        if (!_menuOpen) return;   // the title closed it this step: the fading menu's strips can still match, and nothing would clear those tags
        var cells =MenuSlots.Cells(img, _vp.Height / Geometry.ReferenceHeight);
        bool learned = Interlocked.Exchange(ref _menuLearned, 0) == 1;   // a read taught new cards: look again, though nothing moved
        if (_menuCells is { } was && !learned)
        {
            if (!MenuSlots.Moved(was, cells)) return;
            _menuPace.Changed(t);
        }
        _menuCells = cells;
        _menuHeld = MenuSlots.Steady(_menuHeld, MenuSlots.Recalled(_menuMemory, MenuSlots.Strips(img, Geometry.MenuArea(_vp, MenuSlots.RefWatch), _vp)));
        var (cards, playing) = _menuHeld;
        string key = string.Join('|', cards.Select(c => $"{c.Name}@{c.X},{c.Y}")) + (playing is { } p ? $"+{p.X},{p.Y}" : "");
        if (key == _menuKey && !learned) return;
        _menuKey = key;
        MenuRecalled?.Invoke(new MenuRead(true, null, null, cards, false, null, playing), _vp, t);
    }

    /// <summary>Starts the OCR of what was captured (copies: the capture buffers are reused next step).</summary>
    void StartMenu(Image? title, Image? panel, Image? waiting, TimeSpan t)
    {
        if (Interlocked.CompareExchange(ref _menuBusy, 1, 0) != 0) return;
        if (panel is not null) _menuPace.Read(t);
        else _lastMenuProbe = t;
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
                    Interlocked.Exchange(ref _titleOcrSeen, 1);   // the panel is read from the next step
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
            if (read.Waiting) Trace.WriteLine($"[menu] waiting: {read.Picked} picked (playing {read.Current})");
            var area = Geometry.MenuArea(vp, Geometry.RefMenuPanel);
            if (GameMenu.IsTitle(lines.Select(l => string.Join(' ', l.Select(w => w.Text)))))
                LearnTitle(MenuSlots.Grey(panel, area, Geometry.MenuArea(vp, MenuSlots.RefTitleWord)), vp);   // the same frame's pixels
            if (!read.Open)
            {
                if (_menuMemory.TitleKnown) return;   // the title's pixels decide once known: this read just missed the words
                _menuOpen = false;
                MenuSeen?.Invoke(read, vp, t);
                return;
            }
            var strips = MenuSlots.Strips(panel, area, vp);
            MenuSlots.Learn(_menuMemory, read, strips);
            Interlocked.Exchange(ref _menuLearned, 1);
            var merged = MenuSlots.WithRecalled(read, _menuMemory, strips);
            if (merged.Current is not null) Interlocked.Exchange(ref _menuNamedTicks, Math.Max(1, t.Ticks));
            if (_menuOpen) MenuSeen?.Invoke(merged, vp, t);   // not one that closed while it was read
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
        Perf.Count("OCR 게임 메뉴");
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
