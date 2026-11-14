using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The look pages' preview (settings-design.plan.md §1): the windows the page sets, drawn by the real renderer on a
/// copy of the settings and playing the demo fight, beside the form when the page is wide enough, else above the rows.</summary>
public partial class SettingsWindow
{
    const double PreviewBesideHeight = 260, PreviewAboveHeight = 200;
    const double FormSides = 48;   // Form's margin, left and right (SettingsWindow.xaml)
    const double ScrollbarRoom = 18;   // Body's vertical scrollbar, beside the form
    static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(300);   // typing in a box rebuilds once, not per key

    readonly Border _previewHost = new() { CornerRadius = new(6), ClipToBounds = true };
    readonly StackPanel _previewPane = new() { VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock _previewNote = Note("");   // names the click only on a page where a click does something
    readonly DispatcherTimer _previewDebounce = new() { Interval = PreviewDelay };
    IReadOnlyList<OverlayPart> _previewParts = [];
    OverlayCanvas? _preview;
    bool _previewHooked;
    bool? _previewBeside;   // null: not placed
    const string DefaultBackColour = "#202020";
    readonly ComboBox _previewBack = new() { MinWidth = 120 };   // 게임 화면 / 그림 파일… / 단색
    readonly Button _previewFile = new() { Content = "다른 그림…", Margin = new(8, 0, 0, 0), Padding = new(10, 2, 10, 2) };
    Button? _previewPlace;   // 위치 조정, hidden on a page whose windows the 레이아웃 편집기 doesn't hold
    UIElement? _previewColour;   // its colour row, shown for 단색
    string _backColour = DefaultBackColour;   // the last 단색 colour, kept while another 배경화면 is picked
    bool _previewBackSetting;   // the combo set from the settings, not by the user

    /// <summary>The page shown: its preview placed and rebuilt, or taken away when the page sets no window.</summary>
    void ShowPreview()
    {
        HookPreview();
        SyncPreviewBack();   // 되돌리기 may have changed 배경화면
        _previewDebounce.Stop();   // a change typed just before leaving: no rebuild for the page left
        _previewParts = SettingsPreview.PartsOf(_area.Name, _area.Shown);
        _previewPlace!.Visibility = Shown(EditorPlace() is not null);
        _previewNote.Text = _previewParts.Any(p => p is not (OverlayPart.Meter or OverlayPart.BarCount or OverlayPart.Party))
            ? "미리보기 · 예시 전투 · 창을 누르면 (또는 Space) 다음 단계로"   // OverlayCanvas.PlaysOnClick's windows
            : "미리보기 · 예시 전투";
        PreviewGone();
        if (_previewParts.Count == 0)
        {
            _preview?.Play.Stop();
            (_preview, _previewHost.Child) = (null, null);
            return;
        }
        SectionPage.Children.Add(_previewPane);
        PlacePreview();
        BuildPreview();
    }

    /// <summary>No preview: Body is the page's last child again and fills it.</summary>
    void PreviewGone()
    {
        Detach(_previewPane);
        _previewBeside = null;
        (Body.Width, Body.HorizontalAlignment) = (double.NaN, HorizontalAlignment.Stretch);
    }

    void HookPreview()
    {
        if (_previewHooked) return;
        _previewHooked = true;
        _previewPane.Children.Add(_previewNote);
        _previewPane.Children.Add(_previewHost);
        AddPreviewControls();
        _previewHost.SetResourceReference(Border.BorderBrushProperty, StrokeBrushKey);
        _previewHost.BorderThickness = new(1);
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            if (_previewPane.Parent is not null && IsVisible) BuildPreview();   // not for a page without one, nor a hidden 설정
        };
        _previewPane.IsVisibleChanged += (_, _) =>   // another page, the canvas page or 설정 hidden: nothing plays unseen
        {
            if (_preview is null) return;
            if (_previewPane.IsVisible) _preview.Play.Start();
            else _preview.Play.Stop();
        };
        SectionPage.SizeChanged += (_, _) => { if (_previewPane.Parent is not null) PlacePreview(); };
    }

    /// <summary>Beside: Body narrowed to the form (its scrollbar against the form, not mid-page) and the preview right after it.
    /// Above: the preview docked under the header, Body filling the rest.</summary>
    void PlacePreview()
    {
        bool beside = SettingsPreview.Beside(SectionPage.ActualWidth - FormSides - ScrollbarRoom);
        // Above: as wide as the form under it (left alone, it sizes to the canvas, a thumbnail).
        if (!beside) _previewPane.Width = Math.Max(0, Math.Min(FormMaxWidth, SectionPage.ActualWidth - FormSides - ScrollbarRoom));
        if (_previewBeside == beside) return;   // a resize that doesn't cross the line: nothing moves
        _previewBeside = beside;
        // DockPanel fills with its last child: beside, the preview is last; above, Body is.
        SectionPage.Children.Remove(_previewPane);
        SectionPage.Children.Insert(beside ? SectionPage.Children.Count : SectionPage.Children.IndexOf(Body), _previewPane);
        DockPanel.SetDock(Body, Dock.Left);   // ignored while Body is last
        DockPanel.SetDock(_previewPane, Dock.Top);   // ignored while the preview is last
        (Body.Width, Body.HorizontalAlignment) = beside ? (FormMaxWidth + FormSides + ScrollbarRoom, HorizontalAlignment.Left) : (double.NaN, HorizontalAlignment.Stretch);
        if (beside) _previewPane.Width = SettingsPreview.PreviewWidth;
        _previewPane.HorizontalAlignment = HorizontalAlignment.Left;
        _previewPane.Margin = beside ? new(SettingsPreview.Gap - ScrollbarRoom, 12, 24, 0) : new(24, 8, 24, 0);
        _previewHost.Height = beside ? PreviewBesideHeight : PreviewAboveHeight;
    }

    /// <summary>A fresh copy of the settings drawn (the 기능 맵 scene's), only the page's windows, playing while seen.</summary>
    void BuildPreview()
    {
        _preview?.Play.Stop();
        var copy = SceneSettings();
        var preview = _preview = new OverlayCanvas(copy, _chars, scene: true) { IsOn = part => TrayToggles.IsShown(_s, part), Only = _previewParts, ClickPlays = true };
        preview.Play.Auto = () => PreviewPlayer.Gauge(copy, _chars, PreviewTarget);
        preview.SetTarget(PreviewTarget);   // 편집 중's 직업각인: its boxes and gauge, and the 기력·충격 창 only for a class with a ring
        System.Windows.Automation.AutomationProperties.SetName(preview, $"{_area.Name} 미리보기");
        preview.Loaded += (_, _) =>
        {
            // ponytail: decodes the picture per rebuild (debounced); cache it if rebuilds show up in [perf].
            ShowPreviewBack(preview);
            preview.Relayout();
            if (_previewPane.IsVisible) preview.Play.Start();
        };
        _previewHost.Child = preview;
    }

    /// <summary>A setting changed: the preview redraws once the changes pause.</summary>
    void PreviewChanged()
    {
        if (_preview is null) return;
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    /// <summary>Under the preview: 위치 조정 (the 레이아웃 편집기 at the page's first window) and the 배경화면 choice.</summary>
    void AddPreviewControls()
    {
        var place = new Button { Content = "위치 조정", Padding = new(10, 2, 10, 2) };
        place.Click += (_, _) => { if (EditorPlace() is { } at) GoTo(at); };
        foreach (var name in new[] { "게임 화면", "그림 파일", "단색" }) _previewBack.Items.Add(name);
        _previewBack.SelectionChanged += (_, _) => PreviewBackPicked();
        _previewFile.Click += (_, _) => PickPreviewPicture();
        var label = Label("배경화면");
        (label.Margin, label.VerticalAlignment) = (new(0, 0, 8, 0), VerticalAlignment.Center);
        System.Windows.Automation.AutomationProperties.SetLabeledBy(_previewBack, label);   // a hand-built row: FormRow would do this
        var back = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        back.Children.Add(label);
        back.Children.Add(_previewBack);
        back.Children.Add(_previewFile);
        var row = new DockPanel { Margin = new(0, 8, 0, 0) };
        DockPanel.SetDock(place, Dock.Left);
        row.Children.Add(place);
        row.Children.Add(back);
        _previewPane.Children.Add(row);
        _previewPlace = place;
        if (IsColour(_s.PreviewBack)) _backColour = _s.PreviewBack!;
        SyncPreviewBack();
    }

    /// <summary>위치 조정: the page's first window that the 레이아웃 편집기 holds, picked there. The 미터기 isn't in it (it
    /// moves on screen), so its page has no button.</summary>
    Place? EditorPlace() => _previewParts.Select(part => part switch
    {
        OverlayPart.Meter => (Place?)null,
        OverlayPart.BarCount => new Place(AreaOverlay, "레이드 줄 수 창", new("Raid", null, null)),
        OverlayPart.Party => new Place(AreaOverlay, "파티원 창", new("Party", null, null)),
        _ => PlaceOfPart(part),
    }).FirstOrDefault(p => p is not null);

    static bool IsColour(string? back) => back is not null && back.StartsWith('#') && UiColor.TryParse(back) is not null;
    static bool IsPicture(string? back) => back is not null && !back.StartsWith('#') && File.Exists(back);

    /// <summary>The combo, 다른 그림… and the colour row show the saved 배경화면 (also after 되돌리기); a bad colour or a picture
    /// gone shows as 게임 화면, which is what the preview then draws.</summary>
    void SyncPreviewBack()
    {
        var back = _s.PreviewBack;
        if (IsColour(back)) _backColour = back!;
        _previewBackSetting = true;
        _previewBack.SelectedIndex = IsColour(back) ? 2 : IsPicture(back) ? 1 : 0;
        _previewBackSetting = false;
        _previewFile.Visibility = Shown(_previewBack.SelectedIndex == 1);
        _previewFile.ToolTip = IsPicture(back) ? back : null;
        // Rebuilt each time so its box shows the colour in use, not the one it was built with.
        int at = _previewColour is null ? _previewPane.Children.Count : _previewPane.Children.IndexOf(_previewColour);
        if (_previewColour is not null) _previewPane.Children.Remove(_previewColour);
        _previewColour = ColorRow(Label("배경 색"), _backColour, c => _s.PreviewBack = _backColour = c, swatch: false);
        _previewColour.Visibility = Shown(_previewBack.SelectedIndex == 2);
        _previewPane.Children.Insert(at, _previewColour);
    }

    void PreviewBackPicked()
    {
        if (_previewBackSetting) return;
        if (_previewBack.SelectedIndex == 1) { PickPreviewPicture(); return; }
        SetPreviewBack(_previewBack.SelectedIndex == 2 ? _backColour : null);
    }

    /// <summary>그림 파일 (or 다른 그림…): a picture file; cancelled or unreadable, the old choice stays.</summary>
    void PickPreviewPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "그림 파일 (png, jpg, bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true };
        SetPreviewBack(dialog.ShowDialog(this) == true && ScreenShots.CanLoad(dialog.FileName) ? dialog.FileName : _s.PreviewBack);
    }

    void SetPreviewBack(string? back)
    {
        bool changed = back != _s.PreviewBack;
        _s.PreviewBack = back;
        SyncPreviewBack();
        if (changed) Changed();
    }

    /// <summary>배경화면: one colour, the picked picture (where a screenshot of its shape would lie), else the 배치's saved
    /// game screen (also for a bad colour or a picture gone, as <see cref="SyncPreviewBack"/> shows).</summary>
    void ShowPreviewBack(OverlayCanvas preview)
    {
        var back = _s.PreviewBack;
        if (IsColour(back) && UiColor.TryParse(back!) is { } c)
        {
            preview.ShowColour(c);
            return;
        }
        if (IsPicture(back) && OverlayCanvas.Decode(new Uri(back!)) is { } picture)
        {
            preview.ShowPicture(picture, ScreenShots.AreaFor(picture.PixelWidth, picture.PixelHeight, GameMonitor()));
            return;
        }
        if (SavedShot() is var (shot, area)) preview.ShowPicture(shot, area);
    }
}
