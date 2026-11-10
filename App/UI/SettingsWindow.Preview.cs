using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    readonly DispatcherTimer _previewDebounce = new() { Interval = PreviewDelay };
    IReadOnlyList<OverlayPart> _previewParts = [];
    OverlayCanvas? _preview;
    bool _previewHooked;
    bool? _previewBeside;   // null: not placed

    /// <summary>The page shown: its preview placed and rebuilt, or taken away when the page sets no window.</summary>
    void ShowPreview()
    {
        HookPreview();
        _previewDebounce.Stop();   // a change typed just before leaving: no rebuild for the page left
        _previewParts =SettingsPreview.PartsOf(_area.Name, _area.Shown);
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
        _previewPane.Children.Add(Note("미리보기 · 예시 전투"));
        _previewPane.Children.Add(_previewHost);
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
        var preview = _preview = new OverlayCanvas(copy, _chars, scene: true) { IsOn = part => TrayToggles.IsShown(_s, part), Only = _previewParts };
        preview.Play.Auto = () => PreviewPlayer.Sections(copy, _chars);
        preview.ScenePicked += part => GoTo(PlaceOfPart(part));   // the gauge in 스킬창's preview: to its row
        System.Windows.Automation.AutomationProperties.SetName(preview, $"{_area.Name} 미리보기");
        preview.Loaded += (_, _) =>
        {
            // ponytail: decodes the saved shot per rebuild (debounced); cache it per profile if rebuilds show up in [perf].
            if (SavedShot() is var (picture, area)) preview.ShowPicture(picture, area);
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
}
