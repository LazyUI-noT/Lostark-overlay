using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>아덴 게이지 and 막대: shown once the gauge is clicked, under the 오버레이 tab's preview or on the 레이아웃
/// 편집기's left. 막대 is per 클래스·직업각인: the class shown's only.</summary>
public partial class SettingsWindow
{
    /// <summary>The gauge's options; shown while the gauge is the part picked (<see cref="ShowBox"/>).</summary>
    internal CardStack GaugeBox { get; } = new() { Margin = new(16, 0, 16, 12), Visibility = Visibility.Collapsed };

    void FillGauge()
    {
        GaugeBox.Children.Clear();
        GaugeBox.Children.Add(GaugePanel());
    }

    UIElement GaugePanel()
    {
        var g = Target.Gauge;   // 편집 중's look; 읽기 방식 is global (_s)
        var p = new CardStack();
        p.Children.Add(SwitchBadge());   // 합쳐서 / 따로 is one per profile (프로필 N 공통): its chip first, then 편집 중's look
        p.Children.Add(ApartRow());   // outside the look group: 모든 프로필에 적용 doesn't copy it
        p.Children.Add(CardBadge(LookGroup.게이지));
        // 편집 중's 직업각인 (ShownSubclass), not the character's.
        // 기본 has no 직업각인 to read for: BarRows says so.
        if (ShownClass != ClassProfiles.Template && !(ShownSubclass is { } shown && shown.Class.Subclasses.GetValueOrDefault(shown.Sub) is not null))
            p.Children.Add(Note("이 직업각인은 아덴 게이지를 읽지 않습니다."));
        else if (ShownClass == ActiveOrTemplate && _chars.Gauge is null)
            p.Children.Add(Note("게이지 정의 없음: 이 직업각인의 게이지 파일(Profiles/aden)이 아직 없어서 읽지 않습니다."));
        foreach (var e in ApartOnly(Defaulted(HelpRow("세로", "막대를 세워 아래에서 위로 채웁니다. 구분선도 가로로 눕습니다.", Check("세로", g.Upright, v => g.Upright = v, Changed)),
            s => s.Gauge.Upright, (s, v) => s.Gauge.Upright = v, Target), "합쳐서에서는 가로로 그립니다. 따로로 바꾸면 세로로 돌아옵니다.")) p.Children.Add(e);
        if (_s.Apart) p.Children.Add(Note("길이와 두께: 화면에서 테두리를 끌거나 Ctrl+화살표로 바꿉니다."));
        p.Children.Add(ColorRow(Label("찬 색"), g.Fill, v => g.Fill = v, swatch: false));
        p.Children.Add(ColorRow(Label("빈 색"), g.Empty, v => g.Empty = v, swatch: false));
        p.Children.Add(SliderRow("두께", 2, 16, g.Thickness, 1, v => $"{v:0} px", v => g.Thickness = v, Changed, "두꺼워지면 위쪽으로 늘어나고, 아래 칸의 위치는 그대로입니다."));
        p.Children.Add(SliderRow("불투명도", 0.2, 1, g.Opacity, 0.05, v => $"{v * 100:0}%", v => g.Opacity = v, Changed));
        p.Children.Add(HelpRow("읽기 방식", "기본: 같은 값이 5번 연속 읽혀야 바뀝니다. 작은 상승: 조금 오른 값은 2번만 같아도 바뀝니다. 스킬별 예측 (베타): " + "스킬마다 게이지가 얼마나 차는지 배워서, 그만큼 오른 값은 2번만 같아도 바로 바뀝니다. 스킬마다 10번쯤 쓰면 작동합니다. 아덴스킬로 쓰는 양은 클래스 규칙대로 바로 바뀝니다. 스킬창의 스킬을 바꿨으면 배운 값을 지우세요(안 지워도 30번쯤 쓰면 맞춰집니다).", EnumCombo([(GaugeReadMode.Agreed, "기본 (5번 연속 일치하면 반영)"), (GaugeReadMode.SteadyRise, "작은 상승은 2번 일치하면 반영"),
            (GaugeReadMode.Predicted, "스킬별 예측으로 빠르게 반영 (베타)")], _s.Gauge.ReadMode, v => _s.Gauge.ReadMode = v)));
        // Shown for every reading: gains learned under 스킬별 예측 stay saved after switching to another reading, and are used again on
        // switching back.
        var forget = new Button { Content = "이 캐릭터의 배운 값 지우기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 8),
            ToolTip = "스킬별 예측에서 배운 게이지 값을 지웁니다. 스킬창의 스킬을 바꿨을 때 쓰세요." };
        forget.Click += (_, _) => GainsResetRequested?.Invoke();
        p.Children.Add(forget);
        BarRows(p);
        if (ShownSubclass is { } edited && _chars.HasRing(edited.Class.Name)) RingRows(p);   // the edited class's name (ShownClass is the entry key), not the one read
        return p;
    }

    /// <summary>기력·충격 창 (브레이커): its own switch, colours and 불투명도, shown while 편집 중 is a class with a ring.</summary>
    void RingRows(Panel p)
    {
        var r = _s.Ring;
        p.Children.Add(Heading("기력·충격 창"));
        p.Children.Add(Check("켜기", r.On, v => r.On = v, Changed, "링의 기력(왼쪽)과 충격(오른쪽)을 막대 하나로 보여 줍니다. 아덴 게이지를 꺼도 따로 켜집니다."));
        p.Children.Add(ColorRow(Label("기력 색"), r.LeftFill, v => r.LeftFill = v, swatch: false));
        p.Children.Add(ColorRow(Label("충격 색"), r.RightFill, v => r.RightFill = v, swatch: false));
        p.Children.Add(ColorRow(Label("빈 색"), r.Empty, v => r.Empty = v, swatch: false));
        p.Children.Add(SliderRow("불투명도", 0.2, 1, r.Opacity, 0.05, v => $"{v * 100:0}%", v => r.Opacity = v, Changed));
        p.Children.Add(Note("위치와 크기는 편집 모드에서 창을 끌어 바꿉니다."));
    }

    /// <summary>막대: the class shown's split marks, segment colours and glow (GaugeBar), over its classes.json default.
    /// Only for a class picked: 기본 has no 직업각인, so it has no 막대.</summary>
    void BarRows(Panel p)
    {
        if (ShownSubclass is not { } picked)
        {
            p.Children.Add(Note("막대 설정은 편집 중에서 직업각인을 고르면 나옵니다."));
            return;
        }
        var shown = (Class: picked.Class.Name, picked.Sub, Rule: picked.Class.Subclasses[picked.Sub]);
        p.Children.Add(Heading($"막대 ({shown.Class} · {shown.Sub})", help: shown.Rule is null
            ? "이 직업각인은 아직 게이지 기본값이 없습니다. 구분선과 색은 지금 정할 수 있고, 빛나는 조건은 클래스 정보가 추가되면 정해집니다."
            : "구분선은 게이지를 나누는 위치(%)입니다. 언제 빛나는지는 클래스마다 정해져 있고, 여기서는 켜고 끄기와 방식만 바꿉니다."));
        var bar = GaugeBar.Resolve(_s, shown.Class, shown.Sub, shown.Rule);
        string key = GaugeBar.Key(shown.Class, shown.Sub);
        var set = _s.GaugeBars.GetValueOrDefault(key) ?? new GaugeBarSettings();
        GaugeBarSettings Edit() => GaugeBar.Edit(_s, shown.Class, shown.Sub);
        void Rebuild() { Changed(); Dispatcher.BeginInvoke(FillGauge); }
        void SetSplits(IEnumerable<double> splits) { GaugeBar.SetSplits(_s, shown.Class, shown.Sub, shown.Rule, splits); Rebuild(); }

        var splits = new WrapPanel();
        foreach (double split in bar.Splits)
        {
            var box = new TextBox { Text = $"{split * 100:0.#}", Width = 44, Margin = new(0, 0, 2, 0) };
            box.LostKeyboardFocus += (_, _) =>
            {
                if ((double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var pct)
                     || double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct)) && pct is > 0 and < 100 && Math.Abs(pct / 100 - split) > 1e-9)
                    SetSplits(bar.Splits.Where(x => x != split).Append(pct / 100));
                else box.Text = $"{split * 100:0.#}";
            };
            var remove = new Button { Content = "✕", ToolTip = "이 구분선 지우기", Margin = new(0, 0, 10, 0) };
            System.Windows.Automation.AutomationProperties.SetName(remove, "이 구분선 지우기");   // read aloud instead of "✕"
            remove.Click += (_, _) => SetSplits(bar.Splits.Where(x => x != split));
            splits.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { box, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 4, 0) }, remove } });
        }
        var add = new Button { Content = "구분선 추가", ToolTip = "가장 넓은 구간의 가운데에 구분선을 넣습니다." };
        add.Click += (_, _) => { var (from, to) = bar.Segments.MaxBy(s => s.To - s.From); SetSplits(bar.Splits.Append(Math.Round((from + to) / 2, 3))); };
        splits.Children.Add(add);
        p.Children.Add(Row(Label("칸 구분선"), splits));
        p.Children.Add(SliderRow("구분선 두께", 0, 6, bar.SplitThickness, 0.5, v => v == 0 ? "끔" : $"{v:0.#} px", v => Edit().SplitThickness = v, Changed));
        p.Children.Add(ColorRow(Label("구분선 색"), bar.SplitColor, v => Edit().SplitColor = v, swatch: false));

        p.Children.Add(Check("구간 색 통일", set.UnifiedColor, v => { Edit().UnifiedColor = v; Rebuild(); }, () => { }));
        if (!set.UnifiedColor)
            for (int i = 0; i < bar.SegmentColors.Count; i++)
            {
                int n = i;
                p.Children.Add(ColorRow(Label($"{n + 1}번째 구간"), bar.SegmentColors[n], v =>
                {
                    var colors = Edit().SegmentColors;
                    while (colors.Count <= n) colors.Add(bar.Fill);
                    colors[n] = v;
                }, swatch: false));
            }
        if (shown.Rule?.Colors is not null)
        {
            // 바드: gold while a section fills, blue once full (the game's colours by default).
            p.Children.Add(ColorRow(Label("완충 구간"), bar.Fill, v => { Edit().FullColor = v; Rebuild(); }, swatch: false));   // the rows below default from it
            p.Children.Add(ColorRow(Label("충전 중 구간"), bar.FillingColor ?? bar.Fill, v => Edit().FillingColor = v, swatch: false));
        }

        if (shown.Rule is { Ramp.Count: > 0 } rule)
        {
            // 색 단계: one row per level the class uses, labelled with how many full sections turn it on.
            var ramp = Stack([.. rule.Ramp.Distinct().Select(level =>
            {
                int from = rule.Ramp.ToList().IndexOf(level) + 1;
                string when = level == rule.Ramp[^1] ? $"{from}칸 이상" : $"{from}칸";
                return ColorRow(Label($"{"약중강"[level - 1]} ({when} 참)"), bar.RampColors[level - 1], v =>
                {
                    var b = Edit();
                    var defs = GaugeBar.Resolve(_s, shown.Class, shown.Sub, shown.Rule).RampColors;   // from the class's full colour
                    b.RampColors ??= [];
                    while (b.RampColors.Count < defs.Count) b.RampColors.Add(defs[b.RampColors.Count]);
                    b.RampColors[level - 1] = v;
                }, swatch: false);
            })]);
            ramp.Visibility = Shown(set.Ramp);
            p.Children.Add(Check("충전량 따라 진해짐", set.Ramp, v => { Edit().Ramp = v; ramp.Visibility = Shown(v); }, Changed));
            p.Children.Add(ramp);
        }

        var glow = Stack(
            Row("빛남 방식", EnumCombo([(GlowStyle.Pulse, "밝아졌다 어두워짐"), (GlowStyle.Shine, "빛이 지나감"), (GlowStyle.Outline, "테두리")],
                set.GlowStyle, v => Edit().GlowStyle = v)),
            SliderRow("빛남 주기", 0.4, 3, set.GlowPeriod, 0.1, v => $"{v:0.0}초", v => Edit().GlowPeriod = v, Changed));
        glow.Visibility = Shown(set.GlowEnabled);
        p.Children.Add(Check("완충 빛남", set.GlowEnabled, v => { Edit().GlowEnabled = v; glow.Visibility = Shown(v); }, Changed));
        p.Children.Add(glow);
        var reset = new Button { Content = "기본값으로", IsEnabled = _s.GaugeBars.ContainsKey(key),
            ToolTip = "이 직업각인의 막대 설정을 지우고 기본값(classes.json)으로 되돌립니다." };
        reset.Click += (_, _) => { _s.GaugeBars.Remove(key); Rebuild(); };
        p.Children.Add(Row("", reset));
    }
}
