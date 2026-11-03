using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 숙제 (homework.plan.md): who is being played, each character's picks (up to 5 raids at a difficulty) with this
/// week's tags, characters added by hand or hidden, and the tags' names.</summary>
public partial class SettingsWindow
{
    const string AreaHomework = "숙제";
    const string HomeworkLead = "캐릭터마다 이번 주(수요일 오전 6시부터) 클리어한 레이드를 봅니다. 캐릭터는 ESC 게임 메뉴와 '다른 캐릭터 바로 접속' 창에서 읽고, " +
        "레이드는 전투 기록이 마지막 관문을 클리어로 저장하면 지웁니다. 같은 레이드는 난이도와 상관없이 한 주에 한 번이라, 고른 것보다 낮은 난이도로 깨면 ↓, 높으면 ↑를 붙이고 고른 난이도를 올립니다.";
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
        if (Homework is not { } hw) { p.Children.Add(Note("숙제는 게임과 함께 켜졌을 때 씁니다.")); return; }
        p.Children.Add(Note(hw.Current is { } now ? $"지금 캐릭터: {now}" : "지금 캐릭터: 캐릭터 불명 (ESC를 눌러 게임 메뉴를 열면 읽습니다)"));
        p.Children.Add(Row("", Check("다른 캐릭터 접속 창에 숙제 표시", _s.Homework.ShowOnGrid, v => _s.Homework.ShowOnGrid = v, Changed,
            "다른 캐릭터 바로 접속 창의 캐릭터 칸 아래에 고른 레이드를 작게 보여 줍니다. 클리어한 것은 줄을 긋습니다.")));
        var clears = hw.Clears();
        var shown = _s.Homework.Characters.Where(c => !c.Hidden).ToList();
        p.Children.Add(Note(shown.Count == 0
            ? "아직 읽은 캐릭터가 없습니다. 게임에서 ESC → 다른 캐릭터 바로 접속을 열고 캐릭터 칸을 하나씩 누르세요(오른쪽 위에 큰 글씨로 뜬 이름을 읽습니다). 아래에서 직접 추가해도 됩니다."
            : "목록에 없는 캐릭터는 다른 캐릭터 바로 접속 창에서 그 칸을 한 번 누르면 들어옵니다(칸의 작은 글씨는 잘못 읽힐 때가 많아 큰 글씨 이름만 씁니다)."));
        foreach (var c in shown) p.Children.Add(CharacterCard(hw, c, clears));
        p.Children.Add(AddCharacterRow(hw));
        if (_s.Homework.Characters.Where(c => c.Hidden).ToList() is { Count: > 0 } hidden) p.Children.Add(HiddenRow(hidden));
        p.Children.Add(TagNames(hw));
    }

    UIElement CharacterCard(HomeworkService hw, HomeworkCharacter c, IReadOnlyList<HomeworkClear> clears)
    {
        var card = new StackPanel { Margin = new(0, 6, 0, 10) };
        var head = new WrapPanel();
        head.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) });
        if (c.ClassName is not null)
            head.Children.Add(new TextBlock { Text = RecordCharacter.Label(c.ClassName, c.Subclass), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        if (c.Name == hw.Current) head.Children.Add(new TextBlock { Text = "· 지금 플레이 중", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        foreach (var tag in hw.TagsOf(c, clears)) head.Children.Add(TagChip(tag));
        var hide = new Button { Content = "숨기기", Margin = new(12, 0, 0, 0) };
        hide.Click += (_, _) => { c.Hidden = true; Changed(); FillHomework(); };
        head.Children.Add(hide);
        card.Children.Add(head);
        for (int i = 0; i < c.Picks.Count; i++) card.Children.Add(PickRow(hw, c, i));
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

    /// <summary>A pick: its raid and difficulty, and 빼기.</summary>
    UIElement PickRow(HomeworkService hw, HomeworkCharacter c, int index)
    {
        var pick = c.Picks[index];
        var raid = hw.Raids.FirstOrDefault(r => r.Id == pick.Raid);
        var raids = Combo();
        raids.ItemsSource = hw.Raids.Select(r => r.Name).ToList();
        raids.SelectedIndex = raid is null ? -1 : hw.Raids.ToList().IndexOf(raid);
        var levels = Combo();
        levels.ItemsSource = raid?.Levels.Select(l => l.Difficulty).ToList() ?? [];
        levels.SelectedItem = pick.Difficulty;
        void Set(HomeworkPick next) { c.Picks = [.. c.Picks.Select((x, i) => i == index ? next : x)]; Changed(); FillHomework(); }
        raids.SelectionChanged += (_, _) =>
        {
            if (raids.SelectedIndex < 0 || hw.Raids[raids.SelectedIndex] is not { } r || r.Id == pick.Raid) return;
            Set(new HomeworkPick(r.Id, r.Levels.Any(l => l.Difficulty == pick.Difficulty) ? pick.Difficulty : r.Levels[^1].Difficulty));
        };
        levels.SelectionChanged += (_, _) => { if (levels.SelectedItem is string d && d != pick.Difficulty) Set(pick with { Difficulty = d }); };
        var remove = new Button { Content = "빼기", Margin = new(8, 0, 0, 0) };
        remove.Click += (_, _) => { c.Picks = [.. c.Picks.Where((_, i) => i != index)]; Changed(); FillHomework(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(16, 2, 0, 2) };
        foreach (var e in new UIElement[] { raids, levels, remove }) row.Children.Add(e);
        levels.Margin = new(8, 0, 0, 0);
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
            Text = tag.Text + tag.Arrow switch { < 0 => "↓", > 0 => "↑", _ => "" }, Foreground = new SolidColorBrush(ink), FontWeight = FontWeights.Bold,
            TextDecorations = tag.Done ? TextDecorations.Strikethrough : null,
        };
        return new Border
        {
            Child = text, Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x14, 0x14, 0x18)), CornerRadius = new(3), Padding = new(5, 1, 5, 1),
            Margin = new(0, 0, 4, 0), Opacity = tag.Done ? 0.55 : 1, VerticalAlignment = VerticalAlignment.Center,
        };
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
                var box = new TextBox { Width = 70, Text = _s.Homework.Tags.GetValueOrDefault(key, ""), Margin = new(0, 0, 4, 0), ToolTip = $"{level.Difficulty} (기본: {level.Tag})" };
                box.LostFocus += (_, _) =>
                {
                    string v = box.Text.Trim();
                    if (v.Length == 0) _s.Homework.Tags.Remove(key); else _s.Homework.Tags[key] = v;
                    Changed();
                };
                row.Children.Add(new TextBlock { Text = level.Difficulty, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 4, 0) });
                row.Children.Add(box);
            }
            panel.Children.Add(Row(raid.Name, row));
        }
        panel.Children.Add(Note("비워 두면 기본 이름(마우스를 올리면 보임)을 씁니다."));
        return panel;
    }
}
