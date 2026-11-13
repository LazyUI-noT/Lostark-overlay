using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>복사 (settings-copy.plan.md): one page on 내 오버레이, opened from the 전체 view, for both kinds of copy: 프로필 n → m,
/// and 직업각인 (or 기본) A → B or → 모든 직업각인, 나 or 파티원. What may be copied is <see cref="ClassPicker"/>'s and
/// <see cref="ClassProfiles.Copy"/>'s decision; this file only asks and shows. Both blocks are off in Test.</summary>
public partial class SettingsWindow
{
    const string AllClasses = "*";   // 복사 → 대상: every 직업각인 and the template
    const string CopyHelp = "프로필 복사는 고른 설정을 덮어쓰고, 바로 되돌릴 수 있습니다. 직업각인 복사는 고른 부분만 덮어씁니다. 크기는 파티원 설정에만 있습니다.";
    static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(5);

    readonly CardStack _secCopyLink = new();   // the 전체 view's way in
    CopyParts _copyParts = CopyParts.Color | CopyParts.Size | CopyParts.Layout;
    bool _copyFromMembers, _copyToMembers = true;
    string? _copyFrom;               // 가져올 설정: null follows 편집 중
    string _copyTo = AllClasses;
    string? _copyResult;

    /// <summary>The active profile's 설정 was overwritten by a copy: App puts the windows on its spots and sizes, as for a switch.</summary>
    public event Action? ProfileOverwritten;

    string CopyFrom => ClassPicker.Normalize(_copyFrom, ShownClass, PickerEntries);

    /// <summary>The 전체 view's 복사 card: a button to the page.</summary>
    void FillCopyLink()
    {
        var p = Refill(_secCopyLink, "복사", "프로필끼리, 또는 직업각인끼리 설정을 복사합니다.");
        var open = new Button { Content = "복사 열기…", HorizontalAlignment = HorizontalAlignment.Left, ToolTip = CopyHelp };
        open.Click += (_, _) => ShowCopy();
        p.Children.Add(open);
    }

    /// <summary>The page: block 1 (프로필) and block 2 (직업각인 / 기본), both disabled in Test.</summary>
    void FillCopyPage()
    {
        var p = Refill(_secCopy, "프로필 복사", CopyHelp);
        p.IsEnabled = !_inTestProfile;
        if (_inTestProfile) p.Children.Add(Note("테스트 모드에서는 복사할 수 없습니다."));
        FillProfileCopy(p);
        p.Children.Add(Heading("직업각인 설정 복사"));
        FillClassCopy(p);
    }

    static ComboBox ProfileCombo(IEnumerable<int> profiles, int value, Action<int> set)
    {
        var c = Combo();
        c.MinWidth = 90;
        c.ItemsSource = profiles.Select(i => new ComboBoxItem { Content = $"{i + 1}", Tag = i }).ToList();
        c.SelectedItem = c.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == value);
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem { Tag: int v } && v != value) set(v); };
        return c;
    }

    /// <summary>From → to → what (user, 2026-11-13): 가져올 설정, 대상, 복사할 것, then the button with its note beside it.</summary>
    void FillClassCopy(Panel p)
    {
        string from = CopyFrom;
        var fromPick = PickCombo(ClassPicker.FavoritesFirst(PickerEntries, _s.FavoriteClasses), from, v => _copyFrom = v, _s.FavoriteClasses);
        p.Children.Add(Row("가져올 설정", fromPick, PickSearch(fromPick, "가져올 설정"), Label("쪽"), SideCombo(_copyFromMembers, v => _copyFromMembers = v)));

        var toFavorites = ClassPicker.CopyFavorites(_s.FavoriteClasses, _s.Characters);
        var to = PickCombo(ClassPicker.FavoritesFirst(PickerEntries, toFavorites).Prepend(AllClasses), _copyTo, v => _copyTo = v, toFavorites);
        string? Target() => _copyTo == AllClasses ? null : _copyTo;
        var copy = CopyButton("복사",
            () => ClassPicker.CopyRefusal(_copyParts, from, _copyFromMembers, Target(), _copyToMembers, _chars.Classes),
            () => ClassPicker.CopyNeedsConfirm(Target()) ? ("정말 모든 직업각인에 복사", $"{from}>{_copyFromMembers}>{_copyToMembers}") : null,
            text => { _copyResult = text; Dispatcher.BeginInvoke(FillCopyPage); }, () => RunClassCopy(from, Target()));
        Themes.Primary(copy);
        p.Children.Add(Row("대상", to, PickSearch(to, "복사 대상"), Label("쪽"), SideCombo(_copyToMembers, v => _copyToMembers = v)));

        var parts = new List<UIElement>();
        foreach (var (part, text) in new[] { (CopyParts.Color, "색"), (CopyParts.Size, "크기"), (CopyParts.Layout, "배치"), (CopyParts.Display, "표시"), (CopyParts.Aden, "아덴"), (CopyParts.Effects, "효과"),
            (CopyParts.Progress, "쿨 진행 표시"), (CopyParts.Timer, "남은 쿨 (초)") })
            parts.Add(Check(text, _copyParts.HasFlag(part), v => _copyParts = v ? _copyParts | part : _copyParts & ~part, () => { }));
        p.Children.Add(Row("복사할 것", [.. parts]));

        copy.HorizontalAlignment = HorizontalAlignment.Left;
        p.Children.Add(copy);
        p.Children.Add(ConfirmNote("대상이 모든 직업각인이면 한 번 더 눌러야 복사합니다."));
        if (_copyResult is { } result) p.Children.Add(Note(result));
    }

    /// <summary>What a copy button's second click is for, right under it (it was in the footer, far from the buttons).</summary>
    static TextBlock ConfirmNote(string text) => new() { Text = text, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 0) };

    string RunClassCopy(string from, string? target)
    {
        var c = ClassProfiles.Of(_s, from);
        ClassProfile source = _copyFromMembers ? c.Members : c.Mine;
        // 모든 직업각인: 기본 and every 직업각인 (party members: each class once, as theirs are the class's).
        var targets = ClassPicker.CopyTargets(target, _chars.Classes, _copyToMembers)
            .Select(n => ClassProfiles.Edit(_s, n)).Select(t => _copyToMembers ? (ClassProfile)t.Members : t.Mine).ToList();
        ClassProfiles.Copy(_s, source, targets, _copyParts);
        Changed();
        bool membersSide = _copyFromMembers || _copyToMembers;
        return $"{targets.Count(t => t != source)}곳에 복사했습니다."
            + (membersSide && (_copyParts & (CopyParts.Progress | CopyParts.Timer)) != 0 ? " 쿨 진행 표시와 남은 쿨 (초)는 파티원 쪽에 복사되지 않습니다." : "");
    }

    /// <summary>A copy button. A click first asks <paramref name="refuse"/> (a note, nothing runs); when <paramref name="confirm"/>
    /// gives a label and a key, the first click only turns the button into that label for 5 s and the next with the same key
    /// runs <paramref name="run"/> (whose result is told by <paramref name="say"/>); no confirm: runs at once.</summary>
    Button CopyButton(string idle, Func<string?> refuse, Func<(string Armed, string Key)?> confirm, Action<string> say, Func<string> run)
    {
        var b = new Button { Content = idle };
        string? armedKey = null;
        var timer = new DispatcherTimer { Interval = ConfirmWindow };
        void Disarm() { timer.Stop(); armedKey = null; b.Content = idle; }
        timer.Tick += (_, _) => Disarm();
        b.Unloaded += (_, _) => Disarm();   // the page is rebuilt often: the timer goes with its button
        b.Click += (_, _) =>
        {
            if (refuse() is { } refusal) { Disarm(); say(refusal); return; }
            if (confirm() is { } c && armedKey != c.Key)
            {
                armedKey = c.Key;
                b.Content = c.Armed;
                timer.Stop();
                timer.Start();
                return;
            }
            Disarm();
            say(run());
        };
        return b;
    }

    /// <summary>복사 열기… (the 전체 view): the page in the inspector, on 내 오버레이.</summary>
    internal void ShowCopy()
    {
        if (_canvas is null) return;
        _inspected = new("Copy", null, null);
        if (_area.Name != AreaOverlay) Go(AreaOverlay);   // ShowArea fills it
        else FillCopyInspector();
    }

    void FillCopyInspector()
    {
        InspectorHead.Children.Clear();
        var back = new Button { Content = "← 전체", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 10), Padding = new(8, 1, 8, 2) };
        back.Click += (_, _) => { _canvas?.Select(null); ShowInspector(null); };
        InspectorHead.Children.Add(back);
        InspectorHead.Children.Add(new TextBlock { Text = "복사", FontSize = 18, FontWeight = FontWeights.SemiBold });
        Detach(_secCopy);
        Inspector.Children.Clear();
        Inspector.Children.Add(_secCopy);
        InspectorFoot.Child = null;   // the confirm notes sit under their buttons
    }
}
