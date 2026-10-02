using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaugeOverlay.Core.Domain;

/// <summary>기본: the built-in colours (for party members, the role colour on 일반스킬 first). 개인 설정: the
/// colours set in the profile. 파티원 설정 받기 (members only): the colours the member sent. A group without a colour
/// falls back to 기본.</summary>
public enum ColorSource { Default, Personal, Received }

/// <summary>What the engine reads (<see cref="ClassProfiles.Reads"/>). <paramref name="BuffSize"/>: the game's 버프 크기,
/// or null to leave the buff row unread. <paramref name="Gauge"/>: the 아덴 게이지 is shown or sent.</summary>
public sealed record ReadPlan(IReadOnlySet<SlotId> Skip, bool Label, int? BuffSize, bool Gauge);

/// <summary>What 복사 → 대상 copies.</summary>
[Flags]
public enum CopyParts { Color = 1, Size = 2, Layout = 4, Display = 8, Effects = 16, All = Color | Size | Layout | Display | Effects }

/// <summary>One class's boxes for you (<see cref="ClassSettings.Mine"/>) or for a party member
/// (<see cref="MemberProfile"/>): what is shown, where and in what colours.</summary>
public class ClassProfile
{
    public Dictionary<SlotGroup, bool> Groups { get; set; } = Enum.GetValues<SlotGroup>().ToDictionary(g => g, _ => true);
    /// <summary>Slots hidden one by one (e.g. empty slots).</summary>
    public HashSet<SlotId> HiddenSlots { get; set; } = [];
    public bool Gauge { get; set; } = true;
    /// <summary>편집 모드의 칸 배치: each part's spot and size (DIP at size 1.0), or null for the fixed grid, which closes
    /// up when a group is off.</summary>
    public Dictionary<Part, Cell>? Layout { get; set; }
    public ColorSource Color { get; set; } = ColorSource.Default;
    /// <summary>#RRGGBB per group, used while <see cref="Color"/> is <see cref="ColorSource.Personal"/>.</summary>
    public Dictionary<SlotGroup, string> Colors { get; set; } = [];
    /// <summary>#RRGGBB for one skill (the eight 일반스킬), over its group's in <see cref="ColorSource.Personal"/>. Yours
    /// only: never sent to the party.</summary>
    public Dictionary<SlotId, string> SlotColors { get; set; } = [];
    /// <summary>일반스킬 모두 같은 색, as set; unset, read through <see cref="SkillsOneColor"/>.</summary>
    public bool? SkillsUnified { get; set; }
    /// <summary>Every 일반스킬 in the group colour, <see cref="SlotColors"/> set aside: as set, else on unless some skill
    /// already has its own colour (settings from before the switch).</summary>
    [JsonIgnore] public bool SkillsOneColor => SkillsUnified ?? SlotColors.Count == 0;
    /// <summary>쿨타임 색 자동: a cooling box is its colour dimmed; off, <see cref="DimColors"/> and
    /// <see cref="SlotDimColors"/> where set (in <see cref="ColorSource.Personal"/>).</summary>
    public bool AutoDim { get; set; } = true;
    public Dictionary<SlotGroup, string> DimColors { get; set; } = [];
    public Dictionary<SlotId, string> SlotDimColors { get; set; } = [];
}

/// <summary>A party member's boxes while they play the class: their size too, and 내 설정 적용, which uses your
/// profile of the class for a part while leaving this one's as it is.</summary>
public sealed class MemberProfile : ClassProfile
{
    public double Width { get; set; } = 1.0;
    public double Height { get; set; } = 1.0;
    public bool UseMineColor { get; set; }
    public bool UseMineDisplay { get; set; }
    public bool UseMineSize { get; set; }
}

/// <summary>Your boxes' effects while you play the class (party members' boxes have none): 준비되면 반짝임, 준비된 동안
/// 계속 빛남 (with its style and colour), 끝나기 직전 깜빡임, 아이콘으로 표시 and 키 표시. An 일반스킬 follows its group's
/// switch until set on its own.</summary>
public sealed class MineProfile : ClassProfile
{
    /// <summary>Groups whose boxes don't flash on becoming ready.</summary>
    public HashSet<SlotGroup> NoShine { get; set; } = [];
    public bool Shines(SlotGroup g) => !NoShine.Contains(g);
    /// <summary>준비된 동안 계속 빛남: groups whose ready boxes glow for as long as they stay ready.</summary>
    public HashSet<SlotGroup> Pulse { get; set; } = [];
    public bool Pulses(SlotGroup g) => Pulse.Contains(g);
    /// <summary>How each group's ready box shows 계속 빛남, and in what colour (#RRGGBB); a group not in them: 일반스킬
    /// breathe, the others sweep a diagonal band, all white.</summary>
    public Dictionary<SlotGroup, ReadyGlowStyle> PulseStyle { get; set; } = [];
    public Dictionary<SlotGroup, string> PulseColor { get; set; } = [];
    public ReadyGlowStyle PulseStyleOf(SlotGroup g) =>
        PulseStyle.TryGetValue(g, out var s) ? s : g == SlotGroup.일반스킬 ? ReadyGlowStyle.Breathe : ReadyGlowStyle.Diagonal;
    public string PulseColorOf(SlotGroup g) => PulseColor.GetValueOrDefault(g) ?? "#FFFFFF";
    /// <summary>빛 색 직접 고르기: off, every glow is white and <see cref="PulseColor"/> and <see cref="SlotPulseColors"/> wait.</summary>
    public bool CustomGlow { get; set; }
    /// <summary>An 일반스킬's own glow colour, used while the 일반스킬 have their own colours (see Appearance.GlowOf).</summary>
    public Dictionary<SlotId, string> SlotPulseColors { get; set; } = [];
    /// <summary>준비 후 경과 시간: groups whose ready boxes count the seconds since ready (off by default).</summary>
    public HashSet<SlotGroup> ReadyCount { get; set; } = [];
    public Dictionary<SlotId, bool> SlotReadyCount { get; set; } = [];
    public bool Counts(SlotId slot, SlotGroup g) => SlotReadyCount.TryGetValue(slot, out var on) ? on : ReadyCount.Contains(g);
    /// <summary>사용 후 3초 유지: groups whose count stays for 3 s after the skill is used (off by default; needs the count).</summary>
    public HashSet<SlotGroup> HeldCount { get; set; } = [];
    public Dictionary<SlotId, bool> SlotHeldCount { get; set; } = [];
    public bool Holds(SlotId slot, SlotGroup g) => SlotHeldCount.TryGetValue(slot, out var on) ? on : HeldCount.Contains(g);
    /// <summary>아이콘으로 표시: groups whose boxes show the skill's own icon, copied from the skill bar.</summary>
    public HashSet<SlotGroup> Icons { get; set; } = [];
    public bool ShowsIcon(SlotGroup g) => Icons.Contains(g);
    public Dictionary<SlotId, bool> SlotShine { get; set; } = [];
    public Dictionary<SlotId, bool> SlotPulse { get; set; } = [];
    public bool Shines(SlotId slot, SlotGroup g) => SlotShine.TryGetValue(slot, out var on) ? on : Shines(g);
    public bool Pulses(SlotId slot, SlotGroup g) => SlotPulse.TryGetValue(slot, out var on) ? on : Pulses(g);
    /// <summary>끝나기 직전 깜빡임: a band sweeps the box in the last 0.8 s of its cooldown (off by default).</summary>
    public HashSet<SlotGroup> EndFlash { get; set; } = [];
    public Dictionary<SlotId, bool> SlotEndFlash { get; set; } = [];
    public bool EndFlashes(SlotGroup g) => EndFlash.Contains(g);
    public bool EndFlashes(SlotId slot, SlotGroup g) => SlotEndFlash.TryGetValue(slot, out var on) ? on : EndFlashes(g);
    /// <summary>키 표시: boxes that show their key (from the 키 tab) in the top-left corner.</summary>
    public HashSet<SlotId> KeyLetters { get; set; } = [];

    internal void CopyEffects(MineProfile from)
    {
        (NoShine, Pulse, PulseStyle, PulseColor, Icons) = ([.. from.NoShine], [.. from.Pulse], new(from.PulseStyle), new(from.PulseColor), [.. from.Icons]);
        (SlotShine, SlotPulse, EndFlash, SlotEndFlash, KeyLetters) = (new(from.SlotShine), new(from.SlotPulse), [.. from.EndFlash], new(from.SlotEndFlash), [.. from.KeyLetters]);
        (CustomGlow, SlotPulseColors, ReadyCount, SlotReadyCount) = (from.CustomGlow, new(from.SlotPulseColors), [.. from.ReadyCount], new(from.SlotReadyCount));
        (HeldCount, SlotHeldCount) = ([.. from.HeldCount], new(from.SlotHeldCount));
    }
}

/// <summary>What a member sends of their own boxes, once per session and on 업데이트: their colours, box size and free
/// layout (null = the fixed grid).</summary>
public sealed record Look(IReadOnlyDictionary<SlotGroup, string> Colors, double Width = 1, double Height = 1,
    IReadOnlyDictionary<Part, Cell>? Layout = null);

/// <summary>How one player's boxes are drawn, decided once per layout: what is shown, where, in what colours and at
/// what box size.</summary>
public sealed record Appearance(IReadOnlyDictionary<SlotGroup, bool> Groups, IReadOnlySet<SlotId> Hidden, bool Gauge,
    IReadOnlyDictionary<Part, Cell>? Layout, IReadOnlyDictionary<SlotGroup, string> Colors, double Width, double Height,
    IReadOnlyDictionary<SlotId, string>? SlotColors = null, IReadOnlyDictionary<SlotGroup, string>? DimColors = null,
    IReadOnlyDictionary<SlotId, string>? SlotDimColors = null, MineProfile? Effects = null)
{
    /// <summary>A slot's colour: its own (<see cref="SlotColors"/>), else its group's.</summary>
    public string ColorOf(SlotId slot, SlotGroup g) => SlotColors?.GetValueOrDefault(slot) ?? Colors[g];

    /// <summary>A slot's cooldown colour, or null to dim <see cref="ColorOf"/>: a slot with its own colour takes its own
    /// cooldown colour, any other its group's.</summary>
    public string? DimOf(SlotId slot, SlotGroup g) =>
        SlotColors?.ContainsKey(slot) == true ? SlotDimColors?.GetValueOrDefault(slot) : DimColors?.GetValueOrDefault(g);

    /// <summary>A slot's ready glow: white unless 빛 색 직접 고르기 is on, then its own while the 일반스킬 have their own
    /// colours (<see cref="SlotColors"/>), else its group's.</summary>
    public string GlowOf(SlotId slot, SlotGroup g) =>
        Effects is not { CustomGlow: true } fx ? "#FFFFFF"
        : (SlotColors is not null ? fx.SlotPulseColors.GetValueOrDefault(slot) : null) ?? fx.PulseColorOf(g);
}

/// <summary>The one place a player's boxes are decided, per class and in this order: what to show, where (the class's
/// layout or the fixed grid), what colour, and for a party member what size (a received size or layout only while
/// receiving it is on, fitted inside the size you set). Called when a layout is built, not per frame.</summary>
public static class ClassProfiles
{
    /// <summary>The key of the settings a class without its own starts from (and that an unknown class uses).</summary>
    public const string Template = "";
    /// <summary>The Width/Height settings' range, and how far a received size or layout may be shrunk to fit.</summary>
    public const double MinScale = 0.3, MaxScale = 2.0, MinFitScale = 0.1;

    /// <summary>Hues from overlay link.png, red and blue lifted to L* ≈ 65 so every group's ready box is about as bright
    /// (brightness is the state signal).</summary>
    public static readonly IReadOnlyDictionary<SlotGroup, string> BuiltIn = new Dictionary<SlotGroup, string>
    {
        [SlotGroup.각성기] = "#A4D82B",
        [SlotGroup.초각스] = "#C3C3C3",
        [SlotGroup.일반스킬] = "#FF6B70",
        [SlotGroup.아덴스킬] = "#8098FF",
    };

    /// <summary>The active character's class, or null.</summary>
    public static string? ActiveClass(Settings s) =>
        s.ActiveCharacter is { } k && s.Characters.TryGetValue(k, out var c) && c.Class.Length > 0 ? c.Class : null;

    /// <summary>What <paramref name="className"/> draws with (not stored: for reading): its own boxes when 개별, else
    /// 기본's.</summary>
    public static ClassSettings Of(Settings s, string? className)
    {
        var t = s.Classes.GetValueOrDefault(Template);
        if (className is null or Template || !s.Classes.TryGetValue(className, out var c)) return t ?? new ClassSettings();
        return c.Own ? c : new ClassSettings { Mine = t?.Mine ?? new(), Members = t?.Members ?? new() };
    }

    /// <summary>개별 설정 is on for <paramref name="className"/> (never for 기본 itself).</summary>
    public static bool IsOwn(Settings s, string? className) =>
        className is not (null or Template) && s.Classes.TryGetValue(className, out var c) && c.Own;

    /// <summary>The class's stored entry, added bare (following 기본) the first time.</summary>
    public static ClassSettings Entry(Settings s, string? className)
    {
        className ??= Template;
        if (!s.Classes.TryGetValue(className, out var c)) s.Classes[className] = c = new ClassSettings();
        return c;
    }

    /// <summary>개별 설정 on or off. On for the first time copies 기본; off keeps the class's settings for the next time
    /// it is turned on.</summary>
    public static void SetOwn(Settings s, string? className, bool on)
    {
        if (className is null or Template) return;
        var c = Entry(s, className);
        if (on && !c.Own && Same(c.Mine, new MineProfile()) && Same(c.Members, new MemberProfile())
            && s.Classes.TryGetValue(Template, out var t))
            (c.Mine, c.Members) = (Clone(t.Mine), Clone(t.Members));
        c.Own = on;
    }

    /// <summary>The settings of <paramref name="className"/> to change, which makes it 개별 (copying 기본 the first time);
    /// null or <see cref="Template"/> = 기본 itself.</summary>
    public static ClassSettings Edit(Settings s, string? className)
    {
        SetOwn(s, className, true);
        return Entry(s, className);
    }

    /// <summary>What a change seen on screen (편집 모드) goes into: the class's own settings when 개별, else 기본's, which
    /// the class shows.</summary>
    public static ClassSettings EditShown(Settings s, string? className) =>
        IsOwn(s, className) ? s.Classes[className!] : Entry(s, Template);

    /// <summary>The boxes a drag on screen (레이아웃 편집기의 칸 옮기기) changes: yours, or a party member's of
    /// <paramref name="className"/> when <paramref name="member"/>, in <see cref="EditShown"/>'s settings.</summary>
    public static ClassProfile BoxesShown(Settings s, string? className, bool member) =>
        member ? EditShown(s, className).Members : EditShown(s, className).Mine;

    /// <summary>Settings from before 개별: a class is 개별 only where its boxes differ from 기본's.</summary>
    public static void MarkOwnWhereDifferent(Settings s)
    {
        var t = s.Classes.GetValueOrDefault(Template) ?? new ClassSettings();
        foreach (var (name, c) in s.Classes.Where(c => c.Key != Template))
            c.Own = !(Same(c.Mine, t.Mine) && Same(c.Members, t.Members));
    }

    static bool Same<T>(T a, T b) where T : ClassProfile =>
        JsonSerializer.SerializeToUtf8Bytes(a).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(b));

    /// <summary>Your boxes, for the active character's class.</summary>
    public static Appearance Mine(Settings s)
    {
        var cls = ActiveClass(s);
        var c = Of(s, cls);
        // No class known yet: 기본 without the 아덴 slots and the gauge, which only a class gives a meaning.
        IReadOnlySet<SlotId> hidden = cls is null ? new HashSet<SlotId>(c.Mine.HiddenSlots) { SlotId.아덴1, SlotId.아덴2 } : c.Mine.HiddenSlots;
        bool personal = c.Mine.Color == ColorSource.Personal;
        return new(c.Mine.Groups, hidden, c.Mine.Gauge && cls is not null, c.Mine.Layout, MineColors(c), s.Overlay.Width, s.Overlay.Height,
            personal && !c.Mine.SkillsOneColor ? c.Mine.SlotColors : null,
            personal && !c.Mine.AutoDim ? c.Mine.DimColors : null, personal && !c.Mine.AutoDim ? c.Mine.SlotDimColors : null, c.Mine);
    }

    /// <summary>What the engine reads: a slot is skipped when it is neither shown (its group on, the slot not hidden) nor
    /// sent to the party, the 각성기 변화 label likewise with its 각성기 group, the gauge likewise, and the buff row only
    /// while an indicator is on.</summary>
    public static ReadPlan Reads(Settings s, IReadOnlyCollection<SlotId> adenSlots)
    {
        var mine = Mine(s);
        var sent = s.Party.SendGroups();
        bool Kept(SlotGroup g) => mine.Groups.GetValueOrDefault(g, true) || sent.Contains(g);
        bool Skipped(SlotId id, SlotGroup g) => !Kept(g) || !sent.Contains(g) && mine.Hidden.Contains(id);
        bool classless = ActiveClass(s) is null;   // 기본 with no class: no 아덴 slots or gauge, not even for the party
        var skip = SlotInfo.All.Where(id => Skipped(id, SlotInfo.GroupOf(id, adenSlots)) || classless && id is SlotId.아덴1 or SlotId.아덴2);
        bool buffs = Enum.GetValues<BuffKind>().Any(k => s.Buffs.Of(k).Visible);
        return new(skip.ToHashSet(), Kept(SlotGroup.각성기), buffs ? s.Buffs.Size : null, !classless && (mine.Gauge || s.Party.SendGauge));
    }

    /// <summary>A party member's boxes: class <paramref name="className"/> (null = unknown) in role <paramref name="role"/>,
    /// having sent <paramref name="look"/> (null = nothing yet).</summary>
    public static Appearance Member(Settings s, string? className, Role role, Look? look)
    {
        var c = Of(s, className);
        var m = c.Members;
        ClassProfile display = m.UseMineDisplay ? c.Mine : m;
        var colors = m.UseMineColor ? MineColors(c) : ColorsBy(g => m.Color switch
        {
            ColorSource.Received => look?.Colors.GetValueOrDefault(g),
            ColorSource.Personal => m.Colors.GetValueOrDefault(g),
            _ => null,
        } ?? (g == SlotGroup.일반스킬 ? s.Party.RoleColor.GetValueOrDefault(role) : null) ?? BuiltIn[g]);
        var (w, h) = m.UseMineSize ? (s.Overlay.Width, s.Overlay.Height) : (m.Width, m.Height);
        var layout = (IReadOnlyDictionary<Part, Cell>?)m.Layout;
        bool size = s.Party.ReceiveSize && look is not null, cells = s.Party.ReceiveLayout && look?.Layout is not null;
        if (size || cells) (layout, w, h) = Fit(layout, w, h, look!, size, cells, s.Gauge.Thickness);
        return new(display.Groups, display.HiddenSlots, display.Gauge, layout, colors, w, h);
    }

    /// <summary>A received size and/or layout, scaled as one so it fills the area your layout takes at your size
    /// (<paramref name="w"/>, <paramref name="h"/>) and never goes past it: the renderer scales cells by the box size,
    /// so only the size changes.</summary>
    static (IReadOnlyDictionary<Part, Cell>?, double, double) Fit(IReadOnlyDictionary<Part, Cell>? mine, double w, double h,
        Look look, bool size, bool cells, double gaugeThickness)
    {
        (w, h) = (Math.Max(w, MinScale), Math.Max(h, MinScale));
        var local = mine ?? FreeLayout.Default(gaugeThickness);
        var theirs = cells ? look.Layout! : local;
        var (sw, sh) = size ? (look.Width, look.Height) : (w, h);
        var (lw, lh) = Extent(local);
        var (tw, th) = Extent(theirs);
        double k = Math.Min(lw * w / (tw * sw), lh * h / (th * sh));
        return (cells ? look.Layout : mine, Math.Clamp(sw * k, MinFitScale, MaxScale), Math.Clamp(sh * k, MinFitScale, MaxScale));
    }

    static (double W, double H) Extent(IReadOnlyDictionary<Part, Cell> cells) =>
        (Math.Max(1, cells.Values.Max(c => c.Right) - cells.Values.Min(c => c.X)),
         Math.Max(1, cells.Values.Max(c => c.Bottom) - cells.Values.Min(c => c.Y)));

    /// <summary>복사 → 대상: copies <paramref name="parts"/> of <paramref name="from"/> into each target once. Your size
    /// is your window's; only a member profile has a size to copy into. 파티원 설정 받기 becomes 기본 in your profile.</summary>
    public static void Copy(Settings s, ClassProfile from, IEnumerable<ClassProfile> targets, CopyParts parts)
    {
        foreach (var to in targets.Where(t => t != from))
        {
            if (parts.HasFlag(CopyParts.Color))
            {
                to.Color = from.Color == ColorSource.Received && to is not MemberProfile ? ColorSource.Default : from.Color;
                (to.Colors, to.SlotColors, to.SkillsUnified) = (new(from.Colors), new(from.SlotColors), from.SkillsUnified);
                (to.AutoDim, to.DimColors, to.SlotDimColors) = (from.AutoDim, new(from.DimColors), new(from.SlotDimColors));
            }
            if (parts.HasFlag(CopyParts.Layout)) to.Layout = from.Layout is null ? null : new(from.Layout);
            if (parts.HasFlag(CopyParts.Effects) && (from, to) is (MineProfile fx, MineProfile tx)) tx.CopyEffects(fx);
            if (parts.HasFlag(CopyParts.Display)) (to.Groups, to.HiddenSlots, to.Gauge) = (new(from.Groups), [.. from.HiddenSlots], from.Gauge);
            if (parts.HasFlag(CopyParts.Size) && to is MemberProfile m)
                (m.Width, m.Height) = from is MemberProfile f ? (f.Width, f.Height) : (s.Overlay.Width, s.Overlay.Height);
        }
    }

    static IReadOnlyDictionary<SlotGroup, string> MineColors(ClassSettings c) => ColorsBy(g =>
        (c.Mine.Color == ColorSource.Personal ? c.Mine.Colors.GetValueOrDefault(g) : null) ?? BuiltIn[g]);

    static Dictionary<SlotGroup, string> ColorsBy(Func<SlotGroup, string> colour) =>
        Enum.GetValues<SlotGroup>().ToDictionary(g => g, colour);

    static T Clone<T>(T profile) where T : ClassProfile => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(profile))!;
}
