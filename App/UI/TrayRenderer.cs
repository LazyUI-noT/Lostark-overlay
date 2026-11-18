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
    // Dimmed text is #8A98A6 (4.5:1 and up on the card); #7D8B99 was 4.3:1.
    static readonly Colors DeepTeal = new(Hex("#1B2734"), Hex("#2A3A4C"), Hex("#2A3A4C"), Hex("#E6EDF3"), Hex("#8A98A6"), Hex("#83E6E0"));
    static readonly Colors LostArk = new(Hex("#2A2C35"), Hex("#353845"), Hex("#1C1F2A"), Hex("#F1EEE8"), Hex("#B3AFA8"), Hex("#C9A472"));
    static readonly Colors Graphite = new(Hex("#25272B"), Hex("#34363C"), Hex("#34363C"), Hex("#ECECEE"), Hex("#9A9CA3"), Hex("#8AB4F8"));
    static readonly Colors ArkesiaNight = new(Hex("#1F2433"), Hex("#2E3446"), Hex("#2E3446"), Hex("#EDE7DA"), Hex("#A39C8E"), Hex("#D9B46A"));
    static readonly Colors Dark = new(Hex("#2B2B2B"), Hex("#3D3D3D"), Hex("#454545"), Hex("#FFFFFF"), Hex("#8A8A8A"), Hex("#FFFFFF"));

    readonly Colors _c;

    TrayRenderer(Colors c) : base(new Table(c)) { _c = c; RoundedEdges = false; }

    /// <summary>The renderer for <paramref name="theme"/>; Windows' own for 밝게, and for "Windows 설정 따름" in light mode.</summary>
    public static ToolStripRenderer For(UiTheme theme) => SystemInformation.HighContrast ? new ToolStripSystemRenderer() : theme switch
    {
        UiTheme.DeepTeal => new TrayRenderer(DeepTeal),
        UiTheme.LostArk => new TrayRenderer(LostArk),
        UiTheme.Graphite => new TrayRenderer(Graphite),
        UiTheme.ArkesiaNight => new TrayRenderer(ArkesiaNight),
        UiTheme.Dark => new TrayRenderer(Dark),
        UiTheme.System when Themes.AppsDark() => new TrayRenderer(Dark),
        _ => new ToolStripProfessionalRenderer(),
    };

    /// <summary>설정's measures on <paramref name="menu"/> and every submenu, in every theme (high contrast and 밝게 too): rows
    /// <see cref="Tokens.Row.Height"/> tall, and Windows 11's rounded corners (Windows 10 has none and keeps square ones).
    /// Applied as each menu opens, since submenus (직업 인식, 테스트 모드, 레이드) are refilled while the app runs.</summary>
    public static void Shape(ToolStripDropDown menu)
    {
        menu.Opening += (_, _) => Rows(menu);
        menu.HandleCreated += (_, _) => Round(menu);   // before the first frame, so a menu opened still is round from the start
        if (menu.IsHandleCreated) Round(menu);
    }

    /// <summary>Menus and items already hooked; entries go with the items when a submenu is refilled.</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> Hooked = [];

    static void Rows(ToolStripDropDown menu)
    {
        var row = MulDiv((int)Tokens.Row.Height, DpiAt(Cursor.Position), 96);
        foreach (ToolStripItem item in menu.Items)
        {
            if (item is not ToolStripMenuItem m) continue;
            m.Padding = Padding.Empty;
            var pad = Math.Max(0, row - m.GetPreferredSize(Size.Empty).Height);
            m.Padding = new Padding(0, pad / 2, 0, pad - pad / 2);
            // Hooked on the item, not on HasDropDownItems: a submenu filled while this menu is open (테스트 모드's window list)
            // is shaped when it opens. DropDownOpening fires before the submenu's own Opening.
            if (Hooked.TryAdd(m, Hooked))
                m.DropDownOpening += (_, _) => { if (Hooked.TryAdd(m.DropDown, Hooked)) Shape(m.DropDown); };
        }
    }

    /// <summary>The DPI of the monitor the menu opens on (the tray click), not the menu's own: before its window exists it
    /// still has the DPI it was made at, wrong on a second monitor with another scale.</summary>
    static int DpiAt(Point p) =>
        GetDpiForMonitor(MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */), 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0 ? (int)dpi : 96;

    static int MulDiv(int value, int numerator, int denominator) => (int)Math.Round(value * (double)numerator / denominator);

    // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2 (8 px, Tokens.Radius.Card): Windows 11 only; Windows 10 returns an
    // error and the corners stay square, which is the intended fallback (user, 2026-11-17).
    static void Round(ToolStripDropDown menu)
    {
        int round = 2;
        _ = DwmSetWindowAttribute(menu.Handle, 33, ref round, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint MonitorFromPoint(Point pt, uint flags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>The hovered row as 설정's controls: inset <see cref="Tokens.Space.Tight"/> from the sides, corners
    /// <see cref="Tokens.Radius.Control"/>, instead of WinForms' edge-to-edge box with a border.</summary>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var inset = e.Item.Owner?.LogicalToDeviceUnits((int)Tokens.Space.Tight) ?? (int)Tokens.Space.Tight;
        var r = new Rectangle(inset, 1, e.Item.Width - 2 * inset - 1, e.Item.Height - 3);   // -1: Right/Bottom are exclusive
        var d = 2 * (e.Item.Owner?.LogicalToDeviceUnits((int)Tokens.Radius.Control) ?? (int)Tokens.Radius.Control);
        if (r.Width <= d || r.Height <= d) return;
        using var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(_c.Hover);
        var state = e.Graphics.Save();   // the Graphics is shared with the text and arrow drawn next
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
        e.Graphics.Restore(state);
    }

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
