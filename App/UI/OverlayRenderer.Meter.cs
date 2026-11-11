using System.Windows;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The DPS 미터기 window (spec § C Display): a header (전분 timer, boss damage), then the members as 행 (a row per member,
/// its share as the row's background bar, ▲▼ over 10 s, the number, DPS), 세로 막대, 선 그래프, 한 줄 막대 or 파티 비교 (1파티
/// from the left, 2파티 from the right); 서포터 · 찬조 and 기타 under the dealers, an optional DPS graph, and 버프 적용률 (how much
/// of my damage fell under 공증 / 아덴 피증 / 초각스 피증) as three bars at the bottom. The panel keeps one
/// size per scope (4 rows, 8 for the 공대), so rows coming and going don't move the window.</summary>
public sealed partial class OverlayRenderer
{
    const double MeterWidth = 6.2, MeterRow = 0.5, MeterGraph = 1.4;   // × a skill box, × the window's 너비 / 높이
    const double MeterSkillRow = 0.7;   // × a row
    static readonly Color MeterMine = Color.FromRgb(0xFF, 0xC8, 0x3C), MeterDealer = Color.FromRgb(0x5B, 0x8D, 0xEF),
        MeterSupport = Color.FromRgb(0x5B, 0xD1, 0x8B), MeterOtherColor = Color.FromRgb(0x6B, 0x72, 0x80);

    /// <summary>The DPS 미터기 alone.</summary>
    public bool MeterOnly { get; set; }

    /// <summary>My row's id (drawn in my colour); 0 alone.</summary>
    public ulong MeterMe { get; set; }

    (double W, double Row) MeterUnit => (BoxSize(Settings.Meter.Width, MeterWidth), BoxSize(Settings.Meter.Height, MeterRow));

    int MeterRows => Settings.Meter.Scope switch { MeterScope.나 => 1, MeterScope.파티 => 4, _ => 8 };

    Size MeterPanelSize()
    {
        var (w, row) = MeterUnit;
        var m = Settings.Meter;
        double body = m.ShownLook switch
        {
            MeterLook.한줄막대 or MeterLook.파티비교 => row * 3,
            MeterLook.세로막대 or MeterLook.선그래프 => row * 6,
            _ => row * (MeterRows + (m.Support ? 1 : 0) + (m.Other ? 1 : 0)),
        };
        double graph = m.Graph && m.ShownLook == MeterLook.행 ? BoxSize(m.Height, MeterGraph) : 0;
        return new(w + 2 * Bleed, row + MeterSkillHeight(row) + body + graph + (BuffBarsShown ? row * BuffBarRows : 0) + 2 * Bleed);
    }

    /// <summary>스킬 사용 횟수 has a skill picked for the character played.</summary>
    double MeterSkillHeight(double row) => _player?.Snapshot.MeterView?.Skill is null ? 0 : row * MeterSkillRow;

    /// <summary>스킬 사용 횟수 under the header, in every look: mine alone, "—" dimmed with nothing to count from.</summary>
    void MeterSkillDraw(DrawingContext dc, Rect r, MeterSkillLine skill)
    {
        string rate = skill.PerMinute is { } n ? $"{n:0.0}회/분" : "—";
        MeterText(dc, new(r.X, r.Y), skill.Name, r.Height * 0.68, 0.85);
        MeterTextRight(dc, new(r.Right, r.Y), rate, r.Height * 0.68, skill.PerMinute is null ? 0.45 : 1);
    }

    void DrawMeter(DrawingContext dc)
    {
        var m = Settings.Meter;
        var size = MeterPanelSize();
        var (w, row) = MeterUnit;
        var panel = new Rect(Bleed, Bleed, w, size.Height - 2 * Bleed);
        dc.PushOpacity(MeterSettings.ClampOpacity(m.Opacity));
        dc.DrawRoundedRectangle(Solid(UiColor.Parse(m.Background, Color.FromArgb(0xCC, 0x10, 0x14, 0x18))), null, panel, 4, 4);
        var view = _player?.Snapshot.MeterView;
        _ghostShown = null;
        double bars = BuffBarsShown ? row * BuffBarRows : 0, skill = MeterSkillHeight(row);
        if (view?.Skill is { } line) MeterSkillDraw(dc, new Rect(panel.X + 6, panel.Y + row, w - 12, skill), line);
        var body = new Rect(panel.X + 6, panel.Y + row + skill, w - 12, panel.Height - row - skill - 4 - bars);
        if (view is null || !view.PanelFound && view.Rows.Count == 0)
            MeterText(dc, body.TopLeft, "전분 없음 · 전투 중에 전분 창을 찾습니다", row * 0.42, 0.75);
        else
            switch (m.ShownLook)
            {
                case MeterLook.세로막대: MeterColumns(dc, body, view); break;
                case MeterLook.선그래프: MeterLines(dc, body, view); break;
                case MeterLook.한줄막대: MeterStrip(dc, body, view); break;
                case MeterLook.파티비교: MeterTug(dc, body, view); break;
                default: MeterRowsDraw(dc, body, view, row); break;
            }
        // Lost for now: the last figures stay, marked; else the line graph drew a 비교 line (known only once the body is drawn).
        string? note = view is { PanelFound: false, Rows.Count: > 0 } ? "전분 가려짐" : _ghostShown is { } how ? MeterCompare.Label(how) : null;
        double noteX = panel.X + w * 0.32, noteSize = row * 0.4;
        if (note is not null) MeterText(dc, new(noteX, panel.Y + row * 0.25), note, noteSize, 0.7);
        MeterHeader(dc, new Rect(panel.X + 6, panel.Y, w - 12, row), view, note is null || noteSize < MinTimerFont ? null : noteX + Text(note, noteSize).Width);
        dc.Pop();
        if (bars <= 0) return;
        // Their own 투명도 (profile-looks.plan.md), outside the panel's; on the panel's background, under none of the body.
        dc.PushOpacity(MeterSettings.ClampOpacity(m.BarOpacity ?? m.Opacity));
        MeterBuffBars(dc, new Rect(body.X, body.Bottom + 2, body.Width, bars - 2), view);
        dc.Pop();
    }

    /// <summary>How the 비교 line drawn this frame was picked; null when none was drawn.</summary>
    MeterCompare.Kind? _ghostShown;

    /// <summary>The timer at the left; the boss and my share at the right, the name shortened with "…" so it ends before the
    /// timer or the note (<paramref name="noteEnd"/>: 전분 가려짐, 비교) and the figure after it stays whole.</summary>
    void MeterHeader(DrawingContext dc, Rect r, MeterView? v, double? noteEnd)
    {
        string timer = v?.Timer is { } t ? $"{t / 60:00}:{t % 60:00}" : "--:--";
        string state = v is null ? "" : v.Running ? "▶" : "■";
        string left = $"{state} {timer}";
        double y = r.Y + r.Height * 0.2, timerSize = r.Height * 0.5, size = r.Height * 0.45;
        MeterText(dc, new(r.X, y), left, timerSize, 1);
        if (v?.Header(MeterMe, Eok) is not var (name, after) || size < MinTimerFont) return;
        double from = Math.Max(r.X + Text(left, timerSize).Width, noteEnd ?? 0) + size * 0.6;
        var tail = Text(after, size);
        double room = r.Right - tail.Width - from;
        dc.PushOpacity(0.8);
        dc.DrawText(tail, new(r.Right - tail.Width, y));
        if (room >= Text("…", size).Width)   // no room even for "…": the figure alone
        {
            var boss = Text(name, size);
            (boss.MaxTextWidth, boss.MaxLineCount, boss.Trimming) = (room, 1, TextTrimming.CharacterEllipsis);
            dc.DrawText(boss, new(r.Right - tail.Width - boss.Width, y));
        }
        dc.Pop();
    }

    Color MeterColor(MeterRow r)
    {
        var m = Settings.Meter;
        if (r.Id == MeterMe) return UiColor.TryParse(m.MyColor) ?? MeterMine;
        return r.Role == MeterRole.서포터 ? UiColor.TryParse(m.SupportColor) ?? MeterSupport : UiColor.TryParse(m.DealerColor) ?? MeterDealer;
    }

    void MeterRowsDraw(DrawingContext dc, Rect body, MeterView v, double row)
    {
        var m = Settings.Meter;
        double y = body.Y;
        var supports = v.Rows.Where(r => r.Role == MeterRole.서포터).ToList();
        foreach (var r in v.Rows.Where(r => r.Role == MeterRole.딜러).Take(MeterRows))
        {
            MeterRowDraw(dc, new Rect(body.X, y, body.Width, row - 2), r, row);
            y += row;
        }
        if (m.Support && supports.Count > 0)
        {
            MeterText(dc, new(body.X, y + row * 0.15), "서포터 · 찬조", row * 0.38, 0.6);
            double each = body.Width * 0.62 / supports.Count;
            for (int i = 0; i < supports.Count; i++)
            {
                var s = supports[i];
                var cell = new Rect(body.X + body.Width * 0.38 + i * each, y + row * 0.15, each - 4, row * 0.7);
                dc.DrawRectangle(Solid(Color.FromArgb(0x60, 0, 0, 0)), null, cell);
                if (s.Share is { } sh) dc.DrawRectangle(Solid(MeterColor(s)), null, cell with { Width = cell.Width * Math.Clamp(sh, 0, 1) });
                MeterText(dc, new(cell.X + 3, cell.Y), $"{s.Name} {Value(s)}", row * 0.36, 1);
            }
            y += row;
        }
        if (m.Other && v.Other is { } o)
        {
            var cell = new Rect(body.X, y, body.Width, row - 2);
            dc.DrawRectangle(Solid(UiColor.TryParse(m.OtherColor) ?? MeterOtherColor), null, cell with { Width = cell.Width * Math.Clamp(o, 0, 1) });
            MeterText(dc, new(cell.X + 4, cell.Y + row * 0.18), "기타", row * 0.45, 0.85);
            MeterTextRight(dc, new(cell.Right - 4, cell.Y + row * 0.18), $"{o * 100:0.0}%", row * 0.45, 0.85);
            y += row;
        }
        if (m.Graph) MeterLines(dc, new Rect(body.X, y + 2, body.Width, Math.Max(0, body.Bottom - y - 2)), v);
    }

    void MeterRowDraw(DrawingContext dc, Rect cell, MeterRow r, double row)
    {
        var c = MeterColor(r);
        dc.DrawRectangle(Solid(Color.FromArgb(0x50, 0, 0, 0)), null, cell);
        if (r.Share is { } s) dc.DrawRectangle(Solid(Color.FromArgb(0xB0, c.R, c.G, c.B)), null, cell with { Width = cell.Width * Math.Clamp(s, 0, 1) });
        string party = r.Party == 2 && Settings.Meter.Scope == MeterScope.파티별 ? "2 · " : "";
        MeterText(dc, new(cell.X + 4, cell.Y + row * 0.16), party + r.Name, row * 0.46, 1);
        if (Emblems.Class(r.ClassId)?.Name is { } cls) MeterText(dc, new(cell.X + cell.Width * 0.36, cell.Y + row * 0.24), cls, row * 0.34, 0.6);
        string arrow = r.Trend > 0 ? "▲ " : r.Trend < 0 ? "▼ " : "";
        MeterTextRight(dc, new(cell.Right - 4, cell.Y + row * 0.1), arrow + Value(r), row * 0.55, 1);
        if (r.Damage >= MeterMath.DashUnder)
            MeterTextRight(dc, new(cell.Right - cell.Width * 0.3, cell.Y + row * 0.24), $"{Eok(r.Dps)}/초", row * 0.32, 0.65);
    }

    /// <summary>The row's number as 수치 says: % with one decimal, or 피해량 / DPS in 억; "–" under the dash rule.</summary>
    string Value(MeterRow r) => Settings.Meter.Value switch
    {
        MeterValue.피해량 => r.Damage >= MeterMath.DashUnder ? Eok(r.Damage) : "–",
        MeterValue.DPS => r.Damage >= MeterMath.DashUnder ? Eok(r.Dps) : "–",
        _ => r.Share is { } s ? $"{s * 100:0.0}%" : "–",
    };

    static string Eok(long v) => v >= 1_000_000_000_000 ? $"{v / 1e12:0.0}조" : $"{v / 1e8:0.0}억";

    void MeterColumns(DrawingContext dc, Rect body, MeterView v)
    {
        var rows = v.Rows.Take(MeterRows).ToList();
        if (rows.Count == 0) return;
        double each = body.Width / rows.Count, label = body.Height * 0.25;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            double h = (body.Height - label) * Math.Clamp(r.Share ?? 0, 0, 1);
            dc.DrawRectangle(Solid(MeterColor(r)), null, new Rect(body.X + i * each + 2, body.Bottom - label - h, Math.Max(0, each - 4), h));
            MeterText(dc, new(body.X + i * each + 2, body.Bottom - label), r.Name, label * 0.4, 1);
            MeterText(dc, new(body.X + i * each + 2, body.Bottom - label * 0.5), Value(r), label * 0.38, 0.8);
        }
    }

    void MeterStrip(DrawingContext dc, Rect body, MeterView v)
    {
        double x = body.X, h = body.Height * 0.45;
        foreach (var r in v.Rows.Where(r => r.Role == MeterRole.딜러 && r.Share is not null))
        {
            double w = body.Width * Math.Clamp(r.Share!.Value, 0, 1);
            dc.DrawRectangle(Solid(MeterColor(r)), new Pen(Solid(Colors.Black), 1), new Rect(x, body.Y, w, h));
            if (w > h * 2) MeterText(dc, new(x + 3, body.Y + h * 0.2), $"{r.Name} {Value(r)}", h * 0.4, 1);
            x += w;
        }
        if (v.Other is { } o)
            dc.DrawRectangle(Solid(UiColor.TryParse(Settings.Meter.OtherColor) ?? MeterOtherColor), null, new Rect(x, body.Y, Math.Max(0, Math.Min(body.Right - x, body.Width * o)), h));
    }

    /// <summary>파티 비교: 1파티 from the left, 2파티 from the right, in % or 피해량.</summary>
    void MeterTug(DrawingContext dc, Rect body, MeterView v)
    {
        double Sum(int party) => v.Rows.Where(r => r.Party == party && r.Role == MeterRole.딜러)
            .Sum(r => Settings.Meter.CompareAsShare ? r.Share ?? 0 : r.Damage);
        double a = Sum(1), b = Sum(2), total = a + b, h = body.Height * 0.45;
        if (total <= 0) return;
        double split = body.Width * a / total;
        dc.DrawRectangle(Solid(MeterDealer), null, new Rect(body.X, body.Y, split, h));
        dc.DrawRectangle(Solid(MeterSupport), null, new Rect(body.X + split, body.Y, body.Width - split, h));
        string Fmt(double x) => Settings.Meter.CompareAsShare ? $"{x * 100:0.0}%" : Eok((long)x);
        MeterText(dc, new(body.X + 4, body.Y + h * 0.2), $"1파티 {Fmt(a)}", h * 0.42, 1);
        MeterTextRight(dc, new(body.Right - 4, body.Y + h * 0.2), $"{Fmt(b)} 2파티", h * 0.42, 1);
    }

    /// <summary>Each dealer's DPS over this 전분 record (그래프 범위: or its last minute), one line each, and always mine (alone, or as a support). The scale is a
    /// round step (GridScale), labelled at the left in 억/초; the time lines fall on round steps, labelled m:ss below. A graph
    /// too small for its labels draws its lines alone.</summary>
    void MeterLines(DrawingContext dc, Rect area, MeterView v)
    {
        if (area.Height < 8) return;
        dc.DrawRectangle(Solid(Color.FromArgb(0x40, 0, 0, 0)), null, area);
        var lines = v.Lines(MeterMe, MeterRows).Select(r => (Row: r, Points: v.History.Series(r.Id))).ToList();
        var latest = lines.Where(s => s.Points.Count > 0).Select(s => s.Points[^1].At).DefaultIfEmpty().Max();
        var series = lines.Select(s => (s.Row, Points: MeterHistory.Shown(s.Points, Settings.Meter.Range, latest))).Where(s => s.Points.Count > 1).ToList();
        if (series.Count == 0) { MeterText(dc, new(area.X + 4, area.Y + 4), "DPS 그래프: 전투가 시작되면 그립니다", Math.Min(area.Height * 0.3, 14), 0.6); return; }
        var t0 = series.Min(s => s.Points[0].At);
        var tEnd = series.Max(s => s.Points[^1].At);
        double span = Math.Max(1, (tEnd - t0).TotalSeconds);
        double font = Math.Min(area.Height / (series.Count * 1.3 + 0.5), 12);   // the names fit the graph's height
        // 비교 (lostark-api.plan.md phase 6): the saved fight's my-DPS on the seconds shown, the scale fitting it too.
        var ghost = v.Ghost?.Points.Where(p => p.At >= t0 && p.At <= tEnd).ToList() ?? [];
        // 최근 1분 (user, 2026-11-11): the scale sits around my own line's lowest and highest, so its swing shows instead of
        // lying flat on a scale from 0; the other lines are cut at the plot's edge.
        var around = Settings.Meter.Range == MeterRange.최근1분 ? series.FirstOrDefault(s => s.Row.Id == MeterMe).Points : null;
        bool zoomed = around is { Count: > 1 };
        double step, bottom = 0, top;
        if (zoomed) (step, bottom, top) = GridScale.Around(around!.Min(p => (double)p.Dps), around!.Max(p => (double)p.Dps));
        else (step, top) = GridScale.Values(Math.Max(series.Max(s => s.Points.Max(p => (double)p.Dps)), ghost.Select(p => (double)p.Dps).DefaultIfEmpty().Max()));
        double labelW = Text(GridScale.Label(top) + "/초", font).Width;   // the widest label is the top's
        bool labels = GridScale.LabelsFit(area.Height, area.Width, font, labelW);
        var plot = labels ? new Rect(area.X + labelW + 6, area.Y, Math.Max(1, area.Width - labelW - 6), Math.Max(0, area.Height - font * 1.3)) : area;
        var grid = new Pen(Solid(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);   // round DPS steps, and round time steps
        double lastLabel = double.MaxValue;   // labels from the bottom up; one too close to the last is left out (its line stays)
        double Y(double value) => plot.Bottom - plot.Height * (value - bottom) / (top - bottom);
        foreach (double value in GridScale.Lines(step, bottom, top))
        {
            double gy = Math.Round(Y(value)) + 0.5;
            dc.DrawLine(grid, new(plot.X, gy), new(plot.Right, gy));
            double ly = Math.Max(area.Y, gy - font * 0.6);   // the top one stays inside: the skill line is above it
            if (!labels || lastLabel - ly < font * 1.2) continue;
            MeterTextRight(dc, new(plot.X - 4, ly), GridScale.Label(value) + "/초", font, 0.6);
            lastLabel = ly;
        }
        double timeStep = GridScale.Seconds(span, plot.Width, 6 * font);
        foreach (double sec in GridScale.Ticks(t0.TotalSeconds, span, timeStep))
        {
            double gx = Math.Round(plot.X + plot.Width * (sec - t0.TotalSeconds) / span) + 0.5;
            dc.DrawLine(grid, new(gx, plot.Y), new(gx, plot.Bottom));
            string at = $"{(int)sec / 60}:{(int)sec % 60:00}";
            if (labels && gx + 2 + Text(at, font).Width <= plot.Right) MeterText(dc, new(gx + 2, plot.Bottom + 1), at, font, 0.6);
        }
        Point At(TimeSpan at, long dps) => new(plot.X + plot.Width * (at - t0).TotalSeconds / span, Y(dps));
        dc.PushClip(new RectangleGeometry(plot));   // a line off the scale (최근 1분 follows mine) stops at the plot's edge
        if (v.Ghost is { } compare)
        {
            if (ghost.Count > 1)   // under the live lines, dashed in my colour
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    // Off the 최근 1분 scale it runs flat along the plot's top or bottom edge, which says where it went
                    // (user, 2026-11-11); a pixel inside, so the clip doesn't halve it.
                    Point Held(TimeSpan at, long dps) { var p = At(at, dps); return new(p.X, Math.Clamp(p.Y, plot.Y + 1, Math.Max(plot.Y + 1, plot.Bottom - 1))); }
                    g.BeginFigure(Held(ghost[0].At, ghost[0].Dps), false, false);
                    foreach (var p in ghost.Skip(1)) g.LineTo(Held(p.At, p.Dps), true, false);
                }
                var mine = series.FirstOrDefault(s => s.Row.Id == MeterMe).Row;
                var color = mine is null ? Colors.White : MeterColor(mine);
                dc.DrawGeometry(null, new Pen(Solid(Color.FromArgb(0xB0, color.R, color.G, color.B)), 1.2) { DashStyle = DashStyles.Dash }, geo);
                _ghostShown = compare.How;   // its label goes in the header (DrawMeter): the plot's corners hold the scale and the names
            }
        }
        var names = new List<(double Y, string Name)>();
        foreach (var (r, points) in series)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                for (int i = 0; i < points.Count; i++)
                {
                    double value = points[i].Dps;
                    var pt = new Point(plot.X + plot.Width * (points[i].At - t0).TotalSeconds / span, Y(value));
                    if (i == 0) g.BeginFigure(pt, false, false); else g.LineTo(pt, true, false);
                }
            dc.DrawGeometry(null, new Pen(Solid(MeterColor(r)), 1.5), geo);
            // 최근 1분 (user, 2026-11-11): the line's current DPS is written at its end, after the name (the rows show the
            // whole 전분 record's figure, not this).
            string name = Settings.Meter.Range == MeterRange.최근1분 ? $"{r.Name} {GridScale.Label(points[^1].Dps)}" : r.Name;
            names.Add((Math.Clamp(Y(points[^1].Dps), plot.Y, plot.Bottom), name));
        }
        dc.Pop();
        double y = plot.Y;   // whose line: names just above the line ends (not on them), top down, kept apart
        if (font < MinTimerFont) return;   // too small to draw: no empty backings
        foreach (var (end, name) in names.OrderBy(n => n.Y))
        {
            y = Math.Max(y, Math.Min(end - font * 1.3, plot.Bottom - font * 1.3));
            // Pushed down by the names above, a name can land on another line: a dark backing keeps it readable.
            double nameW = Text(name, font).Width;
            dc.DrawRoundedRectangle(Solid(Color.FromArgb(0xB0, 0x10, 0x14, 0x18)), null, new Rect(plot.Right - 5 - nameW, y, nameW + 4, font * 1.25), 2, 2);
            MeterTextRight(dc, new(plot.Right - 3, y), name, font, 0.85);
            y += font * 1.25;
        }
    }

    const double BuffBarRows = 2.4;   // × a row: three bars

    /// <summary>버프 적용률 is on and I'm not on a support (the view says so once the meter has read).</summary>
    bool BuffBarsShown => Settings.Meter.BuffShare() == ThreeWay.켜기 && _player?.Snapshot.MeterView?.ShowBuffBars != false;

    /// <summary>버프 적용률: the share of my damage dealt while each of my three buffs was on (TimelineView's colours); with two
    /// or more dealers sharing, 버프 분배 instead: each buff's bar is 100% of the damage dealt under it, split by dealer (who
    /// made the most of it). A note while the buff row isn't read (설정 → 버프 off).</summary>
    void MeterBuffBars(DrawingContext dc, Rect r, MeterView? v)
    {
        double each = r.Height / 3, font = each * 0.75, label = r.Width * 0.3;
        if (v?.BuffSplit is { } split) { MeterBuffSplit(dc, r, v, split, each, font, label); return; }
        // A 4-man raid: the party's share (boss HP lost under the buff) in brackets after mine; a support sees the party's alone.
        var party = v?.BuffParty;
        if ((v?.BuffCover ?? party) is not { } cover) { MeterText(dc, r.TopLeft, "버프 적용률: 설정 → 버프를 켜면 보입니다", font, 0.6); return; }
        bool mine = v!.BuffCover is not null;
        double text = font * (mine && party is not null ? 6 : 3.2);
        for (int i = 0; i < TimelineView.Buffs.Length && i < cover.Count; i++)
        {
            var (_, name, color) = TimelineView.Buffs[i];
            double y = r.Y + i * each, share = Math.Clamp(cover[i], 0, 1);
            MeterText(dc, new(r.X, y), name, font, 0.8);
            var bar = new Rect(r.X + label, y + each * 0.15, Math.Max(0, r.Width - label - text), each * 0.7);
            dc.DrawRectangle(Solid(Color.FromArgb(0x50, 0, 0, 0)), null, bar);
            dc.DrawRectangle(Solid(color), null, bar with { Width = bar.Width * share });
            string figure = !mine ? $"({share * 100:0}%)"
                : party is not null && i < party.Count ? $"{share * 100:0}% ({Math.Clamp(party[i], 0, 1) * 100:0}%)" : $"{share * 100:0}%";
            MeterTextRight(dc, new(r.Right, y), figure, font, 0.9);
        }
    }

    void MeterBuffSplit(DrawingContext dc, Rect r, MeterView v, IReadOnlyList<IReadOnlyList<(ulong Id, string Name, double Share)>> split,
        double each, double font, double label)
    {
        for (int i = 0; i < TimelineView.Buffs.Length && i < split.Count; i++)
        {
            var (_, name, color) = TimelineView.Buffs[i];
            double y = r.Y + i * each;
            MeterText(dc, new(r.X, y), name, font, 0.8);
            var bar = new Rect(r.X + label, y + each * 0.1, r.Width - label, each * 0.8);
            dc.DrawRectangle(Solid(Color.FromArgb(0x50, 0, 0, 0)), new Pen(Solid(Color.FromArgb(0x90, color.R, color.G, color.B)), 1), bar);
            double x = bar.X;
            foreach (var (id, who, share) in split[i])
            {
                double w = bar.Width * share;
                var c = v.Rows.FirstOrDefault(row => row.Id == id) is { } row ? MeterColor(row) : MeterDealer;
                dc.DrawRectangle(Solid(c), new Pen(Solid(Color.FromArgb(0xC0, 0, 0, 0)), 1), new Rect(x, bar.Y, w, bar.Height));
                string text = $"{who} {share * 100:0}%";
                if (w > font * text.Length * 0.6) MeterText(dc, new(x + 2, y + each * 0.05), text, font * 0.9, 1);
                else if (w > font * 1.8) MeterText(dc, new(x + 2, y + each * 0.05), $"{share * 100:0}", font * 0.9, 1);
                x += w;
            }
        }
    }

    void MeterText(DrawingContext dc, Point at, string s, double size, double opacity)
    {
        if (size < MinTimerFont) return;
        dc.PushOpacity(opacity);
        dc.DrawText(Text(s, size), at);
        dc.Pop();
    }

    void MeterTextRight(DrawingContext dc, Point topRight, string s, double size, double opacity)
    {
        if (size < MinTimerFont) return;
        var t = Text(s, size);
        dc.PushOpacity(opacity);
        dc.DrawText(t, new Point(topRight.X - t.Width, topRight.Y));
        dc.Pop();
    }
}
