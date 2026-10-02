using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>아덴 게이지 and 막대: shown once the gauge is clicked, under the 오버레이 tab's preview or on the 레이아웃
/// 편집기's left. 막대 is per 클래스·직업각인: the class shown's only.</summary>
public partial class SettingsWindow
{
    /// <summary>The gauge's options; shown while the gauge is the part picked (<see cref="ShowBox"/>).</summary>
    internal StackPanel GaugeBox { get; } = new() { Margin = new(16, 0, 16, 12), Visibility = Visibility.Collapsed };

    void FillGauge()
    {
        GaugeBox.Children.Clear();
        GaugeBox.Children.Add(GaugePanel());
        GaugeBox.Children.Add(ResetButton("아덴 게이지 되돌리기", "게이지의 색·두께·읽기 방식과 모든 클래스·직업각인의 막대 모양(구분선, 색, 빛남)을 기본값으로 되돌립니다. 클래스의 게이지 규칙과 배운 게이지 값은 남습니다.",
            () => SettingsResets.Gauge(_s)));
    }

    UIElement GaugePanel()
    {
        var g = _s.Gauge;
        var p = new StackPanel();
        p.Children.Add(Heading("아덴 게이지", first: true));
        p.Children.Add(Note("아덴 게이지를 추적하는 직업각인이면 오버레이 위쪽에 막대로 표시됩니다."));
        if (!(_chars.ActiveClass is { } cls && _chars.Active is { } c && cls.Subclasses.GetValueOrDefault(c.Subclass) is not null))
            p.Children.Add(Note("현재 직업각인은 아덴 게이지를 추적하지 않습니다 (나중에 추가 예정)."));
        else if (_chars.Gauge is null)
            p.Children.Add(Note("게이지 정의 없음: 이 직업각인의 게이지 파일(Profiles/aden)이 아직 없어서 읽지 않습니다."));
        p.Children.Add(ColorRow(Label("채움 색"), g.Fill, v => g.Fill = v, swatch: false));
        p.Children.Add(ColorRow(Label("빈 칸 색"), g.Empty, v => g.Empty = v, swatch: false));
        p.Children.Add(SliderRow("두께", 2, 16, g.Thickness, 1, v => $"{v:0} px", v => g.Thickness = v, Changed));
        p.Children.Add(Note("두꺼워지면 위쪽으로 늘어나고, 아래 칸의 위치는 그대로입니다."));
        p.Children.Add(SliderRow("불투명도", 0.2, 1, g.Opacity, 0.05, v => $"{v * 100:0}%", v => g.Opacity = v, Changed));
        var beta = new StackPanel();
        beta.Children.Add(Note("스킬마다 게이지가 얼마나 차는지 쓸 때마다 배워, 그만큼 오르는 값은 2번 일치하면 바로 반영합니다. 스킬마다 10번쯤 쓴 뒤부터 적용되고, 배운 값은 캐릭터(클래스·직업각인)마다 저장되어 앱을 다시 켜도 이어집니다. 아덴스킬로 게이지를 쓰는 양은 배우지 않고 클래스 규칙으로 바로 반영합니다. 배운 값은 캐릭터·인식 → 배운 값에서 지웁니다."));
        beta.Visibility = Shown(g.ReadMode == GaugeReadMode.Predicted);
        p.Children.Add(Row("게이지 읽기", EnumCombo([(GaugeReadMode.Agreed, "기본 (5번 연속 일치하면 반영)"), (GaugeReadMode.SteadyRise, "작은 상승은 2번 일치하면 반영"),
            (GaugeReadMode.Predicted, "스킬별 예측으로 빠르게 반영 (베타)")], g.ReadMode, v => { g.ReadMode = v; beta.Visibility = Shown(v == GaugeReadMode.Predicted); })));
        p.Children.Add(beta);
        BarRows(p);
        return p;
    }

    /// <summary>막대: the class shown's split marks, segment colours and glow (GaugeBar), over its classes.json default.
    /// Only for a class picked: 기본 has no 직업각인, so it has no 막대.</summary>
    void BarRows(Panel p)
    {
        if (_chars.Active is not { } a)
        {
            p.Children.Add(Note("막대 설정은 클래스·직업각인을 고르면 나옵니다."));
            return;
        }
        var shown = _chars.Classes.SelectMany(k => k.Subclasses.Select(sc => (Class: k.Name, Sub: sc.Key, Rule: sc.Value)))
            .FirstOrDefault(x => x.Class == a.Class && x.Sub == a.Subclass);
        if (shown.Class is null) return;   // a class the profiles don't know
        p.Children.Add(Heading($"막대 ({shown.Class} · {shown.Sub})"));
        var bar = GaugeBar.Resolve(_s, shown.Class, shown.Sub, shown.Rule);
        string key = GaugeBar.Key(shown.Class, shown.Sub);
        var set = _s.GaugeBars.GetValueOrDefault(key) ?? new GaugeBarSettings();
        GaugeBarSettings Edit() => GaugeBar.Edit(_s, shown.Class, shown.Sub);
        void Rebuild() { Changed(); Dispatcher.BeginInvoke(FillGauge); }
        void SetSplits(IEnumerable<double> splits) { GaugeBar.SetSplits(_s, shown.Class, shown.Sub, shown.Rule, splits); Rebuild(); }

        p.Children.Add(Note(shown.Rule is null
            ? "이 직업각인은 아직 게이지 기본값이 없습니다. 구분선과 색은 정해 둘 수 있고, 빛나는 조건은 클래스 정보가 추가될 때 정해집니다."
            : "구분선은 게이지를 나누는 위치(%)입니다. 빛나는 조건은 클래스마다 정해져 있고, 여기서는 켜고 끄기와 방식만 바꿉니다."));
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
            remove.Click += (_, _) => SetSplits(bar.Splits.Where(x => x != split));
            splits.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { box, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 4, 0) }, remove } });
        }
        var add = new Button { Content = "구분선 추가", ToolTip = "가장 넓은 구간의 가운데에 구분선을 넣습니다." };
        add.Click += (_, _) => { var (from, to) = bar.Segments.MaxBy(s => s.To - s.From); SetSplits(bar.Splits.Append(Math.Round((from + to) / 2, 3))); };
        splits.Children.Add(add);
        p.Children.Add(Row(Label("구분선"), splits));
        p.Children.Add(SliderRow("구분선 두께", 0, 6, bar.SplitThickness, 0.5, v => v == 0 ? "끔" : $"{v:0.#} px", v => Edit().SplitThickness = v, Changed));
        p.Children.Add(ColorRow(Label("구분선 색"), bar.SplitColor, v => Edit().SplitColor = v, swatch: false));

        p.Children.Add(Check("모든 구간 같은 색 (채움 색)", set.UnifiedColor, v => { Edit().UnifiedColor = v; Rebuild(); }, () => { }));
        if (!set.UnifiedColor)
            for (int i = 0; i < bar.SegmentColors.Count; i++)
            {
                int n = i;
                p.Children.Add(ColorRow(Label($"   {n + 1}번째 구간"), bar.SegmentColors[n], v =>
                {
                    var colors = Edit().SegmentColors;
                    while (colors.Count <= n) colors.Add(bar.Fill);
                    colors[n] = v;
                }, swatch: false));
            }
        if (shown.Rule?.Colors is not null)
        {
            // 바드: gold while a section fills, blue once full (the game's colours by default).
            p.Children.Add(ColorRow(Label("다 찬 구간 색"), bar.Fill, v => { Edit().FullColor = v; Rebuild(); }, swatch: false));   // the rows below default from it
            p.Children.Add(ColorRow(Label("채우는 중인 구간 색"), bar.FillingColor ?? bar.Fill, v => Edit().FillingColor = v, swatch: false));
        }

        if (shown.Rule is { Ramp.Count: > 0 } rule)
        {
            // 색 단계: one row per level the class uses, labelled with how many full sections turn it on.
            var ramp = Stack([.. rule.Ramp.Distinct().Select(level =>
            {
                int from = rule.Ramp.ToList().IndexOf(level) + 1;
                string when = level == rule.Ramp[^1] ? $"{from}칸 이상" : $"{from}칸";
                return ColorRow(Label($"   {"약중강"[level - 1]} ({when} 참)"), bar.RampColors[level - 1], v =>
                {
                    var b = Edit();
                    var defs = GaugeBar.Resolve(_s, shown.Class, shown.Sub, shown.Rule).RampColors;   // from the class's full colour
                    b.RampColors ??= [];
                    while (b.RampColors.Count < defs.Count) b.RampColors.Add(defs[b.RampColors.Count]);
                    b.RampColors[level - 1] = v;
                }, swatch: false);
            })]);
            ramp.Visibility = Shown(set.Ramp);
            p.Children.Add(Check("구간이 찰수록 진해짐 (이 직업각인만)", set.Ramp, v => { Edit().Ramp = v; ramp.Visibility = Shown(v); }, Changed));
            p.Children.Add(ramp);
        }

        var glow = Stack(
            Row("빛나는 방식", EnumCombo([(GlowStyle.Pulse, "밝아졌다 어두워짐"), (GlowStyle.Shine, "빛이 지나감"), (GlowStyle.Outline, "테두리")],
                set.GlowStyle, v => Edit().GlowStyle = v)),
            SliderRow("빛나는 주기", 0.4, 3, set.GlowPeriod, 0.1, v => $"{v:0.0}초", v => Edit().GlowPeriod = v, Changed));
        glow.Visibility = Shown(set.GlowEnabled);
        p.Children.Add(Check("빛남", set.GlowEnabled, v => { Edit().GlowEnabled = v; glow.Visibility = Shown(v); }, Changed));
        p.Children.Add(glow);
        var reset = new Button { Content = "기본값으로", IsEnabled = _s.GaugeBars.ContainsKey(key),
            ToolTip = "이 직업각인의 막대 설정을 지우고 기본값(classes.json)으로 되돌립니다." };
        reset.Click += (_, _) => { _s.GaugeBars.Remove(key); Rebuild(); };
        p.Children.Add(Row("", reset));
    }
}
