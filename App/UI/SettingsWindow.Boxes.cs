using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>칸 옵션: everything about the box clicked on the canvas: its colour and effects, a buff bar's, or with the plate
/// clicked the window's. Changes go to the 직업각인 picked in 편집 중 (<see cref="BoxKey"/>), to 기본 for 기본.</summary>
public partial class SettingsWindow
{
    /// <summary>The options of the box last picked with <see cref="ShowBox"/>; hosted by the inspector.</summary>
    internal CardStack BoxTab { get; } = new() { Margin = new(0, 0, 0, 12) };

    Part? _box;
    int? _debuffBox;   // a debuff box picked on the canvas (0 내 색, 1 교체, 2 번개) instead of a part
    bool _plate, _onlyThis = true;

    /// <summary>Shows the options of <paramref name="part"/> (null: nothing picked), or of the window with <paramref name="plate"/>.</summary>
    internal void ShowBox(Part? part, bool plate)
    {
        (_box, _plate, _debuffBox) = (part, plate, null);
        FillBox();
    }

    /// <summary>Shows the 너비 and 높이 of debuff box <paramref name="i"/>.</summary>
    internal void ShowDebuffBox(int i)
    {
        (_box, _plate, _debuffBox) = (null, false, i);
        FillBox();
    }

    void FillBox()
    {
        var p = BoxTab;
        p.Children.Clear();
        if (_debuffBox is { } d)   // one debuff window for every class: no class heading, never greyed
        {
            p.Children.Add(Heading($"디버프 {DebuffNames[d]}", first: true, help: "100% = 창 크기에 맞춘 칸입니다. 화면에서 칸의 오른쪽 아래 모서리를 끌어도 바꿀 수 있습니다."));
            foreach (var row in DebuffSizeSliders(d)) p.Children.Add(row);
            return;
        }
        var cls = BoxKey;
        // Every shipped 직업각인 has its own entry; a character whose 직업각인 isn't shipped shares 기본's, which the change then edits.
        if (cls is not null && !ClassProfiles.IsOwn(_s, cls)) p.Children.Add(Note("이 캐릭터는 기본을 따릅니다. 바꾸면 기본이 바뀝니다."));
        var rows = new CardStack();
        p.Children.Add(rows);

        if (_plate) PlateRows(rows);
        else if (_box is not { } part) rows.Children.Add(Note("화면에서 칸을 누르면 그 칸의 색과 효과가 나옵니다. 칸 사이의 판을 누르면 창 전체의 옵션이 나옵니다."));
        else if (BoxOptions.Of(part, BoxAdenSlots) is var (slot, g)) SlotRows(rows, cls, slot, g);
        else if (part >= FreeLayout.FirstBuff) BuffBarRows(rows, (BuffKind)(part - FreeLayout.FirstBuff));
        else if (part == Part.변화)
        {
            rows.Children.Add(Heading("초각스"));
            rows.Children.Add(ColorRow(Label("색"), _s.Overlay.AwakeningChangedColor, v => _s.Overlay.AwakeningChangedColor = v, swatch: false, warnDark: true));
        }
    }

    /// <summary>One box's or group's options: heading, 적용 범위, then <see cref="GlowRows"/>, <see cref="CountRows"/>, <see cref="IconRows"/>.</summary>
    void SlotRows(Panel p, string? cls, SlotId slot, SlotGroup g)
    {
        bool perBox = BoxOptions.PerBox(g);
        bool only = perBox && _onlyThis;
        p.Children.Add(Heading(perBox ? $"{slot} ({_s.Keys.GetValueOrDefault(slot) ?? "?"})" : g.ToString(), first: true));
        if (perBox)
        {
            var one = new RadioButton { Content = "이 칸만", GroupName = "boxScope", IsChecked = _onlyThis };
            var all = new RadioButton { Content = "일반스킬 전체", GroupName = "boxScope", IsChecked = !_onlyThis };
            one.Checked += (_, _) => { _onlyThis = true; Refill(); };
            all.Checked += (_, _) => { _onlyThis = false; Refill(); };
            p.Children.Add(Row("적용 대상", one, all));
        }
        var (colour, iconNote) = GlowRows(p, cls, slot, g, only);
        CountRows(p, cls, slot, g, only);
        IconRows(p, cls, slot, g, colour, iconNote);
    }

    /// <summary>The box list again, after the current click is done (a rebuild inside its own handler would drop it).</summary>
    void Refill() => Dispatcher.BeginInvoke(FillBox);

    /// <summary>색, 반짝임, 계속 빛남 with its style and colour. Returns the colour row and its icon note, which
    /// <see cref="IconRows"/> swaps while the group shows icons.</summary>
    (UIElement Colour, UIElement IconNote) GlowRows(Panel p, string? cls, SlotId slot, SlotGroup g, bool only)
    {
        MineProfile Fx() => ClassProfiles.EditShown(_s, cls).Mine;
        var fx = Fx();
        var aden = BoxAdenSlots;
        // Rows that do nothing in the current state collapse, and come back the moment their parent is ticked.
        var glowLabel = Label("");
        void LabelGlow() => glowLabel.Text = BoxOptions.OwnGlow(Fx(), g, only) ? "발광 색 (이 칸만)" : $"발광 색 ({g} 전체)";
        LabelGlow();
        // 이 칸만 gives the skill its own colour, and with it its own glow colour: the label follows.
        var colour = ColorRow(Label("색"), ClassProfiles.Mine(_s, cls).ColorOf(slot, g), v => { BoxOptions.SetColor(Fx(), slot, g, v, only); LabelGlow(); }, swatch: true, warnGold: true);
        if (colour is FrameworkElement tip) tip.ToolTip = "그룹마다의 색, 기본/개인 설정, 쿨타임 색은 스킬 칸의 '나' → 색에서 정합니다.";
        var iconNote = Note(IconColourNote);
        p.Children.Add(colour);
        p.Children.Add(iconNote);
        p.Children.Add(Check("쿨 돌면 1회 반짝", fx.Shines(slot, g), v => BoxOptions.SetShine(Fx(), slot, g, v, only, aden), Changed));
        // 계속 빛남, then how and in what colour it glows: shown while any box of the group glows (both are the group's
        // unless this skill has its own colour).
        var glow = new StackPanel();
        p.Children.Add(Check("쿨 돈 동안 발광", fx.Pulses(slot, g), v => { BoxOptions.SetPulse(Fx(), slot, g, v, only, aden); glow.Visibility = Shown(AnyPulses(Fx(), g, aden)); }, Changed));
        glow.Children.Add(Row($"발광 방식 ({g} 전체)", EnumCombo([(ReadyGlowStyle.Diagonal, "사선 띠"), (ReadyGlowStyle.Horizontal, "가로 띠"),
            (ReadyGlowStyle.Breathe, "숨쉬기"), (ReadyGlowStyle.Outline, "테두리")], fx.PulseStyleOf(g), v => Fx().PulseStyle[g] = v)));
        glow.Children.Add(ColorRow(glowLabel, ClassProfiles.Mine(_s, cls).GlowOf(slot, g),
            v => { BoxOptions.SetGlowColor(Fx(), slot, g, v, only); Dispatcher.BeginInvoke(FillClassSections); }, swatch: false));   // '나' shows the way back to white
        glow.Visibility = Shown(AnyPulses(fx, g, aden));
        p.Children.Add(glow);
        return (colour, iconNote);
    }

    /// <summary>쿨 돈 뒤 지난 시간 with its hold and colour, 직전 깜빡임, 쿨타임 중 숨기기.</summary>
    void CountRows(Panel p, string? cls, SlotId slot, SlotGroup g, bool only)
    {
        MineProfile Fx() => ClassProfiles.EditShown(_s, cls).Mine;
        var fx = Fx();
        var aden = BoxAdenSlots;
        // 쿨 돈 뒤 지난 시간, then what goes with it: held after use while this box counts (the renderer holds only a count
        // it shows), and the one colour of every count while any box counts.
        CheckBox? hold = null;
        UIElement? countColour = null;
        p.Children.Add(Check("쿨 돈 뒤 경과 초 (1–29)", fx.Counts(slot, g), v =>
        {
            BoxOptions.SetReadyCount(Fx(), slot, g, v, only, aden);
            hold!.Visibility = Shown(v);
            countColour!.Visibility = Shown(AnyCounts(Fx(), aden));
        }, Changed));
        p.Children.Add(hold = Check("사용 후 3초 유지", fx.Holds(slot, g), v => BoxOptions.SetHeldCount(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(countColour = ColorRow(Label("경과 초 색 (전체 칸)"), _s.Overlay.ReadyCountColor, v => _s.Overlay.ReadyCountColor = v, swatch: false));
        hold.Visibility = Shown(fx.Counts(slot, g));
        countColour.Visibility = Shown(AnyCounts(fx, aden));
        p.Children.Add(Check("쿨 돌기 0.9초 전 깜빡임", fx.EndFlashes(slot, g), v => BoxOptions.SetEndFlash(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(Check("쿨 중 숨김", fx.Hides(slot, g), v => BoxOptions.SetHideOnCooldown(Fx(), slot, g, v, only, aden), Changed));
    }

    /// <summary>아이콘으로 표시 (in place of <paramref name="colour"/>), 아이콘 다시 읽기, 키 표시.</summary>
    void IconRows(Panel p, string? cls, SlotId slot, SlotGroup g, UIElement colour, UIElement iconNote)
    {
        MineProfile Fx() => ClassProfiles.EditShown(_s, cls).Mine;
        var fx = Fx();
        bool icons = false, ownIcons = ShownClass == ActiveOrTemplate;   // 아이콘 다시 읽기 reads the character's
        var reread = new Button { Content = "아이콘 다시 읽기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(24, 4, 0, 4) };
        var rereadNote = HelpMark("아이콘은 직업각인마다 저장되고, 저장된 아이콘이 없는 칸이 있을 때만 화면에서 다시 읽습니다. 스킬을 바꿨으면 눌러서 모든 칸을 다시 읽으세요.");
        var playNote = Note("아이콘은 그 직업각인으로 플레이하며 읽습니다. 아이콘 다시 읽기는 지금 캐릭터를 편집할 때만 쓸 수 있습니다.");
        void ShowIconRows()
        {
            colour.Visibility = Shown(!icons);
            iconNote.Visibility = reread.Visibility = rereadNote.Visibility = Shown(icons);
            playNote.Visibility = Shown(icons && !ownIcons);
        }
        if (g != SlotGroup.아덴스킬)   // 아덴스킬 has no icon: always its colour
        {
            icons = fx.ShowsIcon(g);
            p.Children.Add(Check($"아이콘으로 표시 ({g} 전체)", icons, v => { if (v) Fx().Icons.Add(g); else Fx().Icons.Remove(g); icons = v; ShowIconRows(); }, () => { Changed(); FillContext(); }));   // FillContext: the note on missing icons
            reread.Click += (_, _) => IconRereadRequested?.Invoke();
            if (ownIcons) p.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { reread, rereadNote } });
            else p.Children.Add(playNote);   // not the character's: nothing here may clear or re-read its icons
        }
        ShowIconRows();
        p.Children.Add(Check("키", fx.KeyLetters.Contains(slot), v => BoxOptions.SetKeyLetter(Fx(), slot, v), Changed));
    }

    /// <summary>Under 아이콘으로 표시 in place of 색: where the colour still shows.</summary>
    internal const string IconColourNote = "색: 아이콘으로 표시 중이라 아이콘을 읽기 전과 파티원 화면에만 쓰입니다.";

    /// <summary>Whether any box of <paramref name="g"/> glows while ready (빛나는 방식 is the group's).</summary>
    static bool AnyPulses(MineProfile fx, SlotGroup g, IReadOnlyCollection<SlotId> aden) =>
        SlotInfo.All.Any(id => SlotInfo.GroupOf(id, aden) == g && fx.Pulses(id, g));

    /// <summary>Whether any box shows 쿨 돈 뒤 지난 시간 (its 숫자 색 is one for all).</summary>
    static bool AnyCounts(MineProfile fx, IReadOnlyCollection<SlotId> aden) =>
        SlotInfo.All.Any(id => fx.Counts(id, SlotInfo.GroupOf(id, aden)));

    void BuffBarRows(Panel p, BuffKind k)
    {
        var b = _s.Buffs;
        var i = b.Of(k);
        p.Children.Add(Heading($"버프 {k}"));
        // As in 설정 → 버프: the rest only while the bar is shown.
        var more = Stack([Check("남은 초", i.ShowLabel, v => b.Edit(k).ShowLabel = v, Changed),
            Check("끊기기 1초 전 깜빡임", b.EndFlash(k), v => b.Edit(k).EndFlash = v, Changed),
            ColorRow(Label("색"), i.Color, v => b.Edit(k).Color = v, swatch: false), .. BuffSizeSliders(k)]);
        more.Visibility = Shown(i.Visible);
        p.Children.Add(Check("표시", i.Visible, v => { b.Edit(k).Visible = v; more.Visibility = Shown(v); }, Changed));
        p.Children.Add(more);
    }

    void PlateRows(Panel p)
    {
        p.Children.Add(Heading("창 전체"));
        CooldownLookRows(p);
    }
}
