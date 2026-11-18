using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>The grid lines inside 읽는 자리's boxes (reading-place.plan.md C), from the same <see cref="Geometry"/> the reads use, so
/// they follow the resolution, the ratio, the fit (HUD 크기) and the size settings as the boxes do: the skill slots, the buff and
/// debuff rows' columns and icon/timer line, the boss row's first-debuff split and its slots, the boss bar's ends and text line,
/// and the 전분 panel's timer, title line, cells and rows. 던전, 캐릭터, 단심 have no inner layout the reads use: none.</summary>
public static class ReadingGrid
{
    /// <param name="own">The class's own 아덴1/아덴2 spots (classes.json "adenRects", 발키리 and 브레이커), as the reader uses.</param>
    public static IReadOnlyList<GridLine> Of(string id, Viewport vp, Fit fit, int buffSize, int buffCount, int bossSize, PixelRect? panel,
        IReadOnlyDictionary<SlotId, PixelRect>? own = null) => id switch
    {
        "skill" => [.. SlotInfo.All.SelectMany(s => Frame(Geometry.Slot(s, vp, fit, own)))],
        "buff" => Row(Geometry.BuffBoxes(vp, fit, buffSize, buffCount)),
        DetectPlace.DebuffRow => Row(Geometry.DebuffBoxes(vp, fit, buffSize)),
        "amsu" => BossSlots(vp, fit, bossSize),
        "boss" => BossBar(vp, fit),
        "g-jeonbun" when panel is { } p => Jeonbun(p),
        _ => [],
    };

    static IEnumerable<GridLine> Frame(PixelRect r) =>
        [new(r.X, r.Y, r.Right, r.Y), new(r.X, r.Bottom, r.Right, r.Bottom), new(r.X, r.Y, r.X, r.Bottom), new(r.Right, r.Y, r.Right, r.Bottom)];

    /// <summary>A buff or debuff row: each box's icon and its timer under it, framed where the game draws them (the timer read
    /// itself looks a little wider, DebuffReader).</summary>
    static IReadOnlyList<GridLine> Row(IReadOnlyList<Geometry.BuffBox> boxes) =>
        [.. boxes.SelectMany(b => Frame(b.Icon).Concat(Frame(b.Timer)))];

    /// <summary>The boss debuff row: a frame per slot the size shows, from where the party's debuffs start (the boss's own buffs
    /// grow left of it), its icon and the timer under it. The 암수 walk follows the icons it finds, so these are where they are
    /// expected, not a fixed read.</summary>
    static IReadOnlyList<GridLine> BossSlots(Viewport vp, Fit fit, int bossSize) =>
        [.. Geometry.BossDebuffSlots(vp, fit, bossSize).Concat(Geometry.BossBuffSlots(vp, fit, bossSize))   // buffs: left of the slash
            .SelectMany(s => Frame(s.Icon).Concat(Frame(s.Timer)))];

    /// <summary>The boss bar's text row: the bar's ends that fall inside it (a 16:9 raid bar starts left of the row, which is cut
    /// for a 21:9 bar too), and the top and bottom of its "current/max" text line.</summary>
    static IReadOnlyList<GridLine> BossBar(Viewport vp, Fit fit)
    {
        var (row, bar, text) = (Geometry.BossTextRow(vp, fit), Geometry.BossBar(vp, fit), Geometry.BossHpText(vp, fit));
        int Down(int y) => Math.Clamp(y, row.Y, row.Bottom);
        return [.. new[] { bar.X, bar.Right }.Where(x => x > row.X && x < row.Right).Select(x => new GridLine(x, row.Y, x, row.Bottom)),
            new(row.X, Down(text.Y), row.Right, Down(text.Y)), new(row.X, Down(text.Bottom), row.Right, Down(text.Bottom))];
    }

    /// <summary>The 전분 panel's layout (<see cref="JeonbunReader"/>), scaled by the box's height as the reader scales it, the columns
    /// equal across its width (<see cref="JeonbunLayout.Fixed"/>: the game draws them equal; the read measures them) and the timer
    /// from the last edge.</summary>
    static IReadOnlyList<GridLine> Jeonbun(PixelRect p)
    {
        double s = p.Height / JeonbunReader.RefH;
        var edges = JeonbunLayout.Fixed(p.Width, s).Edges;   // crop px
        int X(double x) => p.X + (int)Math.Round(x);
        int Y(double y) => p.Y + (int)Math.Round(y * s);
        var (t, title, rows) = (JeonbunReader.TimerLayout, JeonbunReader.TitleLayout, JeonbunReader.RowLayout);
        double last = JeonbunReader.CellEdges[^1], tx0 = edges[^1] - (last - t.X0) * s, tx1 = edges[^1] - (last - t.X1) * s;
        var lines = Frame(new PixelRect(X(tx0), Y(t.Y0), X(tx1) - X(tx0), Y(t.Y1) - Y(t.Y0))).ToList();
        foreach (var y in new[] { title.Y0, title.Y1 }) lines.Add(new(X(edges[0]), Y(y), X(edges[^1]), Y(y)));
        foreach (var (y0, y1) in rows)
        {
            lines.Add(new(X(edges[0]), Y(y0), X(edges[^1]), Y(y0)));
            lines.Add(new(X(edges[0]), Y(y1), X(edges[^1]), Y(y1)));
        }
        lines.AddRange(edges.Select(x => new GridLine(X(x), Y(rows[0].Y0), X(x), Y(rows[^1].Y1))));
        return lines;
    }
}
