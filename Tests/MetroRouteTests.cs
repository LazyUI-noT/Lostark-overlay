using GaugeOverlay.Core.Domain;
using Xunit;

namespace GaugeOverlay.Tests;

public class MetroRouteTests
{
    static readonly double[] Widths = [220, 200, 220, 210];
    static readonly string[] Row = ["party", "peers"];
    const double Part = 24, Note = 16, Inset = 8, Spacing = 6;

    /// <summary>The 기능 맵 laid out as FeatureMapView does, with <paramref name="open"/> boxes opened (heights as the plan's mock-up).</summary>
    static MetroLayout Map(params string[] open)
    {
        var openSet = open.ToHashSet();
        var inner = new List<MetroInner>();
        double Height(MapBox b, string outer, double dx, double dy, double width)
        {
            double h = MetroRoute.Head + (b.Note is null ? 0 : Note);
            if (!openSet.Contains(b.Id)) return h;
            h += b.Choices.Length * Part + (b.Choices.Length > 0 ? Spacing : 0);
            var kids = FeatureMap.Boxes.Where(k => k.Inside == b.Id).ToList();
            foreach (var k in kids)
            {
                double kh = Height(k, outer, dx + Inset, dy + h, width - 2 * Inset);
                inner.Add(new MetroInner(k.Id, outer, dx + Inset, dy + h, width - 2 * Inset, kh));
                h += kh + Spacing;
            }
            return h + (kids.Count > 0 ? Inset : 0);
        }
        var boxes = FeatureMap.Boxes.Where(b => b.Inside is null)
            .Select(b => new MetroBox(b.Id, b.Column, b.Id == "party" ? 62 : Height(b, b.Id, 0, 0, Widths[b.Column]), Row.Contains(b.Id))).ToList();
        var shownIds = boxes.Select(b => b.Id).Concat(inner.Select(i => i.Id)).ToHashSet();
        string? Shown(string id)
        {
            for (string? at = id; at is not null; at = FeatureMap.Boxes.First(b => b.Id == at).Inside)
                if (shownIds.Contains(at)) return at;
            return null;
        }
        var links = MetroRoute.Links(FeatureMap.Edges, Shown).Select(l => l.Link).ToList();
        return MetroRoute.Layout(Widths, boxes, inner, links);
    }

    public static TheoryData<string[]> Opens => new()
    {
        Array.Empty<string>(),
        FeatureMap.Boxes.Select(b => b.Id).ToArray(),
        new[] { "rec", "meter" },
        new[] { "aden", "boss", "game" },
    };

    [Theory]
    [MemberData(nameof(Opens))]
    public void No_line_runs_through_a_box_or_shares_a_stretch_with_another(string[] open)
    {
        var (through, shared, _) = MetroRoute.Check(Map(open));

        Assert.Equal(0, through);
        Assert.Equal(0, shared);
    }

    [Theory]
    [MemberData(nameof(Opens))]
    public void Boxes_in_a_column_keep_the_least_gap(string[] open)
    {
        var at = Map(open).At.Values.Where(r => r.Top).GroupBy(r => r.Column);

        foreach (var column in at)
        {
            var list = column.OrderBy(r => r.Y).ToList();
            for (int i = 1; i < list.Count; i++) Assert.True(list[i].Y - list[i - 1].Bottom >= MetroRoute.Gap - 0.01, $"column {column.Key}");
        }
    }

    [Fact]
    public void Lines_go_only_across_down_or_at_45_degrees()
    {
        foreach (var line in Map("rec").Lines)
        {
            var pts = MetroRoute.Chamfer(line.Points, 8);
            foreach (var (a, b) in pts.Zip(pts.Skip(1)))
            {
                double dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
                Assert.True(dx < 0.01 || dy < 0.01 || Math.Abs(dx - dy) < 0.01, $"{line.Link}: {a} → {b}");
            }
        }
    }

    [Fact]
    public void The_same_map_lays_out_the_same()
    {
        static string Text(MetroLayout l) => string.Join(";", l.Lines.Select(x => x.Link + string.Join(",", x.Points)))
            + string.Join(";", l.At.OrderBy(kv => kv.Key).Select(kv => kv.Key + kv.Value));

        Assert.Equal(Text(Map("aden")), Text(Map("aden")));
    }

    [Fact]
    public void Two_boxes_in_a_column_with_nothing_between_link_straight_down()
    {
        var map = Map();
        var line = map.Lines.Single(l => l.Link == new MetroLink("rec", "hw", true));
        MetroRect rec = map.At["rec"], hw = map.At["hw"];

        Assert.Equal([new MetroPoint(rec.X + rec.Width / 2, rec.Bottom), new MetroPoint(rec.X + rec.Width / 2, hw.Y)], line.Points);
    }

    [Fact]
    public void A_folded_box_takes_the_lines_of_the_boxes_inside_it_once()
    {
        var links = MetroRoute.Links(FeatureMap.Edges, id => id is "icons" or "buffrows" or "amsuline" or "zone" ? "rec" : id);

        var toRec = links.Where(l => l.Link.From == "skill" && l.Link.To == "rec").ToList();
        Assert.Single(toRec);
        Assert.Equal(2, toRec[0].Edges.Count);   // 사용 기록 and the skill icons
        Assert.DoesNotContain(links, l => l.Link.From == "party" || l.Link.To == "party");
    }

    [Fact]
    public void A_line_is_dashed_only_when_every_edge_in_it_is()
    {
        MapEdge[] edges = [new("a", "b", Optional: true), new("a", "b"), new("c", "d", Optional: true)];

        var links = MetroRoute.Links(edges, id => id);

        Assert.False(links[0].Link.Optional);
        Assert.True(links[1].Link.Optional);
    }

    [Fact]
    public void Party_boxes_stand_in_a_row_under_the_dashed_line()
    {
        var map = Map();
        double lowest = map.At.Where(kv => !Row.Contains(kv.Key)).Max(kv => kv.Value.Bottom);

        Assert.True(map.Separator > lowest);
        Assert.All(Row, id => Assert.True(map.At[id].Y > map.Separator));
        Assert.Equal(map.At["party"].Y, map.At["peers"].Y);
    }

    [Fact]
    public void A_corner_is_cut_back_along_both_sides()
    {
        var pts = MetroRoute.Chamfer([new(0, 0), new(20, 0), new(20, 20)], 8);

        Assert.Equal([new MetroPoint(0, 0), new MetroPoint(12, 0), new MetroPoint(20, 8), new MetroPoint(20, 20)], pts);
    }

    [Fact]
    public void A_short_side_cuts_the_corner_by_half_of_it()
    {
        var pts = MetroRoute.Chamfer([new(0, 0), new(4, 0), new(4, 20)], 8);

        Assert.Equal(new MetroPoint(2, 0), pts[1]);
        Assert.Equal(new MetroPoint(4, 2), pts[2]);
    }

    [Fact]
    public void A_repeated_point_is_dropped_and_a_straight_line_keeps_its_ends()
    {
        var pts = MetroRoute.Chamfer([new(0, 0), new(0.1, 0), new(30, 0)], 8);

        Assert.Equal([new MetroPoint(0, 0), new MetroPoint(30, 0)], pts);
    }

    // A tag 50 × 15 on a level line y 100, x 0–300: above its middle is (125, 83), below (125, 102).
    static readonly MetroPoint[] Level = [new(0, 100), new(300, 100)];
    static MetroRect Box(double x, double y, double w, double h) => new(x, y, w, h, 0);

    [Fact]
    public void A_tag_with_room_sits_above_the_middle_of_the_longest_level_stretch()
    {
        MetroPoint[] line = [new(0, 40), new(60, 40), new(70, 50), new(70, 100), new(300, 100)];

        Assert.Equal(new MetroPoint(160, 83), MetroRoute.TagSpot(line, 50, 15, []));
    }

    [Fact]
    public void A_tag_on_a_street_goes_below_when_the_box_above_would_cover_it()
    {
        var above = Box(100, 50, 100, 48);   // bottom 98: the street's top box ("사용 기록" on 스킬창 표시)

        Assert.Equal(new MetroPoint(125, 102), MetroRoute.TagSpot(Level, 50, 15, [above]));
    }

    [Fact]
    public void A_tag_slides_along_the_stretch_when_boxes_close_both_sides_of_the_middle()
    {
        MetroRect[] street = [Box(100, 50, 100, 48), Box(100, 102, 100, 40)];

        var at = MetroRoute.TagSpot(Level, 50, 15, street);

        Assert.True(at.X + 50 <= 98 || at.X >= 202, $"tag at {at} is between the boxes or touches one");
        Assert.InRange(at.X, 0, 250);   // still on the stretch
    }

    [Fact]
    public void A_tag_keeps_off_a_tag_placed_before_it()
    {
        var earlier = Box(125, 83, 50, 15);

        Assert.Equal(new MetroPoint(125, 102), MetroRoute.TagSpot(Level, 50, 15, [earlier]));
    }

    [Fact]
    public void A_tag_with_no_free_spot_sits_above_the_middle()
    {
        var all = Box(-50, 0, 400, 200);

        Assert.Equal(new MetroPoint(125, 83), MetroRoute.TagSpot(Level, 50, 15, [all]));
    }

    [Fact]
    public void A_vertical_line_hangs_its_tag_above_the_first_point()
    {
        Assert.Equal(new MetroPoint(-25, -17), MetroRoute.TagSpot([new(0, 0), new(0, 100)], 50, 15, []));
    }

    [Fact]
    public void A_one_point_line_centres_its_tag_above_the_point()
    {
        Assert.Equal(new MetroPoint(-15, -7), MetroRoute.TagSpot([new(10, 10)], 50, 15, []));
    }

    [Fact]
    public void A_line_with_no_points_is_refused()
    {
        Assert.Throws<ArgumentException>(() => MetroRoute.TagSpot([], 50, 15, []));
    }
}
