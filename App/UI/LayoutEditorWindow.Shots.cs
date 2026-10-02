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

/// <summary>스크린샷: each profile's picture (<see cref="ScreenShots"/>), loaded from a file or taken of the game after a
/// 3-second countdown, then kept (덮어쓰기), taken again (다시찍기) or dropped (취소). A picture of the screen's shape lies
/// where it was on screen (<see cref="OverlayProfile.Shot"/>); any other is only a backdrop.</summary>
public sealed partial class LayoutEditorWindow
{
    static readonly Uri SampleScreen = new("pack://application:,,,/Assets/sample-screen.jpg");

    /// <summary>게임 화면 찍기, from App: hides what must not be in the picture, asks the engine for one and gives back
    /// the picture (null: none) and where it lies on screen. Null (the demo): no game to take.</summary>
    public Func<Task<(Core.Domain.Image? Picture, PixelRect Area)>>? TakeShot { get; set; }

    /// <summary>A screenshot was kept or removed: the live settings' profile has its new <see cref="OverlayProfile.Shot"/>, to save.</summary>
    public event Action? ShotSaved;

    readonly StackPanel _confirm = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new(0, 6, 0, 0) };
    readonly Button _again = new() { Content = "다시찍기", Padding = new(8, 2, 8, 2), Margin = new(0, 0, 6, 0) };
    (BitmapSource Picture, PixelRect? Area)? _pending;
    bool _taking;   // a countdown or capture is under way

    UIElement ShotRows()
    {
        var p = new StackPanel();
        p.Children.Add(Heading("스크린샷"));
        var load = new Button { Content = "불러오기", Padding = new(8, 2, 8, 2), Margin = new(0, 0, 6, 0), ToolTip = "png, jpg, bmp 그림을 이 프로필의 화면으로 씁니다." };
        var take = new Button { Content = "게임 화면 찍기", Padding = new(8, 2, 8, 2), Margin = new(0, 0, 6, 0), ToolTip = "3초 뒤 게임 화면을 찍습니다." };
        var clear = new Button { Content = "지우기", Padding = new(8, 2, 8, 2) };
        load.Click += (_, _) => LoadFile();
        take.Click += async (_, _) => await Take();
        clear.Click += (_, _) => Clear();
        p.Children.Add(Line(load, take, clear));

        var keep = new Button { Content = "덮어쓰기", Padding = new(8, 2, 8, 2), Margin = new(0, 0, 6, 0) };
        var cancel = new Button { Content = "취소", Padding = new(8, 2, 8, 2) };
        keep.Click += (_, _) => Keep();
        _again.Click += async (_, _) => await Take();
        cancel.Click += (_, _) => { _pending = null; ShowShot(); };
        _confirm.Children.Add(keep);
        _confirm.Children.Add(_again);
        _confirm.Children.Add(cancel);
        p.Children.Add(_confirm);
        return p;
    }

    /// <summary>The picture waiting for 덮어쓰기, else the profile's screenshot, else the sample screen.</summary>
    void ShowShot()
    {
        _confirm.Visibility = _pending is null ? Visibility.Collapsed : Visibility.Visible;
        if (_pending is var (picture, area))
        {
            Place(picture, area);
            _note.Text = $"새 화면입니다. 덮어쓰기를 누르면 프로필 {_s.ActiveProfile + 1}의 화면이 됩니다." + Relative(area);
            return;
        }
        var path = ScreenShots.PathOf(ScreenShots.Dir, _s.ActiveProfile);
        if (File.Exists(path) && Decode(new Uri(path)) is { } saved)
        {
            var shot = _s.Profiles[_s.ActiveProfile].Shot;
            Place(saved, shot);
            _note.Text = $"프로필 {_s.ActiveProfile + 1}의 화면입니다." + Relative(shot);
            return;
        }
        Place(new BitmapImage(SampleScreen), null);
        _note.Text = "예시 화면입니다. 오버레이는 모니터 크기에 맞춰 놓입니다.";
    }

    static string Relative(PixelRect? area) => area is null ? " 모니터와 모양이 달라 배경으로만 씁니다 (창 위치는 모니터 기준)." : "";

    /// <summary>Over the screen area it shows (physical pixels → this window's DIP), or over the whole screen.</summary>
    void Place(BitmapSource picture, PixelRect? area)
    {
        _shot.Source = picture;
        var dpi = VisualTreeHelper.GetDpi(this);
        var r = area is { } a ? new Rect(a.X / dpi.DpiScaleX - _screen.Left, a.Y / dpi.DpiScaleY - _screen.Top, a.Width / dpi.DpiScaleX, a.Height / dpi.DpiScaleY)
            : new Rect(0, 0, _screen.Width, _screen.Height);
        (_shot.Width, _shot.Height) = (r.Width, r.Height);
        Canvas.SetLeft(_shot, r.X);
        Canvas.SetTop(_shot, r.Y);
    }

    /// <summary>The picture in <paramref name="uri"/>, read at once (the file isn't locked, so 덮어쓰기 can replace it);
    /// null when it can't be read or is too big (checked before the pixels are decoded).</summary>
    static BitmapSource? Decode(Uri uri)
    {
        try
        {
            var frame = BitmapDecoder.Create(uri, BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (!ScreenShots.SizeOk(frame.PixelWidth, frame.PixelHeight)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            (image.UriSource, image.CacheOption, image.CreateOptions) = (uri, BitmapCacheOption.OnLoad, BitmapCreateOptions.IgnoreImageCache);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)   // WIC also throws COMException, OverflowException, … on a damaged file
        {
            return null;
        }
    }

    void LoadFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "그림 파일 (png, jpg, bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (!ScreenShots.CanLoad(dialog.FileName) || Decode(new Uri(dialog.FileName)) is not { } picture)
        {
            _note.Text = $"그림을 읽지 못했습니다 (png, jpg, bmp, 한 변 {ScreenShots.MaxSide}픽셀까지).";
            return;
        }
        _pending = (picture, ScreenShots.AreaFor(picture.PixelWidth, picture.PixelHeight, GameMonitor()));
        _again.Visibility = Visibility.Collapsed;
        ShowShot();
    }

    /// <summary>The game's monitor (physical pixels), the primary one while there is no game.</summary>
    static PixelRect GameMonitor()
    {
        if (GameWindow.Find() is var game && game != 0) return GameWindow.MonitorBounds(GameWindow.MonitorOf(game));
        var b = Forms.Screen.PrimaryScreen!.Bounds;
        return new(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>게임 화면 찍기: the editor steps aside, the game comes to the front, 3, 2, 1, and the picture comes back here.</summary>
    async Task Take()
    {
        if (_taking) return;   // a second click while the countdown runs
        var game = GameWindow.Find();
        if (TakeShot is null || game == 0)
        {
            _note.Text = "로스트아크 실행 중에만 찍을 수 있습니다.";
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
            _note.Text = "화면을 찍지 못했습니다. 게임 창이 보이는 상태에서 다시 해 주세요.";
            return;
        }
        var bitmap = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Bgra, img.Stride);
        bitmap.Freeze();
        _pending = (bitmap, shot.Area);
        _again.Visibility = Visibility.Visible;
        ShowShot();
    }

    /// <summary>덮어쓰기: the picture becomes this profile's (file first, through a temp file; then where it lies, in the
    /// copy and the live settings, since a file isn't something 닫기 can take back).</summary>
    void Keep()
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
            _note.Text = $"화면을 저장하지 못했습니다: {ex.Message}";
            return;
        }
        _s.Profiles[profile].Shot = _live.Profiles[profile].Shot = area;
        _pending = null;
        ShotSaved?.Invoke();
        ShowShot();
    }

    /// <summary>지우기: the profile goes back to the sample screen.</summary>
    void Clear()
    {
        int profile = _s.ActiveProfile;
        try { File.Delete(ScreenShots.PathOf(ScreenShots.Dir, profile)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _note.Text = $"화면을 지우지 못했습니다: {ex.Message}";
            return;
        }
        _s.Profiles[profile].Shot = _live.Profiles[profile].Shot = null;
        _pending = null;
        ShotSaved?.Invoke();
        ShowShot();
    }
}
