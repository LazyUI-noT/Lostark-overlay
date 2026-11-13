namespace GaugeOverlay.Core.Domain;

/// <summary>합쳐서 / 따로 (class-profiles.plan.md "합쳐서 / 따로"): the sizes the gauge window and the plate share, and the
/// one-time <see cref="Migrate"/> that keeps the skill boxes still on screen when the gauge leaves the plate. The switch itself
/// is <c>Buffs.Detached</c> (<see cref="ProfileEntry.BuffsDetached"/>, per profile per class).</summary>
public static class SplitLayout
{
    /// <summary>The gauge's length at scale 1: the fixed grid's width (7 boxes and 6 gaps).</summary>
    public const double GaugeLengthAtOne = 7 * FreeLayout.Box + 6 * FreeLayout.Gap;

    /// <summary>A box edge in DIPs for a Width/Height setting (<paramref name="min"/>: a party member's received size may be fitted lower).</summary>
    public static double BoxSize(double setting, double share = 1, double min = ClassProfiles.MinScale) =>
        Math.Round(FreeLayout.Box * Math.Clamp(setting, min, ClassProfiles.MaxScale) * share);

    /// <summary>The gauge's thickness in DIPs: the 두께 setting follows the box height <paramref name="boxHeight"/>, at least 2.</summary>
    public static double GaugeHeight(double thickness, double boxHeight) => Math.Max(2, Math.Round(thickness * boxHeight / FreeLayout.Box));

    /// <summary>The gauge window's length in DIPs for its length scale (<see cref="IWindowPlace.Width"/>).</summary>
    public static double GaugeLength(double scale) => Math.Round(GaugeLengthAtOne * Math.Clamp(scale, ClassProfiles.MinScale, ClassProfiles.MaxScale));

    /// <summary>The Width (length) and Height (thickness) scales that make the gauge window as big as the gauge was drawn on the plate:
    /// <paramref name="length"/> DIPs long, with the skill boxes <paramref name="boxHeight"/> DIPs tall (the thickness follows them).</summary>
    public static (double W, double H) HomeScales(double length, double boxHeight) =>
        (Math.Round(Math.Clamp(length / GaugeLengthAtOne, ClassProfiles.MinScale, ClassProfiles.MaxScale), 2),
         Math.Round(Math.Clamp(boxHeight / FreeLayout.Box, ClassProfiles.MinScale, ClassProfiles.MaxScale), 2));

    /// <summary>The gauge window's size on screen: length × thickness, swapped when upright.</summary>
    public static (double W, double H) GaugeSize(GaugeStyle g)
    {
        var (length, thickness) = (GaugeLength(g.Width), GaugeHeight(g.Thickness, BoxSize(g.Height)));
        return g.Upright ? (thickness, length) : (length, thickness);
    }

    /// <summary>Runs once on a file from before 合쳐서 / 따로 (<see cref="Settings.SplitMigrated"/>), after the class migration.
    /// Bars upright beside the plate open 따로 too (the buff window stays where it was). Wherever 따로 is on and the class draws
    /// a gauge (<paramref name="gaugeKeys"/>: the 클래스 · 직업각인 with gauge data, and the entry's gauge on), the skill window's
    /// <c>Top</c> moves down by the gauge's strip so the boxes stay put: the live settings, each profile's shared entry and
    /// every class entry by its own gauge. A class following a shared entry gets an entry of its own with the shift (the shared
    /// one also serves classes without a gauge, so it is not moved). An unplaced (NaN) spot stays default. False when already done.</summary>
    /// <remarks>ponytail: the free layout counts every non-buff part not hidden, whatever its group's switch; the 열린 쪽 bars' strip
    /// width isn't added to <c>Left</c> (the plate moves a few DIP left).</remarks>
    public static bool Migrate(Settings s, IReadOnlySet<string> gaugeKeys)
    {
        if (s.SplitMigrated) return false;
        s.SplitMigrated = true;
        s.Buffs.Detached |= s.Buffs.Side;
        if (s.Buffs.Detached) s.Overlay.Top = Moved(s.Overlay.Top, Shift(s, s.PlaceKey, gaugeKeys, s.Gauge.Thickness, s.Overlay.Height));
        foreach (var p in s.Profiles) MigrateProfile(s, p, gaugeKeys);
        return true;
    }

    static void MigrateProfile(Settings s, OverlayProfile p, IReadOnlySet<string> gaugeKeys)
    {
        p.Classes ??= [];
        foreach (var e in p.Entries()) e.BuffsDetached |= e.BuffsSide;
        if (p.BuffsDetached)
            foreach (var key in gaugeKeys.Where(k => !p.Classes.ContainsKey(k) && Shift(s, k, gaugeKeys, p.GaugeThickness, p.Overlay.Height) > 0))
                p.Classes[key] = OverlayProfile.Clone(p);
        foreach (var (key, e) in p.Classes)
        {
            if (!e.BuffsDetached) continue;
            e.Overlay.Top = Moved(e.Overlay.Top, Shift(s, key, gaugeKeys, e.GaugeThickness, e.Overlay.Height));
            p.SubclassPlaces[key] = WindowPlace.Of(e.Overlay);   // the 0.22 mirror
        }
    }

    static double Moved(double top, double shift) => double.IsFinite(top) ? top + shift : top;

    /// <summary>How far the skill boxes sit below the window's top without the gauge: its strip in the fixed grid, the drop of the
    /// topmost box under the gauge cell in a free layout. 0 for a class without a gauge or with it off.</summary>
    static double Shift(Settings s, string? key, IReadOnlySet<string> gaugeKeys, double thickness, double heightScale)
    {
        if (key is null || !gaugeKeys.Contains(key)) return 0;
        var a = ClassProfiles.Mine(s, key);
        if (!a.Gauge) return 0;
        double boxH = BoxSize(heightScale);
        if (a.Layout is not { } cells) return GaugeHeight(thickness, boxH) + FreeLayout.Gap;
        var all = FreeLayout.Default(thickness);
        foreach (var (part, cell) in cells) all[part] = cell;
        double scale = boxH / FreeLayout.Box;
        double Top(IEnumerable<Part> parts) => Math.Round(parts.Min(part => all[part].Y) * scale);
        var drawn = all.Keys.Where(part => part == Part.변화 || part < Part.게이지 && !a.Hidden.Contains((SlotId)part)).ToList();
        return drawn.Count == 0 ? 0 : Top(drawn) - Top([.. drawn, Part.게이지]);
    }
}
