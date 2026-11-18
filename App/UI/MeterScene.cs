using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The DPS 미터기 recorded once per data change (overlay-motion.plan.md phase 5): everything but its bars as frozen
/// drawings, in draw order, with each bar between them as a value and the rectangle it makes, so a frame redraws only the
/// bars (eased, <see cref="OverlayRenderer"/>) over the same drawings. The meter draws through this as through a
/// <see cref="DrawingContext"/>; a section's opacity is pushed live, around its items, as the meter pushed it before.
/// A bar drawn at its value makes the very calls the meter made before, so a still meter draws the same pixels.</summary>
sealed class MeterScene
{
    /// <summary>A bar: its <paramref name="Key"/> (what it shows, kept across records), the value it shows now and the
    /// rectangle a value makes (the meter's own expression).</summary>
    public sealed record Bar(object Key, double Value, Func<double, Rect> RectOf, Brush Fill);

    readonly List<(double Opacity, List<object> Items)> _sections = [];
    DrawingGroup? _group;
    DrawingContext? _dc;
    int _depth;   // pushes open in the current drawing: a bar can't cut one in two

    public IEnumerable<Bar> Bars => _sections.SelectMany(s => s.Items.OfType<Bar>());

    /// <summary>What follows draws under <paramref name="opacity"/>, until the next section.</summary>
    public void Section(double opacity)
    {
        Flush();
        _sections.Add((opacity, []));
    }

    /// <summary>The recording is done.</summary>
    public void End() => Flush();

    public void Add(object key, double value, Func<double, Rect> rectOf, Brush fill)
    {
        Debug.Assert(_depth == 0, "a meter bar inside a push");
        Flush();
        _sections[^1].Items.Add(new Bar(key, value, rectOf, fill));
    }

    /// <summary>The meter as recorded, each bar at its glide's value at <paramref name="ms"/> (its recorded value when it has
    /// none). True while any bar still moves.</summary>
    public bool Draw(DrawingContext dc, IReadOnlyDictionary<object, Glide> glides, double ms, double durationMs)
    {
        bool moving = false;
        foreach (var (opacity, items) in _sections)
        {
            dc.PushOpacity(opacity);
            foreach (var item in items)
            {
                if (item is not Bar b) { dc.DrawDrawing((Drawing)item); continue; }
                double value = b.Value;
                if (glides.TryGetValue(b.Key, out var g))
                {
                    value = g.Value(ms, durationMs);
                    moving |= g.Moving(ms, durationMs);
                }
                dc.DrawRectangle(b.Fill, null, b.RectOf(value));
            }
            dc.Pop();
        }
        return moving;
    }

    // ── DrawingContext's calls the meter makes ──
    public void DrawRectangle(Brush? brush, Pen? pen, Rect r) => Dc.DrawRectangle(brush, pen, r);
    public void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect r, double rx, double ry) => Dc.DrawRoundedRectangle(brush, pen, r, rx, ry);
    public void DrawText(FormattedText text, Point at) => Dc.DrawText(text, at);
    public void DrawLine(Pen pen, Point a, Point b) => Dc.DrawLine(pen, a, b);
    public void DrawGeometry(Brush? brush, Pen? pen, Geometry g) => Dc.DrawGeometry(brush, pen, g);
    public void PushOpacity(double opacity) { Dc.PushOpacity(opacity); _depth++; }
    public void PushClip(Geometry clip) { Dc.PushClip(clip); _depth++; }
    public void Pop() { Dc.Pop(); _depth--; }

    DrawingContext Dc
    {
        get
        {
            if (_dc is not null) return _dc;
            _group = new DrawingGroup();
            return _dc = _group.Open();
        }
    }

    void Flush()
    {
        if (_dc is null) return;
        Debug.Assert(_depth == 0, "a push left open in the meter");
        _dc.Close();
        _group!.Freeze();
        _sections[^1].Items.Add(_group);
        (_dc, _group) = (null, null);
    }
}
