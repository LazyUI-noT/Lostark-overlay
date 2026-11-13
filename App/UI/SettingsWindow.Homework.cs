using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 숙제 (homework.plan.md): who is being played, each character's picks (up to 5 raids at a difficulty) with this
/// week's tags, characters added by hand or hidden, and the tags' names.</summary>
public partial class SettingsWindow
{
    const string AreaHomework = "레이드 숙제";
    const int MaxCustomLevels = 3;
    static readonly SolidColorBrush ClearedBrush = new(Color.FromRgb(0x2E, 0xA0, 0x43)), UnsureBrush = new(Color.FromRgb(0xE0, 0x31, 0x31)), UndoneBrush = new(Color.FromRgb(0xE4, 0xE6, 0xE8));
    const string HomeworkLead = "캐릭터마다 이번 주(수요일 오전 6시부터) 클리어한 레이드를 보여 줍니다. 캐릭터는 ESC 게임 메뉴와 '다른 캐릭터 바로 접속' 창에서 읽습니다. " +
        "전투 기록이 마지막 관문을 클리어로 저장하면 그 레이드가 지워집니다. 전투 기록이나 DPS 미터기가 꺼져 있으면 보스 체력바로 판단합니다. 확실하지 않으면 !(확인필요)로 두니 눌러서 고치세요. 고른 것보다 낮은 난이도로 깨면 ↓, 높은 난이도로 깨면 ↑가 붙고 고른 난이도가 올라갑니다. 앱 없이 깼다면 레이드 줄의 [클리어]를 누르세요.";
    readonly CardStack _secHomework = new();

    /// <summary>Who is played, the character list and the clears (null: none yet, the page says so).</summary>
    internal HomeworkService? Homework { get; set; }

    /// <summary>The character, the list or the clears changed: the page and the 전투 기록 chips again (UI thread).</summary>
    public void HomeworkUpdated()
    {
        if (_shownArea == AreaHomework) FillHomework();
        if (_shownArea == AreaRecords && _opened is null) FillRecords();
    }

    void FillHomework()
    {
        var p = Refill(_secHomework, AreaHomework, HomeworkLead);
        if (!_s.HomeworkOn(CredentialVault.Saved))   // a new install without a key (settings-access.plan.md B)
        {
            p.Children.Add(Note("숙제 기능은 로아 오픈 API 키가 필요합니다. 키를 넣으면 바로 켜집니다."));
            var go = ApiButton("API 키 넣기");
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new(0, 6, 0, 0);
            go.Click += (_, _) => Go(AreaApi);
            p.Children.Add(go);
            return;
        }
        if (Homework is not { } hw) { p.Children.Add(Note("숙제는 게임과 함께 켜졌을 때 씁니다.")); p.Children.Add(CapsulePalette()); return; }
        p.Children.Add(Note(hw.Current is { } now ? $"지금 캐릭터: {now}" : "지금 캐릭터: 캐릭터 불명 (ESC를 눌러 게임 메뉴를 열면 읽습니다)"));
        p.Children.Add(Note(Core.Domain.Homework.SourceOf(_s.Meter.On, _s.Meter.Record, _s.Raids.BossHpRead) switch
        {
            HomeworkSource.RecordedFight => "클리어는 전투 기록이 판단합니다. 게임의 전투 분석기를 닫아 두면 기록이 없어 보스 체력바로 판단하니, ! 표시(확인필요)를 직접 확인하세요.",
            HomeworkSource.BossBar => "전투 기록이나 DPS 미터기가 꺼져 있어 보스 체력바를 보고 판단합니다. 전투 기록보다 틀리기 쉬우니 ! 표시(확인필요)는 직접 확인하세요.",
            _ => "보스 체력 읽기가 꺼져 있어 클리어를 자동으로 표시하지 못합니다. 아래 [클리어]를 직접 누르거나 보스 체력 읽기를 켜세요.",
        }));
        p.Children.Add(Row("", Check("다른 캐릭터 접속 창에 숙제 표시", _s.Homework.ShowOnGrid, v => _s.Homework.ShowOnGrid = v, Changed,
            "다른 캐릭터 바로 접속 창의 캐릭터 칸 아래에 고른 레이드를 작게 보여 줍니다. 클리어한 것은 줄을 긋습니다.")));
        p.Children.Add(Row("", Check("아이템 레벨이 바뀌면 레이드 자동으로 다시 고르기 (모든 캐릭터)", _s.Homework.FollowLevel, v => _s.Homework.FollowLevel = v, Changed,
            "켜면 캐릭터의 아이템 레벨이 바뀔 때 들어갈 수 있는 레이드 중 입장 레벨이 높은 3개로 다시 고릅니다. 직접 고친 것도 바뀝니다. 끄면 그대로 둡니다. 새 캐릭터는 레벨을 처음 읽을 때 항상 3개를 골라 줍니다.")));
        p.Children.Add(Row("", Check("게임 순서 따르기", _s.Homework.FollowGameOrder, v => _s.Homework.FollowGameOrder = v, () => { Changed(); FillHomework(); },
            "켜면 아래 캐릭터를 다른 캐릭터 바로 접속 창의 순서대로 보여 줍니다. 게임에서 순서를 바꾸면 따라갑니다. ≡를 끌어 순서를 바꾸면 꺼지고, 끈 동안은 직접 정한 순서를 지킵니다.")));
        var clears = hw.Clears();
        var shown = CardOrder.TabOrder(_s.Homework.Characters, _s.Homework.Order, _s.Homework.FollowGameOrder).Where(c => !c.Hidden).ToList();
        p.Children.Add(Note(shown.Count == 0
            ? "아직 읽은 캐릭터가 없습니다. 게임에서 ESC → 다른 캐릭터 바로 접속을 열면 칸의 캐릭터를 모두 읽습니다. 아래에서 직접 추가해도 됩니다."
            : "다른 캐릭터 바로 접속 창을 열면 목록에 없는 캐릭터를 추가합니다. 칸의 작은 글씨는 잘못 읽힐 수 있으니 이름을 고치세요. 그 칸을 누르면 큰 글씨 이름으로 바뀝니다."));
        foreach (var c in shown) p.Children.Add(CharacterCard(hw, c, clears));
        p.Children.Add(AddCharacterRow(hw));
        if (_s.Homework.Characters.Where(c => c.Hidden).ToList() is { Count: > 0 } hidden) p.Children.Add(HiddenRow(hidden));
        p.Children.Add(CustomRaids(hw));
        p.Children.Add(TagNames(hw));
        p.Children.Add(CapsulePalette());
    }

    static readonly string[] CapsuleSwatches = ["#5E8FD6", "#4CB08A", "#F07C2C", "#8F4FD6", "#D6594C", "#D6B34C", "#C85FA8", "#3FB6C4", "#8C8C8C"];

    /// <summary>레이드 난이도 색 (user, 2026-11-09): one palette for every capsule in 설정 (숙제, 전투 기록, 레이드's HP 변경 감지), saved
    /// in <see cref="Settings.RaidCapsules"/>. Per difficulty: its capsule, a few swatches, the colour dialog and 기본값.</summary>
    UIElement CapsulePalette()
    {
        var panel = new StackPanel { Margin = new(0, 12, 0, 0) };
        panel.Children.Add(CapsulePaletteHeader());
        if (!_capsulePaletteOpen) return panel;
        var colors = _s.RaidCapsules;
        foreach (var tier in RaidCapsuleColors.Editable)
        {
            void Set(string hex) { colors.Set(tier, hex); Changed(); FillHomework(); }
            var parts = new List<UIElement> { RaidBadgeView.Of(null, TierName(tier)) };
            foreach (var hex in CapsuleSwatches)
            {
                var chosen = colors.HexOf(tier) == hex;
                var dot = new Button
                {
                    Width = 20, Height = 20, Padding = new(0), ToolTip = hex, Cursor = System.Windows.Input.Cursors.Hand,
                    BorderThickness = new(chosen ? 2 : 1),
                };
                Fill(dot, UiColor.Parse(hex, Colors.Gray), Brushes.White, border: chosen ? Brushes.White : Brushes.Gray);
                System.Windows.Automation.AutomationProperties.SetName(dot, $"{TierName(tier)} {hex}");
                dot.Click += (_, _) => Set(hex);
                parts.Add(dot);
            }
            var pick = new Button { Content = "색 선택…" };
            pick.Click += (_, _) => { if (UiColor.Pick(colors.HexOf(tier)) is { } picked) Set(picked); };
            var reset = new Button { Content = "기본값", IsEnabled = !colors.IsDefault(tier) };
            reset.Click += (_, _) => { colors.Reset(tier); Changed(); FillHomework(); };
            parts.AddRange([pick, reset]);
            panel.Children.Add(Row(TierName(tier), [.. parts]));
        }
        panel.Children.Add(Note("전투 기록, 레이드의 HP 변경 감지 등 난이도를 색으로 보여 주는 곳 모두에 쓰입니다. 이 컴퓨터에서만 바뀌고 파티원에게는 보이지 않습니다."));
        return panel;
    }

    /// <summary>난이도 색 starts folded (user, 2026-11-10): open or closed lasts while the window is open, through refills.</summary>
    bool _capsulePaletteOpen;

    /// <summary>The fold button: ▸/▾, the title and, folded, every capsule as it looks now.</summary>
    Button CapsulePaletteHeader()
    {
        var face = new StackPanel { Orientation = Orientation.Horizontal };
        face.Children.Add(Muted(new TextBlock { Text = _capsulePaletteOpen ? "▾" : "▸", Width = 14, VerticalAlignment = VerticalAlignment.Center }));
        face.Children.Add(new TextBlock { Text = "난이도 색", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        if (!_capsulePaletteOpen)
            foreach (var tier in RaidCapsuleColors.Editable)
                face.Children.Add(new ContentControl { Content = RaidBadgeView.Of(null, TierName(tier), 11), Margin = new(0, 0, 4, 0), Focusable = false });
        var header = new Button
        {
            Content = face, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(2), Margin = new(0, 0, 0, 4),
            Background = Brushes.Transparent, BorderThickness = new(0), Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = _capsulePaletteOpen ? "접습니다." : "펼쳐서 난이도마다 색을 바꿉니다.",
        };
        System.Windows.Automation.AutomationProperties.SetName(header, "난이도 색");
        System.Windows.Automation.AutomationProperties.SetItemStatus(header, _capsulePaletteOpen ? "펼침" : "접힘");
        header.Click += (_, _) => { _capsulePaletteOpen = !_capsulePaletteOpen; FillHomework(); };
        return header;
    }

    static string TierName(DifficultyTier tier) => tier switch
    {
        DifficultyTier.Normal => "노말", DifficultyTier.Hard => "하드", DifficultyTier.Nightmare => "나메", DifficultyTier.Single => "싱글",
        DifficultyTier.Matching => "매칭", DifficultyTier.Step1 => "1단계", DifficultyTier.Step2 => "2단계", _ => "3단계",
    };

    UIElement CharacterCard(HomeworkService hw, HomeworkCharacter c, IReadOnlyList<HomeworkClear> clears)
    {
        var card = new StackPanel { Margin = new(0, 6, 0, 10), Background = Brushes.Transparent, AllowDrop = true };   // a background: drops hit the gaps too
        var head = new WrapPanel();
        head.Children.Add(DragHandle(card, c));
        var name = new TextBox { Text = c.Name, MinWidth = 90, FontWeight = FontWeights.SemiBold, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0),
            ToolTip = "캐릭터 이름. 잘못 읽혔으면 고칩니다. 이번 주 클리어와 전투 기록도 새 이름으로 옮깁니다. 이미 있는 이름으로 고치면 둘을 합칩니다." };
        bool done = false;   // the page is rebuilt after a rename, and the old box still gets its LostFocus
        void RenameTo()
        {
            if (done) return;
            var cleaned = CharacterName.Clean(name.Text);
            if (cleaned == c.Name) { name.Text = c.Name; return; }
            done = true;
            if (cleaned is null || !hw.Rename(c, cleaned))
            {
                name.Text = c.Name;
                done = false;
                MessageBox.Show(this, cleaned is null ? "이름을 확인해 주세요(글자·숫자 2~16자)." : "이름을 바꾸지 못했습니다. 잠시 뒤 다시 해 주세요.", AreaHomework);
                return;
            }
            FillHomework();
        }
        name.LostFocus += (_, _) => RenameTo();
        name.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) RenameTo(); };
        head.Children.Add(name);
        if (c.FromCard)
            head.Children.Add(Muted(new TextBlock { Text = "· 카드에서 읽음", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0),
                ToolTip = "칸의 작은 글씨에서 읽은 이름입니다. 칸을 눌러 큰 글씨가 읽히면 그 이름으로 바뀝니다." }));
        if (c.ClassName is not null)
            head.Children.Add(Muted(new TextBlock { Text = RecordCharacter.Label(c.ClassName, c.Subclass), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) }));
        if (c.Name == hw.Current) head.Children.Add(Muted(new TextBlock { Text = "· 지금 플레이 중", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) }));
        var level = new TextBox { Width = 56, Text = c.ItemLevel?.ToString() ?? "", VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0), ToolTip = "아이템 레벨. 카드에서 읽으며, 잘못 읽었으면 고칩니다." };
        level.LostFocus += (_, _) =>
        {
            if (Core.Domain.Homework.TypedLevel(level.Text) is { } typed) hw.TypedLevel(c, typed);
            else if (level.Text.Trim().Length == 0) hw.TypedLevel(c, null);
            else level.Text = c.ItemLevel?.ToString() ?? "";
        };
        head.Children.Add(level);
        foreach (var pick in c.Picks)
            if (hw.TagOf(c, pick, clears) is { } tag) head.Children.Add(Core.Domain.Homework.IsAdded(pick) ? ClickableChip(hw, c, pick, tag) : TagChip(tag));
        var hide = new Button { Content = "숨기기", Margin = new(12, 0, 0, 0) };
        hide.Click += (_, _) => { c.Hidden = true; Changed(); FillHomework(); };
        head.Children.Add(hide);
        card.Children.Add(head);
        for (int i = 0; i < c.Picks.Count; i++) card.Children.Add(PickRow(hw, c, i, clears));
        if (c.Picks.Count < HomeworkCharacter.MaxPicks && hw.Raids.Count > 0)
        {
            var add = new Button { Content = "+ 레이드", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(16, 4, 0, 0) };
            add.Click += (_, _) =>
            {
                var raid = hw.Raids.FirstOrDefault(r => c.Picks.All(x => x.Raid != r.Id)) ?? hw.Raids[0];
                c.Picks = [.. c.Picks, new HomeworkPick(raid.Id, raid.Levels[^1].Difficulty)];
                Changed();
                FillHomework();
            };
            card.Children.Add(add);
        }
        return card;
    }

    const string CharacterDrag = "GaugeOverlay.HomeworkCharacter";   // not text: the name and level boxes won't take it

    /// <summary>≡: drag a character onto another to take its place (card-order.plan.md); a drag turns 게임 순서 따르기 off, keeping the
    /// tab's order as it was shown.</summary>
    UIElement DragHandle(StackPanel card, HomeworkCharacter c)
    {
        var handle = new TextBlock { Text = "≡", FontSize = 16, Cursor = System.Windows.Input.Cursors.SizeNS, VerticalAlignment = VerticalAlignment.Center,
            Margin = new(0, 0, 8, 0), Opacity = 0.6, ToolTip = "끌어서 순서 바꾸기 (선택하고 ↑/↓도 됩니다)" };
        // The keyboard's way: focus ≡ (Tab) and ↑/↓ moves the card one place (audit 2026-11-12).
        var keys = UiBuild.Bare<Button>(handle, $"{c.Name} 순서: ↑/↓로 옮기기", $"hw-order:{c.Name}");
        (keys.VerticalAlignment, keys.Cursor) = (VerticalAlignment.Center, System.Windows.Input.Cursors.SizeNS);
        keys.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;   // Alt+↑ arrives as a system key
            int by = key switch { System.Windows.Input.Key.Up => -1, System.Windows.Input.Key.Down => 1, _ => 0 };
            if (by == 0) return;
            e.Handled = true;
            var hw = _s.Homework;
            var tab = CardOrder.TabOrder(hw.Characters, hw.Order, hw.FollowGameOrder);
            var moved = CardOrder.Nudge(tab, c.Name, by);
            if (moved.SequenceEqual(tab)) return;   // at an end
            hw.Characters = moved;
            hw.FollowGameOrder = false;
            UiBuild.FocusAfterRebuild($"hw-order:{c.Name}");
            Changed();
            Dispatcher.BeginInvoke(FillHomework);
        };
        Point? down = null;   // a drag starts once the mouse moves: a click on ≡ changes nothing
        handle.MouseLeftButtonDown += (_, e) => { e.Handled = true; down = e.GetPosition(handle); handle.CaptureMouse(); };   // handled: the button would take the capture
        handle.MouseLeftButtonUp += (_, _) => { down = null; handle.ReleaseMouseCapture(); };
        handle.MouseMove += (_, e) =>
        {
            if (down is not { } from || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var moved = e.GetPosition(handle) - from;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            handle.ReleaseMouseCapture();
            DragDrop.DoDragDrop(handle, new DataObject(CharacterDrag, c.Name), DragDropEffects.Move);
        };
        card.PreviewDragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(CharacterDrag) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        card.PreviewDrop += (_, e) =>
        {
            if (e.Data.GetData(CharacterDrag) is not string name) return;
            e.Handled = true;
            if (name == c.Name) return;   // dropped back on itself
            var hw = _s.Homework;
            hw.Characters = CardOrder.Move(CardOrder.TabOrder(hw.Characters, hw.Order, hw.FollowGameOrder), name, c.Name);
            hw.FollowGameOrder = false;
            Changed();
            Dispatcher.BeginInvoke(FillHomework);   // after the drag ends: the page is rebuilt under it
        };
        return keys;
    }

    /// <summary>A pick: its raid and difficulty, 클리어, then 빼기 (✕).</summary>
    UIElement PickRow(HomeworkService hw, HomeworkCharacter c, int index, IReadOnlyList<HomeworkClear> clears)
    {
        var pick = c.Picks[index];
        var raid = hw.Raids.FirstOrDefault(r => r.Id == pick.Raid);
        var raids = Combo();
        var all = hw.Raids;
        raids.ItemsSource = all.Select(r => r.Name).ToList();
        raids.SelectedIndex = all.ToList().FindIndex(r => r.Id == pick.Raid);
        raids.Width = PickRaidWidth;
        void Set(HomeworkPick next) { c.Picks = [.. c.Picks.Select((x, i) => i == index ? next : x)]; Changed(); FillHomework(); }
        raids.SelectionChanged += (_, _) =>
        {
            if (raids.SelectedIndex < 0 || all[raids.SelectedIndex] is not { } r || r.Id == pick.Raid) return;
            Set(new HomeworkPick(r.Id, r.Levels.Any(l => l.Difficulty == pick.Difficulty) ? pick.Difficulty : r.Levels[^1].Difficulty));
        };
        // an X at the row's end, after 확인필요 (user, 2026-11-10); its name still says 빼기
        var remove = new Button { Content = "✕", Margin = new(8, 0, 0, 0), ToolTip = "빼기" };
        System.Windows.Automation.AutomationProperties.SetName(remove, "빼기");
        remove.Click += (_, _) => { c.Picks = [.. c.Picks.Where((_, i) => i != index)]; Changed(); FillHomework(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(16, 2, 0, 2) };
        // logo, raid, difficulty (user, 2026-11-09); a fixed slot for the logo so a raid without one still lines up
        var logo = new ContentControl { Content = RaidBadgeView.Of(raid?.Name, null), Width = PickLogoWidth, VerticalAlignment = VerticalAlignment.Center };
        foreach (var e in new UIElement[] { logo, raids, LevelPicker(raid, pick.Difficulty, d => Set(pick with { Difficulty = d })) }) row.Children.Add(e);
        if (hw.TagOf(c, pick, clears) is { } tag) row.Children.Add(StateButtons(hw, c, pick, tag));
        row.Children.Add(remove);
        return row;
    }

    const double PickLogoWidth = 28, PickBadgeWidth = 110, PickRaidWidth = 200;   // fixed columns: every pick's 클리어 and ✕ line up (user, 2026-11-09)

    /// <summary>The raid's logo and difficulty capsule; a click lists its difficulties as capsules to swap to (user, 2026-11-09:
    /// it replaces the 난이도 box, which said the same).</summary>
    FrameworkElement LevelPicker(HomeworkRaid? raid, string difficulty, Action<string> pick)
    {
        // The theme's button frame and a ▾: it reads as a box to open, not a plain tag (user, 2026-11-10).
        var face = new DockPanel();
        var arrow = Muted(new TextBlock { Text = "▾", Margin = new(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(arrow, Dock.Right);
        face.Children.Add(arrow);
        var badge = RaidBadgeView.Of(null, difficulty);
        badge.HorizontalAlignment = HorizontalAlignment.Center;   // every pick's capsule on one centre line (user, 2026-11-10)
        face.Children.Add(badge);
        var button = new Button
        {
            Content = face, Width = PickBadgeWidth, Margin = new(8, 0, 0, 0), Padding = new(6, 2, 6, 2),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "눌러서 난이도를 바꿉니다.",
        };
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var level in raid?.Levels ?? [])
        {
            var item = new MenuItem { Header = RaidBadgeView.Of(null, level.Difficulty), IsChecked = level.Difficulty == difficulty };
            item.Click += (_, _) => { if (level.Difficulty != difficulty) pick(level.Difficulty); };
            menu.Items.Add(item);
        }
        // A click on the box while it is open folds it (user, 2026-11-10): the press closes the menu just before the box sees
        // it, so a press that lands while the menu is open or within FoldWindowMs of its closing marks that click as the fold.
        // Timed against the press, not the release, so a long hold folds too. Keyboard opens never press, so never fold.
        bool fold = false;
        long closedAt = long.MinValue / 2;
        menu.Closed += (_, _) => closedAt = Environment.TickCount64;
        button.PreviewMouseLeftButtonDown += (_, _) => fold = menu.IsOpen || Environment.TickCount64 - closedAt < FoldWindowMs;
        button.MouseLeave += (_, _) => fold = false;   // released elsewhere: no click comes to clear it
        button.LostMouseCapture += (_, _) => { if (!button.IsPressed) fold = false; };   // Alt-Tab mid-press, the same
        button.Click += (_, _) =>
        {
            if (fold) { fold = false; return; }
            menu.IsOpen = menu.Items.Count > 0;
        };
        return button;
    }

    /// <summary>A press this soon after the difficulty menu closed is the press that closed it.</summary>
    const long FoldWindowMs = 150;

    /// <summary>[미완 | 클리어 | 확인필요] for a raid, the part for this week lit: 클리어 by a saved fight, by hand or by the fallback;
    /// 확인필요 where the fallback can't tell (a ! on the tag). A click sets it; the week's reset turns it off.</summary>
    static UIElement StateButtons(HomeworkService hw, HomeworkCharacter c, HomeworkPick pick, HomeworkTag? tag)
    {
        var lit = Core.Domain.Homework.PartOf(tag);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8, 0, 0, 0) };
        foreach (var part in Enum.GetValues<HomeworkPart>())
        {
            var (back, ink) = part switch { HomeworkPart.클리어 => (ClearedBrush, Brushes.White), HomeworkPart.확인필요 => (UnsureBrush, Brushes.White), _ => (UndoneBrush, Brushes.Black) };
            var button = new Button
            {
                Content = part.ToString(), MinWidth = 52,
                ToolTip = part switch
                {
                    HomeworkPart.미완 => "이번 주 클리어하지 않았습니다.",
                    HomeworkPart.클리어 => "클리어했습니다. 앱 없이 깼다면 누르세요. 수요일 오전 6시에 풀립니다.",
                    _ => "클리어로 보이지만 확실하지 않습니다. 맞으면 클리어를, 아니면 미완을 누르세요.",
                },
            };
            if (part == lit) { Fill(button, back.Color, ink); button.FontWeight = FontWeights.Bold; }
            else button.Opacity = 0.75;
            button.Click += (_, _) => hw.SetState(c, pick, part);
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>A tag as the grid shows it: its colour, crossed out when cleared, ↓ / ↑ when cleared below / above the pick.</summary>
    static Border TagChip(HomeworkTag tag)
    {
        var ink = tag.Color switch
        {
            HomeworkColor.Orange => Color.FromRgb(0xFF, 0x9A, 0x3C),
            HomeworkColor.Purple => Color.FromRgb(0xC7, 0x7D, 0xFF),
            _ => Color.FromRgb(0xF2, 0xF2, 0xF2),
        };
        var text = new TextBlock
        {
            Text = tag.Text + tag.Arrow switch { < 0 => "↓", > 0 => "↑", _ => "" } + (tag.Unsure ? "!" : ""), Foreground = new SolidColorBrush(ink), FontWeight = FontWeights.Bold,
            TextDecorations = !tag.Done ? null : tag.Unsure ? DottedCross(ink) : TextDecorations.Strikethrough,
        };
        return new Border
        {
            Child = text, Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x14, 0x14, 0x18)), CornerRadius = new(3), Padding = new(5, 1, 5, 1),
            Margin = new(0, 0, 4, 0), Opacity = tag.Done ? 0.55 : 1, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    static TextDecorationCollection DottedCross(Color ink) =>
        [new TextDecoration(TextDecorationLocation.Strikethrough, new Pen(new SolidColorBrush(ink), 1.5) { DashStyle = DashStyles.Dot }, 0, TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended)];

    /// <summary>An added raid's tag: no fight clears it, so a click crosses it out for this week, and another takes that back.</summary>
    static System.Windows.Controls.Primitives.ToggleButton ClickableChip(HomeworkService hw, HomeworkCharacter c, HomeworkPick pick, HomeworkTag tag)
    {
        // A toggle button drawn as the chip: Tab and Space work, Narrator says the raid and whether it is cleared.
        var chip = UiBuild.Bare<System.Windows.Controls.Primitives.ToggleButton>(TagChip(tag), $"{c.Name} {pick.Raid} {pick.Difficulty} 클리어", $"hw-clear:{c.Name}:{pick.Raid}:{pick.Difficulty}");
        (chip.IsChecked, chip.VerticalAlignment) = (tag.Done, VerticalAlignment.Center);
        chip.ToolTip = tag.Done ? "눌러서 클리어 취소" : "눌러서 클리어 표시 (수요일 오전 6시에 풀립니다)";
        chip.Click += (_, _) => hw.ToggleClear(c, pick);
        return chip;
    }

    /// <summary>Raids the user added (missing or time-limited ones): the list, and the form for a new one, lowest difficulty first.</summary>
    UIElement CustomRaids(HomeworkService hw)
    {
        var panel = new StackPanel { Margin = new(0, 12, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "직접 추가한 레이드", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 4) });
        panel.Children.Add(Note("목록에 없거나 기간 한정인 레이드(종막 EX 같은)를 추가합니다. 전투 기록으로는 지워지지 않으니 위의 태그를 눌러 클리어를 표시하세요. 자동 3개와 따로 남고, 합쳐서 5개까지입니다."));
        foreach (var raid in _s.Homework.CustomRaids)
        {
            var remove = new Button { Content = "삭제", Margin = new(8, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"{Josa.Object(raid.Name)} 지우고 모든 캐릭터에서 뺄까요?", AreaHomework, MessageBoxButton.YesNo) == MessageBoxResult.Yes) hw.RemoveRaid(raid.Id);
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Muted(new TextBlock { Text = $"{string.Join(", ", raid.Levels.Select(l => l.Tag))} · {(raid.Auto ? "자동" : "수동")}", VerticalAlignment = VerticalAlignment.Center }));
            row.Children.Add(remove);
            panel.Children.Add(Row(raid.Name, row));
        }
        var name = new TextBox { MinWidth = 160, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(Row("레이드 이름", name));
        var rows = new List<(TextBox Difficulty, TextBox Tag, ComboBox Color)>();
        for (int i = 1; i <= MaxCustomLevels; i++)
        {
            var (difficulty, tag, color) = (new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "난이도 이름 (예: 하드)" },
                new TextBox { Width = 70, Margin = new(6, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "태그 (예: EX하). 비우면 난이도 이름을 씁니다." }, Combo());
            color.ItemsSource = new[] { "흰색 (노말)", "주황 (하드)", "보라 (나메)" };
            color.SelectedIndex = i - 1;
            rows.Add((difficulty, tag, color));
            panel.Children.Add(Row(i == 1 ? "난이도 · 태그 · 색 (낮은 것부터)" : "", new StackPanel { Orientation = Orientation.Horizontal, Children = { difficulty, tag, color } }));
        }
        var mode = Combo();
        mode.ItemsSource = new[] { "수동: 캐릭터마다 직접 고르기", "자동: 모든 캐릭터에 넣기 (새 캐릭터도, 5개가 찬 캐릭터는 빼고)" };
        mode.SelectedIndex = 0;
        panel.Children.Add(Row("캐릭터에 넣기", mode));
        var add = new Button { Content = "레이드 추가", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            var levels = rows.Select(r => (r.Difficulty.Text, r.Tag.Text, (HomeworkColor)r.Color.SelectedIndex));
            if (!hw.AddRaid(name.Text, levels, mode.SelectedIndex == 1)) MessageBox.Show(this, "레이드 이름과 난이도를 하나 이상 적어 주세요.", AreaHomework);
        };
        panel.Children.Add(Row("", add));
        return panel;
    }

    UIElement AddCharacterRow(HomeworkService hw)
    {
        var name = new TextBox { MinWidth = 160, VerticalContentAlignment = VerticalAlignment.Center };
        var add = new Button { Content = "추가", Margin = new(8, 0, 0, 0) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, add } };
        add.Click += (_, _) =>
        {
            if (!hw.AddByHand(name.Text)) MessageBox.Show(this, "이름을 확인해 주세요(이미 있거나, 글자·숫자가 아닌 이름).", AreaHomework);
            else FillHomework();
        };
        return Row("캐릭터 추가", row);
    }

    UIElement HiddenRow(IReadOnlyList<HomeworkCharacter> hidden)
    {
        var row = new WrapPanel();
        foreach (var c in hidden)
        {
            var show = new Button { Content = $"{c.Name} 보이기", Margin = new(0, 0, 6, 0) };
            show.Click += (_, _) => { c.Hidden = false; Changed(); FillHomework(); };
            row.Children.Add(show);
        }
        return Row("숨긴 캐릭터", row);
    }

    /// <summary>The tags' names per raid and difficulty: empty is the default (shown on hover).</summary>
    UIElement TagNames(HomeworkService hw)
    {
        var panel = new StackPanel { Margin = new(0, 12, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "표시 이름", FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 4) });
        foreach (var raid in hw.Raids)
        {
            var row = new WrapPanel();
            foreach (var level in raid.Levels)
            {
                string key = Core.Domain.Homework.TagKey(raid.Id, level.Difficulty);
                var box = new TextBox { Width = 70, Text = _s.Homework.Tags.GetValueOrDefault(key, ""), Margin = new(0, 0, 4, 0), ToolTip = $"{RaidBadge.Label(level.Difficulty)} (기본: {level.Tag})" };
                box.LostFocus += (_, _) =>
                {
                    string v = box.Text.Trim();
                    if (v.Length == 0) _s.Homework.Tags.Remove(key); else _s.Homework.Tags[key] = v;
                    Changed();
                };
                row.Children.Add(Muted(new TextBlock { Text = RaidBadge.Label(level.Difficulty), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 4, 0) }));
                row.Children.Add(box);
            }
            panel.Children.Add(Row(raid.Name, row));
        }
        panel.Children.Add(Note("비워 두면 기본 이름(마우스를 올리면 보임)을 씁니다."));
        return panel;
    }
}
