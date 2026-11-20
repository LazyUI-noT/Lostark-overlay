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

    readonly Border _previewHost = new() { CornerRadius = new(Tokens.Radius.Card), ClipToBounds = true };
    readonly StackPanel _previewPane = new() { VerticalAlignment = VerticalAlignment.Top };
    readonly TextBlock _previewNote = Note("");   // names the click only on a page where a click does something
    readonly DispatcherTimer _previewDebounce = new() { Interval = PreviewDelay };
    IReadOnlyList<OverlayPart> _previewParts = [];
    OverlayCanvas? _preview;
    bool _previewHooked;
    bool? _previewBeside;   // null: not placed
    const string DefaultBackColour = "#202020";
    readonly ComboBox _previewBack = new() { MinWidth = 120 };   // 게임 화면 / 단색
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
            if (_preview?.Pressed == true) { _previewDebounce.Start(); return; }   // not under a drag: the new canvas would drop it
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

    const double PreviewEdge = 24;   // the preview to the page's edge

    /// <summary>Beside: Body narrowed to the form (its scrollbar against the form, not mid-page) and the preview right after it.
    /// Above: the preview docked under the header, Body filling the rest.</summary>
    void PlacePreview()
    {
        double page = SectionPage.ActualWidth - FormSides - ScrollbarRoom;
        bool beside = SettingsPreview.Beside(page);
        // Above: as wide as the form under it (left alone, it sizes to the canvas, a thumbnail).
        if (!beside) _previewPane.Width = Math.Max(0, Math.Min(FormMaxWidth, page));
        // On every resize: the form takes what the preview, its gap and its right margin leave.
        else Body.Width = SettingsPreview.FormBeside(SectionPage.ActualWidth - FormSides - PreviewEdge) + FormSides + ScrollbarRoom;
        if (_previewBeside == beside) return;   // a resize that doesn't cross the line: nothing moves
        _previewBeside = beside;
        // DockPanel fills with its last child: beside, the preview is last; above, Body is.
        SectionPage.Children.Remove(_previewPane);
        SectionPage.Children.Insert(beside ? SectionPage.Children.Count : SectionPage.Children.IndexOf(Body), _previewPane);
        DockPanel.SetDock(Body, Dock.Left);   // ignored while Body is last
        DockPanel.SetDock(_previewPane, Dock.Top);   // ignored while the preview is last
        if (!beside) Body.Width = double.NaN;
        Body.HorizontalAlignment = beside ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        if (beside) _previewPane.Width = SettingsPreview.PreviewWidth;
        _previewPane.HorizontalAlignment = HorizontalAlignment.Left;
        _previewPane.Margin = beside ? new(SettingsPreview.Gap - ScrollbarRoom, 12, PreviewEdge, 0) : new(PreviewEdge, 8, PreviewEdge, 0);
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
        HookPreviewView(preview, copy);
        preview.Loaded += (_, _) =>
        {
            // ponytail: decodes the picture per rebuild (debounced); cache it if rebuilds show up in [perf].
            var picture = ShowPreviewBack(preview);
            preview.Relayout();
            StartPreviewView(preview, picture);
            if (_previewPane.IsVisible) preview.Play.Start();
        };
        _previewHost.Child = preview;   // the minimap sits beside the controls, never over the picture
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
        var place = new Button { Content = "위치 조정", Padding = new(Tokens.Space.Gap, Tokens.Space.Hair, Tokens.Space.Gap, Tokens.Space.Hair) };
        place.Click += (_, _) => { if (EditorPlace() is { } at) GoTo(at); };
        foreach (var name in new[] { "게임 화면", "단색" }) _previewBack.Items.Add(name);
        _previewBack.SelectionChanged += (_, _) => PreviewBackPicked();
        var label = Label("배경화면");
        (label.Margin, label.VerticalAlignment) = (new(0, 0, 8, 0), VerticalAlignment.Center);
        System.Windows.Automation.AutomationProperties.SetLabeledBy(_previewBack, label);   // a hand-built row: FormRow would do this
        var back = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        back.Children.Add(label);
        back.Children.Add(_previewBack);
        place.Margin = new(0, 0, Tokens.Space.Gap, 0);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { place, UndoBar() } };
        _previewPane.Children.Add(PreviewViewControls());
        _previewPane.Children.Add(new StackPanel { Margin = new(0, Tokens.Space.Gap, 0, 0), Children = { left, new Border { Height = Tokens.Space.Gap }, back } });
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

    /// <summary>The combo, 다른 그림… and the colour row show the saved 배경화면 (also after 되돌리기); a bad colour
    /// shows as 게임 화면, which is what the preview then draws.</summary>
    void SyncPreviewBack()
    {
        var back = _s.PreviewBack;
        if (IsColour(back)) _backColour = back!;
        _previewBackSetting = true;
        _previewBack.SelectedIndex = IsColour(back) ? 1 : 0;
        _previewBackSetting = false;
        // Rebuilt each time so its box shows the colour in use, not the one it was built with.
        int at = _previewColour is null ? _previewPane.Children.Count : _previewPane.Children.IndexOf(_previewColour);
        if (_previewColour is not null) _previewPane.Children.Remove(_previewColour);
        _previewColour = ColorRow(Label("배경 색"), _backColour, c => _s.PreviewBack = _backColour = c, swatch: false);
        _previewColour.Visibility = Shown(_previewBack.SelectedIndex == 1);
        _previewPane.Children.Insert(at, _previewColour);
    }

    void PreviewBackPicked()
    {
        if (_previewBackSetting) return;
        SetPreviewBack(_previewBack.SelectedIndex == 1 ? _backColour : null);
    }

    void SetPreviewBack(string? back)
    {
        bool changed = back != _s.PreviewBack;
        _s.PreviewBack = back;
        SyncPreviewBack();
        if (changed) Changed();
    }

    /// <summary>배경화면: one colour, else the picture of the image slot the profile shows (also for a bad colour, as
    /// <see cref="SyncPreviewBack"/> shows); an empty slot shows the sample screen. Returns where the picture lies on screen
    /// (DIP; null: a saved picture with no area, a backdrop only).</summary>
    ViewRect? ShowPreviewBack(OverlayCanvas preview)
    {
        var back = _s.PreviewBack;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(preview);
        ViewRect? Rect(PixelRect? area, bool saved) => PreviewView.PictureRect(area, saved, dpi.DpiScaleX, dpi.DpiScaleY,
            SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);   // the canvas's screen (OverlayCanvas._screen)
        if (IsColour(back) && UiColor.TryParse(back!) is { } c)
        {
            preview.ShowColour(c);
            return Rect(null, saved: false);
        }
        if (SavedShot() is not var (shot, area)) return Rect(null, saved: false);
        preview.ShowPicture(shot, area);
        return Rect(area, saved: true);
    }
}
