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

/// <summary>이미지 (canvas tools): the 5 image slots' pictures (<see cref="ScreenShots"/>), shared by all profiles; a profile shows
/// one (<see cref="OverlayProfile.ShotSlot"/>). A picture is loaded from a file or taken of the game after a 3-second
/// countdown, then kept in a slot picked in a popup (덮어쓰기), taken again (다시 찍기) or dropped (취소). A picture of the
/// screen's shape lies where it was on screen (<see cref="Settings.SlotAreas"/>); any other is a backdrop.</summary>
public partial class SettingsWindow
{
    /// <summary>게임 화면 찍기, from App: hides what must not be in the picture, asks the engine for one and gives back the
    /// picture (null: none) and where it lies on screen. Null (the demo): no game to take.</summary>
    public Func<Task<(Core.Domain.Image? Picture, PixelRect Area)>>? TakeShot { get; set; }

    readonly StackPanel _confirm = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly Button _again = new() { Content = "다시 찍기", Margin = new(0, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair) };
    (BitmapSource Picture, PixelRect? Area)? _pending;
    bool _taking;   // a countdown or capture is under way

    RadioButton[] _slotChips = [];
    Button _clearSlot = new();
    bool _chipsSetting;   // the chips set from the settings, not by the user

    /// <summary>The canvas tools' image buttons: 이미지 불러오기, 화면 캡처, 이미지 선택, chips 1–5 (the profile's pick), 지우기,
    /// and 덮어쓰기 / 다시 찍기 / 취소 for a new picture.</summary>
    UIElement ShotButtons()
    {
        Button Tool(string text, string? tip) => new() { Content = text, Margin = new(0, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair), ToolTip = tip };
        var load = Tool("이미지 불러오기", "png, jpg, bmp 그림 파일을 가져옵니다. 덮어쓰기를 누르면 넣을 이미지를 고릅니다.");
        var take = Tool("화면 캡처", "3초 뒤 게임 화면을 찍습니다. 덮어쓰기를 누르면 넣을 이미지를 고릅니다.");
        var pick = Tool("이미지 선택", "이 프로필이 보여 줄 이미지를 고릅니다.");
        _newShotsButton.Click += (_, _) => ShowNewShotsPopup();
        _clearSlot = Tool("지우기", "이 프로필이 보여 주는 이미지를 지우고 예시 화면으로 돌아갑니다.");
        load.Click += (_, _) => LoadShot();
        take.Click += async (_, _) => await Take();
        pick.Click += (_, _) => ShowSlotPicker(pick, "이 프로필이 보여 줄 이미지", _s.Profiles[_s.ActiveProfile].ShotSlot, null, PickSlot);
        _clearSlot.Click += (_, _) => ClearShot();
        var keep = Tool("덮어쓰기", null);
        var cancel = Tool("취소", null);
        Themes.Primary(keep);
        keep.Click += (_, _) => ShowSlotPicker(keep, "새 화면을 넣을 이미지", _s.Profiles[_s.ActiveProfile].ShotSlot, "덮어쓰기", KeepShot);
        _again.Click += async (_, _) => await Take();
        cancel.Click += (_, _) => { _pending = null; ShowCanvasShot(); };
        _confirm.Children.Add(keep);
        _confirm.Children.Add(_again);
        _confirm.Children.Add(cancel);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, Tokens.Space.Gap, 0), VerticalAlignment = VerticalAlignment.Center };
        _slotChips = new RadioButton[OverlayProfileCount];
        for (int i = 0; i < _slotChips.Length; i++)
        {
            int slot = i;
            var chip = Toggle($"{i + 1}", "slot", false);
            (chip.Margin, chip.MinWidth, chip.Padding, chip.Style) = (new(0, 0, 4, 0), 36, new(10, 4, 10, 4), ProfileButtonStyle);
            chip.Checked += (_, _) => { if (!_chipsSetting) PickSlot(slot); };
            chips.Children.Add(_slotChips[i] = chip);
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, Tokens.Space.Card, 0), Children = { load, take, pick, chips, _newShotsButton, _clearSlot, _confirm } };
    }

    /// <summary>The chips as the settings say: the profile's pick checked, an empty slot greyed (and says so on hover).</summary>
    void ShowSlotChips()
    {
        if (_slotChips.Length == 0) return;
        _chipsSetting = true;
        int shown = _s.Profiles[_s.ActiveProfile].ShotSlot;
        for (int i = 0; i < _slotChips.Length; i++)
        {
            bool filled = File.Exists(ScreenShots.PathOf(ScreenShots.Dir, i));
            _slotChips[i].IsChecked = i == shown;
            _slotChips[i].Opacity = filled ? 1 : 0.5;
            _slotChips[i].ToolTip = filled ? $"이미지 {i + 1}" : $"이미지 {i + 1}: 비어 있습니다.";
            _slotChips[i].FontWeight = i == shown ? FontWeights.Bold : FontWeights.Normal;
        }
        _clearSlot.IsEnabled = File.Exists(ScreenShots.PathOf(ScreenShots.Dir, shown));
        _chipsSetting = false;
    }

    /// <summary>This profile shows image <paramref name="slot"/> (a setting: an undo step).</summary>
    void PickSlot(int slot)
    {
        if (_s.Profiles[_s.ActiveProfile].ShotSlot == slot) { ShowSlotChips(); return; }
        _s.Profiles[_s.ActiveProfile].ShotSlot = slot;
        ShowCanvasShot();
        Changed();   // the canvas, the previews and the 되돌리기 step
    }

    /// <summary>The picture waiting for 덮어쓰기, else the profile's screenshot, else the sample screen.</summary>
    void ShowCanvasShot()
    {
        if (_canvas is null) return;
        RefreshPlacement();
        _confirm.Visibility = _pending is null ? Visibility.Collapsed : Visibility.Visible;
        ShowSlotChips();
        if (_pending is var (picture, area))
        {
            _canvas.ShowPicture(picture, area);
            _canvasNote.Text = "새 화면입니다. 덮어쓰기를 누르고 넣을 이미지를 고르세요." + Relative(area);
            return;
        }
        if (SavedShot() is var (saved, shot))
        {
            _canvas.ShowPicture(saved, shot);
            _canvasNote.Text = $"이미지 {_s.Profiles[_s.ActiveProfile].ShotSlot + 1}입니다." + Relative(shot);
            return;
        }
        _canvas.ShowSampleScreen();
        ShowCanvasNote();
    }

    /// <summary>The picture of the image slot the active profile shows and the screen area it covers, or null (an empty slot).</summary>
    (System.Windows.Media.Imaging.BitmapSource Picture, PixelRect? Area)? SavedShot()
    {
        var path = ScreenShots.PathOf(ScreenShots.Dir, _s.Profiles[_s.ActiveProfile].ShotSlot);
        return File.Exists(path) && OverlayCanvas.Decode(new Uri(path)) is { } saved ? (saved, _s.ShotArea) : null;
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

    /// <summary>게임 화면 찍기: the picture comes back here, for the profile it was taken for.</summary>
    async Task Take()
    {
        int profile = _s.ActiveProfile;
        if (await CountdownShot(text => _canvasNote.Text = text) is not var (img, area)) return;
        if (profile != _s.ActiveProfile) return;   // switched meanwhile: the picture was for the other profile
        var bitmap = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, img.Bgra, img.Stride);
        bitmap.Freeze();
        _pending = (bitmap, area);
        _again.Visibility = Visibility.Visible;
        ShowCanvasShot();
    }

    /// <summary>설정 steps aside, the game comes to the front, 3, 2, 1, and the game's picture comes back with where it lies on
    /// screen. Null: none (a second click while the countdown runs, no game, a failed capture), with why told through <paramref name="say"/>.</summary>
    async Task<(Core.Domain.Image Picture, PixelRect Area)?> CountdownShot(Action<string> say)
    {
        if (_taking) return null;   // a second click while the countdown runs
        var game = GameWindow.Find();
        if (TakeShot is null || game == 0)
        {
            say("로스트아크 실행 중에만 찍을 수 있습니다.");
            return null;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var m = GameWindow.MonitorBounds(GameWindow.MonitorOf(game));
        // ponytail: the countdown uses this window's DPI; on a mixed-DPI setup it may sit off centre on the game's monitor
        var monitor = new Rect(m.X / dpi.DpiScaleX, m.Y / dpi.DpiScaleY, m.Width / dpi.DpiScaleX, m.Height / dpi.DpiScaleY);
        var state = WindowState;
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
        if (shot.Picture is not { } picture)
        {
            say("화면을 찍지 못했습니다. 게임 창이 보이는 상태에서 다시 찍으세요.");
            return null;
        }
        return (picture, shot.Area);
    }

    /// <summary>덮어쓰기: the picture becomes image <paramref name="slot"/> (file first, through a temp file; then where it lies),
    /// which this profile then shows.</summary>
    void KeepShot(int slot)
    {
        if (_pending is not var (picture, area)) return;
        var path = ScreenShots.PathOf(ScreenShots.Dir, slot);
        var others = File.Exists(path) ? _s.ProfilesOnSlot(slot).Where(p => p != _s.ActiveProfile).Select(p => p + 1).ToArray() : [];
        if (others.Length > 0 && MessageBox.Show(this, $"프로필 {string.Join(", ", others)}도 이미지 {slot + 1}을 씁니다. 덮어쓸까요?", "이미지 덮어쓰기", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
        _s.SlotAreas[slot] = area;
        _pending = null;
        bool otherSlot = _s.Profiles[_s.ActiveProfile].ShotSlot != slot;
        _s.Profiles[_s.ActiveProfile].ShotSlot = slot;
        ShowCanvasShot();
        if (otherSlot) Changed();   // the pick is a setting: a step
        else { ResetBaseline(); SettingsChanged?.Invoke(); PreviewChanged(); }   // a file is no undo step: the next one starts from here
    }

    /// <summary>지우기: the picked image is emptied and every profile on it goes back to the sample screen; asks first when
    /// other profiles show it.</summary>
    void ClearShot()
    {
        int slot = _s.Profiles[_s.ActiveProfile].ShotSlot;
        var others = _s.ProfilesOnSlot(slot).Where(p => p != _s.ActiveProfile).Select(p => p + 1).ToArray();
        if (others.Length > 0 && MessageBox.Show(this, $"프로필 {string.Join(", ", others)}도 이미지 {slot + 1}을 씁니다. 지울까요?", "이미지 지우기", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { File.Delete(ScreenShots.PathOf(ScreenShots.Dir, slot)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _canvasNote.Text = $"화면을 지우지 못했습니다: {ex.Message}";
            return;
        }
        _s.SlotAreas[slot] = null;
        _pending = null;
        ResetBaseline();
        SettingsChanged?.Invoke();
        ShowCanvasShot();
        PreviewChanged();
    }
}
