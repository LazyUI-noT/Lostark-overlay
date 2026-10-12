using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The tray menu in 설정's colours. WinForms draws menus light whatever Windows' mode, which stood out beside the dark
/// 설정 window; dark themes get their own colours here, the light theme keeps WinForms' look.</summary>
sealed class TrayRenderer : ToolStripProfessionalRenderer
{
    /// <summary>A menu's colours: background, hovered row, lines, text, dimmed text, check mark.</summary>
    sealed record Colors(Color Back, Color Hover, Color Line, Color Text, Color Dim, Color Check);

    // 딥 틸: the 설정 card, stroke and accent colours (UiPalette). 어둡게: Windows 11's dark menu greys.
    static readonly Colors DeepTeal = new(Hex("#1B2734"), Hex("#2A3A4C"), Hex("#2A3A4C"), Hex("#E6EDF3"), Hex("#7D8B99"), Hex("#83E6E0"));
    static readonly Colors Dark = new(Hex("#2B2B2B"), Hex("#3D3D3D"), Hex("#454545"), Hex("#FFFFFF"), Hex("#8A8A8A"), Hex("#FFFFFF"));

    readonly Colors _c;

    TrayRenderer(Colors c) : base(new Table(c)) { _c = c; RoundedEdges = false; }

    /// <summary>The renderer for <paramref name="theme"/>; Windows' own for 밝게, and for "Windows 설정 따름" in light mode.</summary>
    public static ToolStripRenderer For(UiTheme theme) => SystemInformation.HighContrast ? new ToolStripSystemRenderer() : theme switch
    {
        UiTheme.DeepTeal => new TrayRenderer(DeepTeal),
        UiTheme.Dark => new TrayRenderer(Dark),
        UiTheme.System when Themes.AppsDark() => new TrayRenderer(Dark),
        _ => new ToolStripProfessionalRenderer(),
    };

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _c.Text : _c.Dim;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled != false ? _c.Text : _c.Dim;
        base.OnRenderArrow(e);
    }

    /// <summary>A check mark in the accent colour: WinForms' own is a black glyph, lost on a dark menu.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (r.Width <= 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_c.Check, Math.Max(1.6f, r.Height / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
            new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f), new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f)]);
    }

    static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>WinForms' colour table with every surface in <see cref="Colors"/>: no gradients, no light image margin.</summary>
    sealed class Table(Colors c) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => c.Back;
        public override Color ImageMarginGradientBegin => c.Back;
        public override Color ImageMarginGradientMiddle => c.Back;
        public override Color ImageMarginGradientEnd => c.Back;
        public override Color MenuBorder => c.Line;
        public override Color MenuItemBorder => c.Hover;
        public override Color MenuItemSelected => c.Hover;
        public override Color MenuItemSelectedGradientBegin => c.Hover;
        public override Color MenuItemSelectedGradientEnd => c.Hover;
        public override Color MenuItemPressedGradientBegin => c.Hover;
        public override Color MenuItemPressedGradientEnd => c.Hover;
        public override Color SeparatorDark => c.Line;
        public override Color SeparatorLight => c.Line;
        public override Color CheckBackground => c.Back;
        public override Color CheckSelectedBackground => c.Hover;
        public override Color CheckPressedBackground => c.Hover;
    }
}
