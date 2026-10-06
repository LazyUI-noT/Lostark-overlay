using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>칸 옵션: everything about the box picked (a slot chip or the preview in 내 오버레이 → 스킬 칸, a click in the
/// 레이아웃 편집기): its colour and effects, and in the editor a buff bar's or with the plate clicked the window's.
/// Changes go to the character's class, to 기본 while it follows 기본 (then the rows are greyed until 개별 설정).</summary>
public partial class SettingsWindow
{
    /// <summary>The options of the box last picked with <see cref="ShowBox"/>; hosted by 스킬 칸, or by the editor.</summary>
    internal StackPanel BoxTab { get; } = new() { Margin = new(0, 0, 0, 12) };

    readonly WrapPanel _chips = new() { Margin = new(0, 0, 0, 8) };   // 스킬 칸: one per slot, 초각스 and 각성기 included
    Part? _box;
    int? _debuffBox;   // a debuff box picked in the editor (0 내 색, 1 교체, 2 번개) instead of a part
    bool _plate, _onlyThis = true;

    /// <summary>Shows the options of <paramref name="part"/> (null: nothing picked), or of the window with <paramref name="plate"/>.</summary>
    internal void ShowBox(Part? part, bool plate)
    {
        (_box, _plate, _debuffBox) = (part, plate, null);
        if (_inEditor) GaugeBox.Visibility = Shown(part == Part.게이지 && !plate);
        FillChips();
        FillBox();
    }

    /// <summary>레이아웃 편집기: shows the 너비 and 높이 of debuff box <paramref name="i"/>.</summary>
    internal void ShowDebuffBox(int i)
    {
        (_box, _plate, _debuffBox) = (null, false, i);
        GaugeBox.Visibility = Visibility.Collapsed;
        FillBox();
    }

    /// <summary>The slot chips, labelled with the keys; 아덴 slots only with a class (기본 has none).</summary>
    void FillChips()
    {
        _chips.Children.Clear();
        bool hasClass = ClassProfiles.ActiveClass(_s) is not null;
        foreach (var id in SlotInfo.All)
        {
            if (id is SlotId.아덴1 or SlotId.아덴2 && !hasClass) continue;
            string key = KeyNames.Display(_s.Keys.GetValueOrDefault(id, SlotInfo.DefaultKey(id)));
            var chip = Toggle(id is >= SlotId.일반1 and <= SlotId.일반8 ? key : $"{id} {key}", "slot", _box == (Part)id);
            chip.ToolTip = SlotLabel(id);
            chip.MinWidth = 40;
            chip.Checked += (_, _) => { if (_box != (Part)id) Dispatcher.BeginInvoke(() => ShowBox((Part)id, plate: false)); };
            _chips.Children.Add(chip);
        }
    }

    void FillBox()
    {
        var p = BoxTab;
        p.Children.Clear();
        if (_debuffBox is { } d)   // one debuff window for every class: no class heading, never greyed
        {
            p.Children.Add(Heading($"디버프 {DebuffNames[d]}", first: true));
            p.Children.Add(Note("100% = 창 크기에 맞춘 칸입니다. 칸 옮기기를 켜면 칸의 오른쪽 아래 모서리를 끌어서도 바꿀 수 있습니다."));
            foreach (var row in DebuffSizeSliders(d)) p.Children.Add(row);
            return;
        }
        var cls = ClassProfiles.ActiveClass(_s);
        if (_inEditor)
        {
            p.Children.Add(Heading(cls ?? "기본", first: true));
            if (cls is not null)
                p.Children.Add(Check("개별 설정 (끄면 기본을 따름)", ClassProfiles.IsOwn(_s, cls),
                    v => { ClassProfiles.SetOwn(_s, cls, v); Dispatcher.BeginInvoke(FillBox); }, Changed));
            if (cls is null) p.Children.Add(Note("기본: 클래스 정보가 없을 때의 표시입니다. 버프·초각스·각성기·일반스킬만 보이고, 아덴 게이지와 아덴스킬은 보이지 않습니다. 개별 설정을 켜지 않은 클래스도 이 칸 배치와 색을 따릅니다."));
        }
        else if (ShownClass != ActiveOrTemplate)
        {
            // The options are the character's class; editing another class here would change the wrong one.
            p.Children.Add(Note($"칸 옵션은 지금 캐릭터의 클래스({ClassName(ActiveOrTemplate)})에만 있습니다."));
            var back = new Button { Content = "지금 캐릭터로", HorizontalAlignment = HorizontalAlignment.Left };
            back.Click += (_, _) => { _classShown = null; Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); }); };
            p.Children.Add(back);
            return;
        }
        bool own = cls is null || ClassProfiles.IsOwn(_s, cls);
        if (!own && _inEditor) p.Children.Add(Note("기본을 따르는 중입니다. 아래 값은 기본의 값이며, 바꾸려면 개별 설정을 켜세요."));
        var rows = new StackPanel { IsEnabled = own };
        p.Children.Add(rows);

        if (_plate) PlateRows(rows);
        else if (_box is not { } part) rows.Children.Add(Note(_inEditor
            ? "칸을 누르면 그 칸의 색과 효과가 여기에 나옵니다. 칸 사이의 판을 누르면 창 전체의 옵션이 나옵니다."
            : "위의 키나 미리보기의 칸을 누르면 그 칸의 색과 효과가 나옵니다."));
        else if (BoxOptions.Of(part, _chars.AdenSlots) is var (slot, g)) SlotRows(rows, cls, slot, g);
        else if (part >= FreeLayout.FirstBuff) BuffBarRows(rows, (BuffKind)(part - FreeLayout.FirstBuff));
        else if (part == Part.변화)
        {
            rows.Children.Add(Heading("각성기 변화"));
            rows.Children.Add(ColorRow(Label("색"), _s.Overlay.AwakeningChangedColor, v => _s.Overlay.AwakeningChangedColor = v, swatch: false, warnDark: true));
        }
        else rows.Children.Add(Note("게이지의 두께, 구간, 색, 빛남은 왼쪽에 나옵니다."));
    }

    void SlotRows(Panel p, string? cls, SlotId slot, SlotGroup g)
    {
        MineProfile Fx() => ClassProfiles.EditShown(_s, cls).Mine;
        var fx = Fx();
        var aden = _chars.AdenSlots;
        bool perBox = BoxOptions.PerBox(g);
        bool only = perBox && _onlyThis;
        p.Children.Add(Heading(perBox ? $"{slot} ({_s.Keys.GetValueOrDefault(slot) ?? "?"})" : g.ToString(), first: !_inEditor));
        if (perBox)
        {
            var one = new RadioButton { Content = "이 칸만", GroupName = "boxScope", IsChecked = _onlyThis };
            var all = new RadioButton { Content = "일반스킬 전체", GroupName = "boxScope", IsChecked = !_onlyThis };
            one.Checked += (_, _) => { _onlyThis = true; Dispatcher.BeginInvoke(FillBox); };
            all.Checked += (_, _) => { _onlyThis = false; Dispatcher.BeginInvoke(FillBox); };
            p.Children.Add(Row("적용 범위", one, all));
        }
        p.Children.Add(ColorRow(Label("색"), ClassProfiles.Mine(_s).ColorOf(slot, g), v => BoxOptions.SetColor(Fx(), slot, g, v, only), swatch: true, warnGold: true));
        p.Children.Add(Check("준비되면 한 번 반짝임", fx.Shines(slot, g), v => BoxOptions.SetShine(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(Check("준비된 동안 계속 빛남", fx.Pulses(slot, g), v => BoxOptions.SetPulse(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(Row($"   빛나는 방식 ({g} 전체)", EnumCombo([(ReadyGlowStyle.Diagonal, "사선 띠"), (ReadyGlowStyle.Horizontal, "가로 띠"),
            (ReadyGlowStyle.Breathe, "숨쉬기"), (ReadyGlowStyle.Outline, "테두리")], fx.PulseStyleOf(g), v => Fx().PulseStyle[g] = v)));
        p.Children.Add(Check("준비 후 경과 시간 표시 (1~29초)", fx.Counts(slot, g), v => BoxOptions.SetReadyCount(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(Check("   사용 후 3초 동안 유지", fx.Holds(slot, g), v => BoxOptions.SetHeldCount(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(Check("끝나기 직전 깜빡임 (0.9초 전부터)", fx.EndFlashes(slot, g), v => BoxOptions.SetEndFlash(Fx(), slot, g, v, only, aden), Changed));
        p.Children.Add(Check("쿨타임 중 숨기기 (준비되면 다시 표시)", fx.Hides(slot, g), v => BoxOptions.SetHideOnCooldown(Fx(), slot, g, v, only, aden), Changed));
        if (g != SlotGroup.아덴스킬)
        {
            p.Children.Add(Check($"아이콘으로 표시 ({g} 전체)", fx.ShowsIcon(g), v => { if (v) Fx().Icons.Add(g); else Fx().Icons.Remove(g); }, Changed));
            var reread = new Button { Content = "아이콘 다시 읽기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(24, 4, 0, 4) };
            reread.Click += (_, _) => IconRereadRequested?.Invoke();
            p.Children.Add(reread);
            p.Children.Add(Note("아이콘은 직업각인마다 저장되고, 저장된 아이콘이 없는 칸이 있을 때만 화면에서 다시 읽습니다. 스킬을 바꿨으면 눌러서 모든 칸을 다시 읽으세요."));
        }
        p.Children.Add(Check("키 표시", fx.KeyLetters.Contains(slot), v => BoxOptions.SetKeyLetter(Fx(), slot, v), Changed));
    }

    void BuffBarRows(Panel p, BuffKind k)
    {
        var b = _s.Buffs;
        var i = b.Of(k);
        p.Children.Add(Heading($"버프 {k}"));
        p.Children.Add(Check("표시", i.Visible, v => b.Edit(k).Visible = v, Changed));
        p.Children.Add(Check("시간 표시", i.ShowLabel, v => b.Edit(k).ShowLabel = v, Changed));
        p.Children.Add(Check("마지막 1초 깜빡임", b.EndFlash(k), v => b.Edit(k).EndFlash = v, Changed));
        p.Children.Add(ColorRow(Label("색"), i.Color, v => b.Edit(k).Color = v, swatch: false));
        foreach (var row in BuffSizeSliders(k)) p.Children.Add(row);
    }

    void PlateRows(Panel p)
    {
        var o = _s.Overlay;
        p.Children.Add(Heading("창 전체"));
        p.Children.Add(SliderRow("칸 불투명도", 0.2, 1, o.BoxOpacity, 0.05, v => $"{v * 100:0}%", v => o.BoxOpacity = v, Changed));
        p.Children.Add(Check("남은 시간 숫자 표시", o.ShowTimer, v => o.ShowTimer = v, Changed));
        FormRow? fromRow = null;
        p.Children.Add(Row("쿨타임 표시", EnumCombo([(CooldownStyle.Sweep, "회전"), (CooldownStyle.Fill, "채움 (어두운 색 → 밝은 색)"),
            (CooldownStyle.FillHatched, "채움 (원래 색 + 빗금)")], o.CooldownStyle,
            v => { o.CooldownStyle = v; fromRow!.Visibility = Shown(v != CooldownStyle.Sweep); })));
        p.Children.Add(fromRow = Row("채움 방향", EnumCombo([(FillFrom.Bottom, "아래 → 위"), (FillFrom.Top, "위 → 아래"), (FillFrom.Left, "왼쪽 → 오른쪽"), (FillFrom.Right, "오른쪽 → 왼쪽")],
            o.FillFrom, v => o.FillFrom = v)));
        fromRow.Visibility = Shown(o.CooldownStyle != CooldownStyle.Sweep);
    }
}
