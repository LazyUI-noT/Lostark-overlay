using System.Text.Json;
using System.Text.Json.Nodes;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.Core.Data;

/// <summary>A 직업각인's 아덴 게이지 as its own pixels (Profiles/aden/README.md). <see cref="Ref"/> is where the mask
/// sits in reference pixels (1920×1080, HUD 110%), so <see cref="Geometry.ToScreen"/> places it like a slot.</summary>
public sealed record GaugeDef(PixelRect Ref, GaugeShape Shape, bool Pieces, int Tolerance)
{
    /// <summary>The fill, 0–1, of a capture of <see cref="Ref"/> on screen at any size.</summary>
    public double Read(Image capture)
    {
        var img = capture.Width == Shape.Width && capture.Height == Shape.Height ? capture : Scale(capture, Shape.Width, Shape.Height);
        return Pieces ? (double)GaugeReader.ReadPieces(img, Shape, Tolerance) / Shape.Pieces : GaugeReader.ReadContinuous(img, Shape, Tolerance);
    }

    /// <summary>The fill on a whole screenshot whose game area and skill bar fit are <paramref name="vp"/>, <paramref name="fit"/>.</summary>
    public double ReadOn(Image screen, Viewport vp, Fit fit) => Read(BuffReader.Crop(screen, Geometry.ToScreen(Ref, vp, fit)));

    /// <summary>Box-averaged down (or nearest up) to <paramref name="w"/> × <paramref name="h"/>.</summary>
    static Image Scale(Image src, int w, int h)
    {
        var dst = new Image(w, h);
        for (int y = 0; y < h; y++)
        {
            int y0 = y * src.Height / h, y1 = Math.Max(y0 + 1, (y + 1) * src.Height / h);
            for (int x = 0; x < w; x++)
            {
                int x0 = x * src.Width / w, x1 = Math.Max(x0 + 1, (x + 1) * src.Width / w), n = (y1 - y0) * (x1 - x0), b = 0, g = 0, r = 0;
                for (int sy = y0; sy < y1; sy++)
                    for (int i = sy * src.Stride + x0 * 4, e = sy * src.Stride + x1 * 4; i < e; i += 4)
                    { b += src.Bgra[i]; g += src.Bgra[i + 1]; r += src.Bgra[i + 2]; }
                int d = y * dst.Stride + x * 4;
                (dst.Bgra[d], dst.Bgra[d + 1], dst.Bgra[d + 2], dst.Bgra[d + 3]) = ((byte)(b / n), (byte)(g / n), (byte)(r / n), 255);
            }
        }
        return dst;
    }
}

/// <summary>Reads Profiles/aden/&lt;클래스&gt;[_&lt;직업각인&gt;]/gauge.json and its mask.</summary>
public static class GaugeLoader
{
    public const string JsonFile = "gauge.json", MaskFile = "mask.png";
    const int MaxSlack = 10;

    /// <summary>The 직업각인's own folder, else the class's (shared by both), else null.</summary>
    public static string? Folder(string profilesDir, string className, string subclass) =>
        new[] { $"{className}_{subclass}", className }.Select(n => Path.Combine(profilesDir, "aden", n))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, JsonFile)));

    /// <summary>--build-gauge: the pixels that change between <paramref name="empty"/> and <paramref name="full"/>
    /// (reference screenshots, 1920×1080 at HUD 110%) inside <paramref name="region"/>, cut to their bounding box, and a
    /// gauge.json for them: pieces when the gauge is several separate parts, else continuous left → right (edit the
    /// order if it fills another way).</summary>
    public static (string Json, Image Mask) Build(Image empty, Image full, PixelRect region, int threshold = 40)
    {
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 || (long)region.X + region.Width > Math.Min(empty.Width, full.Width)
            || (long)region.Y + region.Height > Math.Min(empty.Height, full.Height))
            throw new InvalidDataException($"--build-gauge: the region {region} is not inside the {full.Width}×{full.Height} shots.");
        var all = GaugeReader.Mask(BuffReader.Crop(empty, region), BuffReader.Crop(full, region), threshold);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < all.Height; y++)
            for (int x = 0; x < all.Width; x++)
                if (all.Bgra[y * all.Stride + x * 4 + 3] != 0) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        if (x1 < 0) throw new InvalidDataException("--build-gauge: nothing differs between the empty and full shots in the search area.");
        var mask = BuffReader.Crop(all, new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        bool pieces = GaugeReader.FromMask(mask).Pieces > 1;
        var json = new JsonObject
        {
            ["origin"] = new JsonArray(region.X + x0, region.Y + y0),
            ["units"] = pieces ? "pieces" : "continuous",
            ["order"] = new JsonObject { ["kind"] = "linear", ["angleDeg"] = 0 },
            ["tolerance"] = threshold,
        };
        return (json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), mask);
    }

    /// <summary>The definition in <paramref name="json"/> over <paramref name="mask"/> (opaque = gauge). Throws
    /// <see cref="InvalidDataException"/> for anything missing or unknown.</summary>
    public static GaugeDef Parse(string json, Image mask)
    {
        static InvalidDataException Bad(string what) => new($"gauge.json: {what}.");
        JsonObject o;
        try { o = JsonNode.Parse(json) as JsonObject ?? throw Bad("not an object"); }
        catch (JsonException e) { throw Bad(e.Message); }

        static double Num(JsonNode? n, string what) =>
            n is JsonValue v && v.TryGetValue<double>(out var d) ? d : throw Bad($"{what} must be a number");
        static (double X, double Y) Point(JsonNode? n, string what) =>
            n is JsonArray { Count: 2 } a ? (Num(a[0], what), Num(a[1], what)) : throw Bad($"{what} must be [x, y]");
        static bool Clockwise(JsonNode? n) =>
            n is null || (n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : throw Bad("clockwise must be true or false"));

        var origin = Point(o["origin"], "origin");
        var shape = GaugeReader.FromMask(mask);
        if (shape.Pixels.Length == 0) throw Bad("the mask has no gauge pixels");
        bool pieces = (o["units"] as JsonValue)?.ToString() switch
        {
            "continuous" => false,
            "pieces" => true,
            _ => throw Bad("units must be continuous or pieces"),
        };
        if (o["order"] is JsonObject order)
            shape = (order["kind"] as JsonValue)?.ToString() switch
            {
                "linear" => GaugeReader.Linear(shape, order["angleDeg"] is null ? 0 : Num(order["angleDeg"], "angleDeg")),
                "angular" when Point(order["center"], "center") is var c => GaugeReader.Angular(shape, c.X, c.Y,
                    order["startDeg"] is null ? 90 : Num(order["startDeg"], "startDeg"), Clockwise(order["clockwise"])),
                "piecewise" => GaugeReader.Piecewise(shape, order["angleDeg"] is null ? 90 : Num(order["angleDeg"], "angleDeg")),
                "rings" => GaugeReader.Rings(shape, order["startDeg"] is null ? 90 : Num(order["startDeg"], "startDeg"), Clockwise(order["clockwise"])),
                _ => throw Bad("order.kind must be linear, angular, piecewise or rings"),
            };
        static int Colour(JsonNode? n) =>
            n is JsonValue v && v.TryGetValue<string>(out var hex) && hex is ['#', _, _, _, _, _, _]
                && int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb)
                ? rgb : throw Bad("filling colours must be \"#RRGGBB\"");
        if (o["filling"] is { } also)
            shape = shape with { Filling = also is JsonArray a ? [.. a.Select(Colour)] : throw Bad("filling must be a list") };
        if (o["slack"] is { } slack)
            shape = shape with { Slack = Num(slack, "slack") is var n && n is >= 0 and <= MaxSlack && n == Math.Floor(n) ? (int)n : throw Bad($"slack must be a whole number from 0 to {MaxSlack}") };
        int tolerance = o["tolerance"] is null ? 40 : (int)Num(o["tolerance"], "tolerance");
        return new GaugeDef(new PixelRect((int)Math.Round(origin.X), (int)Math.Round(origin.Y), mask.Width, mask.Height), shape, pieces, tolerance);
    }
}
