using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 레이드 → HP 변경 감지 (HpChange): the boss max HP values found to differ from raids.json, each with how many raids
/// read it, 확정 (kept over raids.json; the row leaves the list), 복사 (the gate's raids.json line) and 삭제; this raid's unconfirmed
/// ones, faint; and a closed-by-default table of every raid's boss HP (raids.json and 확정), each 확정 with 되돌리기.</summary>
public partial class SettingsWindow
{
    IReadOnlyList<FightHp> _fightHp = [];
    bool _hpTableOpen;

    /// <summary>This raid's HP values (Engine.FightHpChanged); UI thread.</summary>
    public void SetFightHp(IReadOnlyList<FightHp> list)
    {
        _fightHp = list;
        FillRaid();
    }

    /// <summary>--demo --hp-table=px: the boss HP table open with one 확정 (종막 2-1관문 하드, 1% under), the page scrolled px down,
    /// for screenshots. --demo saves nothing.</summary>
    internal void DemoHpTable(string scroll)
    {
        _hpTableOpen = true;
        if (HpChange.Table(RaidGates, _s.Raids).FirstOrDefault(x => x.Gate.Gate.Contains("2-1") && x.Difficulty == "하드") is { Shipped: { } hp } row)
            HpChange.SetFixed(RaidGates, _s.Raids, row.Gate.Id, row.Difficulty, hp / 100 * 99);
        FillRaid();
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (double.TryParse(scroll, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y))
                Body.ScrollToVerticalOffset(y);
        });
    }

    void HpChangeRows(StackPanel p, Action again)
    {
        var r = _s.Raids;
        var found = HpChange.Found(RaidGates, r);
        var unconfirmed = _fightHp.Where(f => f.Trust == HpTrust.Unconfirmed).ToList();
        p.Children.Add(SubHeading("HP 변경 감지",
            "게임 업데이트로 보스 체력이 바뀌어도 보스와 난이도를 계속 인식하도록, 콘텐츠 난이도와 맞는 값을 이 PC에 저장합니다. " +
            "처음 보는 난이도의 값은 다음 레이드에서 같은 값이 다시 보이면 씁니다. 확정하면 앱 업데이트의 값보다 이 값을 계속 씁니다. " +
            "어디에도 보내지 않습니다. 복사한 줄을 알려 주시면 다음 버전에 넣습니다."));
        var effective = HpChange.Effective(RaidGates, r, _fightHp);
        void Button(List<UIElement> to, string text, string tip, Action click)
        {
            var b = new System.Windows.Controls.Button { Content = text, Padding = new(10, 3, 10, 3), Margin = new(to.Count == 0 ? 8 : 6, 0, 0, 0), ToolTip = tip };
            b.Click += (_, _) => { click(); Changed(); again(); };
            to.Add(b);
        }
        foreach (var f in found)
        {
            var buttons = new List<UIElement>();
            void Drop() => HpCrops.Delete(f.Gate.Id, f.Difficulty, f.To);   // the row leaves the list, its picture with it
            Button(buttons, "확정", "이 값을 계속 씁니다 (앱 업데이트의 값보다 먼저). 아래 보스 체력 표에서 되돌릴 수 있습니다.",
                () => { if (HpChange.Confirm(RaidGates, r, f.Gate.Id, f.Difficulty)) Drop(); });
            if (!f.Pending)
            {
                var copy = new System.Windows.Controls.Button { Content = "복사", Padding = new(10, 3, 10, 3), Margin = new(6, 0, 0, 0), ToolTip = "raids.json 줄 복사" };
                copy.Click += (_, _) => Clipboard.SetText(HpChange.HpLine(RaidData.Find(effective, f.Gate.Id)!));
                buttons.Add(copy);
            }
            Button(buttons, "삭제", "이 값을 지웁니다.", () => { if (HpChange.Undo(r, f.Gate.Id, f.Difficulty)) Drop(); });
            string what = Change(f.From, f.To) + $" · 레이드 {f.Raids}번" + (f.Pending ? " · 다음 레이드에서 다시 보면 씀" : "");
            p.Children.Add(Row(RaidBadgeView.Of(f.Gate.Raid, f.Difficulty), [Label($"{RaidData.Label(f.Gate)}  {what}", f.Pending ? 0.6 : 0.8), .. buttons]));
            CropRow(p, f.Gate.Id, f.Difficulty, f.To);
        }
        foreach (var f in unconfirmed)
            if (RaidData.Find(RaidGates, f.GateId) is { } gate)
            {
                p.Children.Add(Row(RaidBadgeView.Of(gate.Raid, f.Difficulty),
                    Label($"{RaidData.Label(gate)}  " + Change(gate.Hp.TryGetValue(f.Difficulty, out var was) ? was : null, f.Hp) + " · 미확인 (이번 레이드만)", 0.5)));
                CropRow(p, f.GateId, f.Difficulty, f.Hp);
            }
        if (found.Count == 0 && unconfirmed.Count == 0) p.Children.Add(Note("다른 값을 찾지 못했습니다."));
        HpTable(p, r, again);
    }

    /// <summary>The boss HP table: a toggle (closed by default), then per raid × difficulty raids.json's value and the 확정 one.</summary>
    void HpTable(StackPanel p, RaidSettings r, Action again)
    {
        var toggle = new System.Windows.Controls.Button { Content = _hpTableOpen ? "보스 체력 표 접기" : "보스 체력 표 펼치기", Padding = new(10, 3, 10, 3) };
        toggle.Click += (_, _) => { _hpTableOpen = !_hpTableOpen; again(); };
        p.Children.Add(Row("", toggle));
        if (!_hpTableOpen) return;
        p.Children.Add(Note("값을 누르면 고칠 수 있습니다. Enter로 확정, Esc로 취소. 앱의 값을 넣으면 앱의 값으로 돌아갑니다."));
        foreach (var raid in HpChange.Table(RaidGates, r).GroupBy(x => x.Gate.Raid))
        {
            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            heading.Children.Add(RaidBadgeView.Of(raid.Key, null));
            heading.Children.Add(new TextBlock { Text = raid.Key, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(Row(heading));
            foreach (var gate in raid.GroupBy(x => x.Gate))
            {
                var name = Label(string.IsNullOrWhiteSpace(gate.Key.Gate) ? gate.Key.Raid : gate.Key.Gate);
                name.Margin = new(16, 0, 0, 0);
                p.Children.Add(Row(name, [.. gate.Select(x => HpCell(r, x, again))]));
            }
        }
    }

    const double HpCellWidth = 94;
    static readonly System.Windows.Media.FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>One difficulty of a gate: its capsule, the value in use as a button (click or Enter: a box with the full number;
    /// Enter or leaving it 확정 via HpChange.SetFixed, Esc cancels, a bad value says so in place), and for a 확정 one the app's value
    /// under it with 되돌리기.</summary>
    UIElement HpCell(RaidSettings r, HpTableRow row, Action again)
    {
        var cell = new StackPanel { Width = HpCellWidth, Margin = new(0, 2, 6, 2) };
        cell.Children.Add(RaidBadgeView.Of(null, row.Difficulty, 11));
        long? shown = row.Fixed ?? row.Shipped;
        var value = new System.Windows.Controls.Button
        {
            Content = shown is { } v ? BossHpGraph.AmountText(v) : "없음", Padding = new(4, 2, 4, 2), Margin = new(-4, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent, ToolTip = shown is { } t ? $"{t:N0} · 눌러서 고치기" : "눌러서 넣기",
        };
        System.Windows.Automation.AutomationProperties.SetName(value, $"{RaidData.Label(row.Gate)} {row.Difficulty} 체력 {value.Content}");
        if (row.Fixed is not null) value.SetResourceReference(ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        cell.Children.Add(value);
        var error = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        value.Click += (_, _) => EditHp(cell, value, error, r, row, again);
        if (row.Fixed is not null)
        {
            var app = new StackPanel { Orientation = Orientation.Horizontal };
            app.Children.Add(new TextBlock { Text = row.Shipped is { } s ? $"앱 {BossHpGraph.AmountText(s)}" : "앱 값 없음", FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
            var undo = new System.Windows.Controls.Button
            {
                Content = "", FontFamily = IconFont, FontSize = 11, Padding = new(4, 2, 4, 2), Margin = new(4, 0, 0, 0),
                Background = System.Windows.Media.Brushes.Transparent, BorderBrush = System.Windows.Media.Brushes.Transparent, ToolTip = "앱 값으로 되돌리기",
            };
            System.Windows.Automation.AutomationProperties.SetName(undo, "앱 값으로 되돌리기");
            undo.Click += (_, _) => { HpChange.Undo(r, row.Gate.Id, row.Difficulty); Changed(); again(); };
            app.Children.Add(undo);
            cell.Children.Add(app);
        }
        cell.Children.Add(error);
        return cell;
    }

    /// <summary>The value button swapped for a box with the full number, until Enter / leaving (saved when it changed) or Esc.</summary>
    void EditHp(StackPanel cell, System.Windows.Controls.Button value, TextBlock error, RaidSettings r, HpTableRow row, Action again)
    {
        long? shown = row.Fixed ?? row.Shipped;
        var box = new TextBox { Text = shown is { } v ? $"{v:N0}" : "", Margin = new(0, 2, 0, 0), ToolTip = "숫자 또는 1조 2985억처럼" };
        System.Windows.Automation.AutomationProperties.SetName(box, $"{RaidData.Label(row.Gate)} {row.Difficulty} 체력");
        int at = cell.Children.IndexOf(value);
        cell.Children[at] = box;
        bool done = false;
        void Close(bool refocus = true)   // not on focus loss: the focus went to whatever was clicked
        {
            done = true;
            cell.Children[at] = value;
            if (refocus) value.Focus();
        }
        void Commit()
        {
            if (done) return;
            if (HpChange.ParseHp(box.Text) is not { } hp || hp <= 0)
            {
                error.Text = "숫자나 1조 2985억처럼 넣어 주세요.";
                error.Visibility = Visibility.Visible;
                return;
            }
            done = true;
            if (!HpChange.SetFixed(RaidGates, r, row.Gate.Id, row.Difficulty, hp)) { Close(); return; }
            Changed();
            // After the click that took the focus: rebuilt now, the control under the mouse would go before its mouse-up.
            Dispatcher.BeginInvoke(again, System.Windows.Threading.DispatcherPriority.Background);
        }
        box.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Commit(); }
            else if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; error.Visibility = Visibility.Collapsed; Close(); }
        };
        box.LostKeyboardFocus += (_, _) =>   // a bad value left behind is dropped, not kept open with no cue
        {
            if (done) return;
            if (HpChange.ParseHp(box.Text) is > 0) Commit();
            else { error.Visibility = Visibility.Collapsed; Close(refocus: false); }
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    /// <summary>The boss HP text row the value was first read from (HpCrops), under its row, so a misread shows; nothing when
    /// there's no picture (a value found before 0.19, or one the engine couldn't save).</summary>
    static void CropRow(StackPanel p, string gateId, string difficulty, long hp)
    {
        string file = HpCrops.PathOf(gateId, difficulty, hp);
        if (!System.IO.File.Exists(file)) return;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;   // the file stays free for 확정 / 삭제 to delete
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            p.Children.Add(Row("", new System.Windows.Controls.Image { Source = bmp, Height = CropHeight, Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "이 값을 읽은 화면 (보스 체력 줄)" }));
        }
        catch (Exception e)   // a broken file shows no picture, never breaks the page
        {
            System.Diagnostics.Trace.WriteLine($"[raid] HP crop not shown: {e.Message}");
        }
    }

    const double CropHeight = 24;   // the 1080p row is 730×36

    static string Change(long? from, long to) => from is { } f ? $"{f:N0} → {to:N0}" : $"새 값 {to:N0}";
}
