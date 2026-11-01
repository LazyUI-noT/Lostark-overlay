using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using Forms = System.Windows.Forms;

namespace GaugeOverlay.App.UI;

/// <summary>스크린샷 (canvas tools): each profile's picture under the overlay (<see cref="ScreenShots"/>), loaded from a
/// file or taken of the game after a 3-second countdown, then kept (덮어쓰기), taken again (다시찍기) or dropped (취소).
/// A picture of the screen's shape lies where it was on screen (<see cref="OverlayProfile.Shot"/>); any other is a backdrop.</summary>
public partial class SettingsWindow
{
    /// <summary>게임 화면 찍기, from App: hides what must not be in the picture, asks the engine for one and gives back the
    /// picture (null: none) and where it lies on screen. Null (the demo): no game to take.</summary>
    public Func<Task<(Core.Domain.Image? Picture, PixelRect Area)>>? TakeShot { get; set; }

    readonly StackPanel _confirm = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly Button _again = new() { Content = "다시찍기", Margin = new(0, 2, 6, 2) };
    (BitmapSource Picture, PixelRect? Area)? _pending;
    bool _taking;   // a countdown or capture is under way

    /// <summary>The canvas tools' screenshot buttons: 불러오기, 게임 화면 찍기, 지우기, and 덮어쓰기 / 다시찍기 / 취소 for a new picture.</summary>
    UIElement ShotButtons()
    {
        Button Tool(string text, string? tip) => new() { Content = text, Margin = new(0, 2, 6, 2), ToolTip = tip };
        var load = Tool("불러오기", "png, jpg, bmp 그림을 이 프로필의 화면으로 씁니다.");
        var take = Tool("게임 화면 찍기", "3초 뒤 게임 화면을 찍습니다.");
        var clear = Tool("지우기", "이 프로필의 화면을 지우고 예시 화면으로 돌아갑니다.");
        load.Click += (_, _) => LoadShot();
        take.Click += async (_, _) => await Take();
        clear.Click += (_, _) => ClearShot();
        var keep = Tool("덮어쓰기", null);
        var cancel = Tool("취소", null);
        Themes.Primary(keep);
        keep.Click += (_, _) => KeepShot();
        _again.Click += async (_, _) => await Take();
        cancel.Click += (_, _) => { _pending = null; ShowCanvasShot(); };
        _confirm.Children.Add(keep);
        _confirm.Children.Add(_again);
        _confirm.Children.Add(cancel);
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 16, 0), Children = { load, take, clear, _confirm } };
    }

    /// <summary>The picture waiting for 덮어쓰기, else the profile's screenshot, else the sample screen.</summary>
    void ShowCanvasShot()
    {
        if (_canvas is null) return;
        _confirm.Visibility = _pending is null ? Visibility.Collapsed : Visibility.Visible;
        if (_pending is var (picture, area))
        {
            _canvas.ShowPicture(picture, area);
            _canvasNote.Text = $"새 화면입니다. 덮어쓰기를 누르면 프로필 {_s.ActiveProfile + 1}의 화면이 됩니다." + Relative(area);
            return;
        }
        if (SavedShot() is var (saved, shot))
        {
            _canvas.ShowPicture(saved, shot);
            _canvasNote.Text = $"프로필 {_s.ActiveProfile + 1}의 화면입니다." + Relative(shot);
            return;
        }
        _canvas.ShowSampleScreen();
        ShowCanvasNote();
    }

    /// <summary>The active profile's saved screenshot and the screen area it shows, or null.</summary>
    (System.Windows.Media.Imaging.BitmapSource Picture, PixelRect? Area)? SavedShot()
    {
        var path = ScreenShots.PathOf(ScreenShots.Dir, _s.ActiveProfile);
        return File.Exists(path) && OverlayCanvas.Decode(new Uri(path)) is { } saved ? (saved, _s.Profiles[_s.ActiveProfile].Shot) : null;
    }

    static string Relative(PixelRect? area) => area is null ? " 모니터와 모양이 달라 배경으로만 씁니다 (창 위치는 모니터 기준)." : "";

    void LoadShot()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "그림 파일 (png, jpg, bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (!ScreenShots.CanLoad(dialog.FileName) || OverlayCanvas.Decode(new Uri(dialog.FileName)) is not { } picture)
        {
            _canvasNote.Text = $"그림을 읽지 못했습니다 (png, jpg, bmp, 한 변 {ScreenShots.MaxSide}픽셀까지).";
            return;
        }
        _pending = (picture, ScreenShots.AreaFor(picture.PixelWidth, picture.PixelHeight, GameMonitor()));
        _again.Visibility = Visibility.Collapsed;
        ShowCanvasShot();
    }

    /// <summary>The game's monitor (physical pixels), the primary one while there is no game.</summary>
    static PixelRect GameMonitor()
    {
        if (GameWindow.Find() is var game && game != 0) return GameWindow.MonitorBounds(GameWindow.MonitorOf(game));
        var b = Forms.Screen.PrimaryScreen!.Bounds;
        return new(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>게임 화면 찍기: 설정 steps aside, the game comes to the front, 3, 2, 1, and the picture comes back here.</summary>
    async Task Take()
    {
        if (_taking) return;   // a second click while the countdown runs
        var game = GameWindow.Find();
        if (TakeShot is null || game == 0)
        {
            _canvasNote.Text = "로스트아크 실행 중에만 찍을 수 있습니다.";
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var m = GameWindow.MonitorBounds(GameWindow.MonitorOf(game));
        // ponytail: the countdown uses this window's DPI; on a mixed-DPI setup it may sit off centre on the game's monitor
        var monitor = new Rect(m.X / dpi.DpiScaleX, m.Y / dpi.DpiScaleY, m.Width / dpi.DpiScaleX, m.Height / dpi.DpiScaleY);
        var (state, profile) = (WindowState, _s.ActiveProfile);
        _taking = true;
        WindowState = WindowState.Minimized;
        GameWindow.Activate();
        (Core.Domain.Image? Picture, PixelRect Area) shot;
        try
        {
            await CountdownWindow.Run(monitor);
            shot = await TakeShot();
        }
        finally
        {
            _taking = false;
            WindowState = state;
            Activate();
        }
        if (profile != _s.ActiveProfile) return;   // switched meanwhile: the picture was for the other profile
        if (shot.Picture is not { } img)
        {
            _canvasNote.Text = "화면을 찍지 못했습니다. 게임 창이 보이는 상태에서 다시 해 주세요.";
            return;
        }
        var bitmap = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Bgra, img.Stride);
        bitmap.Freeze();
        _pending = (bitmap, shot.Area);
        _again.Visibility = Visibility.Visible;
        ShowCanvasShot();
    }

    /// <summary>덮어쓰기: the picture becomes this profile's (file first, through a temp file; then where it lies).</summary>
    void KeepShot()
    {
        if (_pending is not var (picture, area)) return;
        int profile = _s.ActiveProfile;
        var path = ScreenShots.PathOf(ScreenShots.Dir, profile);
        try
        {
            Directory.CreateDirectory(ScreenShots.Dir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(picture));
            var temp = path + ".tmp";
            try
            {
                using (var stream = File.Create(temp)) encoder.Save(stream);
                File.Move(temp, path, overwrite: true);
            }
            finally { File.Delete(temp); }   // gone after the move; a half-written one after a failure
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canvasNote.Text = $"화면을 저장하지 못했습니다: {ex.Message}";
            return;
        }
        _s.Profiles[profile].Shot = area;
        _pending = null;
        ResetBaseline();   // a file is no undo step: the next one starts from here
        SettingsChanged?.Invoke();
        ShowCanvasShot();
    }

    /// <summary>지우기: the profile goes back to the sample screen.</summary>
    void ClearShot()
    {
        int profile = _s.ActiveProfile;
        try { File.Delete(ScreenShots.PathOf(ScreenShots.Dir, profile)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canvasNote.Text = $"화면을 지우지 못했습니다: {ex.Message}";
            return;
        }
        _s.Profiles[profile].Shot = null;
        _pending = null;
        ResetBaseline();
        SettingsChanged?.Invoke();
        ShowCanvasShot();
    }
}
