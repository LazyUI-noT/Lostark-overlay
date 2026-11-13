using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록 편집 (user, 2026-11-03): a list row's 편집 opens its dungeon, difficulty, result and class · 직업각인 under
/// it, written once by 적용. Another class moves the fight to that character's list; a dungeon or difficulty set by hand
/// teaches the boss book its HP (not for 테스트 모드 fights).</summary>
public partial class SettingsWindow
{
    string? _editing;   // the row whose editor is open

    const string Cleared = "클리어", Failed = "실패";

    /// <summary>The fight's editor below its row, and its 적용 · 취소 for the row.</summary>
    (UIElement Panel, UIElement[] Buttons) RecordEditor(string file, FightRecord r, IReadOnlyList<(string File, FightRecord Record)> all)
    {
        var (store, name) = StoreOf(file);
        // A fixed list (결과, 클래스) is picked by item: Text doesn't select one in a box that can't be typed in.
        static ComboBox Pick(IEnumerable<string> items, string? text, bool editable)
        {
            var list = items.ToList();
            var box = new ComboBox { ItemsSource = list, IsEditable = editable, MinWidth = 220, Margin = new(0, 2, 0, 2) };
            if (editable) box.Text = text ?? "";
            else box.SelectedIndex = list.IndexOf(text ?? "");
            return box;
        }
        var books = _s.BossBook.Values.ToList();
        var dungeon = Pick(RaidGates.Select(RaidData.Label).Concat(books.Select(b => b.Name)).Concat(all.Select(x => x.Record.Gate))
            .OfType<string>().Where(n => n.Trim().Length > 0).Distinct(), r.Gate, editable: true);
        // A known raid's own difficulties only (user, 2026-11-11: 벨가 나메 · 하드 · 노말 · 매칭), else every one seen.
        IEnumerable<string> DifficultiesOf(string? gate) => RaidOrder.Difficulties(gate, RaidGates, Homework?.Raids ?? []) ??
            RaidGates.SelectMany(g => g.HpByDifficulty?.Keys ?? []).Concat(books.Select(b => b.Difficulty))
                .Concat(all.Select(x => x.Record.Difficulty)).OfType<string>().Where(n => n.Trim().Length > 0).Distinct();
        var difficulty = Pick(DifficultiesOf(r.Gate), r.Difficulty, editable: true);
        dungeon.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            string kept = difficulty.Text;   // a new list keeps what is typed
            difficulty.ItemsSource = DifficultiesOf(dungeon.Text.Trim()).ToList();
            difficulty.Text = kept;
        }));
        var result = Pick([Cleared, Failed, RecordCharacter.Unknown], r.Cleared switch { true => Cleared, false => Failed, _ => RecordCharacter.Unknown }, editable: false);
        var characters = _chars.Classes.SelectMany(c => c.Subclasses.Keys.Select(s => (Class: c, Sub: s))).ToList();
        // A [Test] fight's class proposed from its skill icons (FightSkills): preselected, the user confirms it with 적용.
        bool proposal = r.MyClassName == RecordCharacter.TestClass && r.ProposedClass is not null;
        var character = Pick(characters.Select(x => RecordCharacter.Label(x.Class.Name, x.Sub)), proposal ? r.ProposedClass : RecordCharacter.ClassOf(r), editable: false);
        if (proposal && character.SelectedIndex < 0)   // a class alone (no 직업각인 found): its first 직업각인, to be checked
            character.SelectedIndex = characters.FindIndex(x => x.Class.Name == r.ProposedClass);
        // Slots left unsure: pick from their candidates, or leave 모름.
        const string NotKnown = "모름";
        var unsure = (r.Unsure ?? new Dictionary<SlotId, IReadOnlyList<RecordSkill>>()).OrderBy(u => u.Key)
            .Select(u => (Slot: u.Key, Options: u.Value, Box: Pick(u.Value.Select(s => s.Name).Append(NotKnown), NotKnown, editable: false))).ToList();
        // 캐릭터: a plain text box, the known characters starting with what is typed in a dropdown under it (user, 2026-11-09). An
        // editable ComboBox rewrote its text mid-IME composition (the next 자모 lost) and, filtered, stopped taking typing. The
        // list never takes focus, so typing goes on in the box.
        var known = _s.Homework.Characters.Select(c => c.Name).Concat(all.Select(x => x.Record.Character)).OfType<string>()
            .Select(n => n.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        var who = new TextBox { Text = r.Character ?? "", MinWidth = 220, Margin = new(0, 2, 0, 2) };
        // Picking a name: its TextChanged follows the class and closes the list (the name now matches in full).
        TypeAhead.Attach(who, typed => typed.Trim() is var t ? [.. known.Where(n => n != t && n.StartsWith(t, StringComparison.Ordinal)).Take(MaxSuggested)] : [],
            n => { who.Text = n; who.CaretIndex = n.Length; });
        // A known character, picked or typed in full, sets its saved class · 직업각인; it can still be changed below.
        void FollowClass(string name)
        {
            if (RecordCharacter.SavedClass(name, _s.Homework.Characters, all.Select(x => x.Record)) is { } saved
                && characters.FindIndex(x => RecordCharacter.Label(x.Class.Name, x.Sub) == saved) is >= 0 and var at)
                character.SelectedIndex = at;
        }
        who.TextChanged += (_, _) => { if (known.Contains(who.Text.Trim())) FollowClass(who.Text.Trim()); };

        static string? Text(ComboBox c) => c.Text.Trim() is { Length: > 0 } t ? t : null;
        // What 적용 stores: the fight as the boxes read now. The unapplied check compares it with the fight as stored.
        FightRecord Draft()
        {
            var edited = r with
            {
                Gate = Text(dungeon), Difficulty = Text(difficulty),
                Cleared = result.SelectedItem switch { Cleared => true, Failed => false, _ => null },
                Character = who.Text.Trim() is { Length: > 0 } n && n != RecordCharacter.Unknown ? n : null,
            };
            if (character.SelectedIndex >= 0 && characters[character.SelectedIndex] is var (cls, sub))
                edited = edited with
                {
                    MyClass = cls.Id, MyClassName = cls.Name, MySubclass = sub,
                    Members = [.. r.Members.Select(m => m.Name == r.Me ? m with { ClassId = cls.Id } : m)],
                };
            var picks = unsure.Where(u => u.Box.SelectedIndex >= 0 && u.Box.SelectedIndex < u.Options.Count)
                .ToDictionary(u => u.Slot, u => u.Options[u.Box.SelectedIndex]);
            return RecordPower.Edited(r, FightSkills.Confirm(r, edited, picks));
        }

        // 적용 and 취소 sit in the fight's row, where 편집 was (user, 2026-11-09).
        var apply = new Button { Content = "적용", Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var cancel = new Button { Content = "취소", Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        void ShowUnapplied() => UiBuild.MarkUnapplied(apply, RecordEdits.Unapplied(r, Draft()));
        cancel.Click += (_, _) => { _editing = null; FillRecords(); };
        apply.Click += (_, _) =>
        {
            var edited = Draft();
            // A footage fight given a real character and class leaves [Test] for that character's fights (user, 2026-11-05).
            bool toReal = store is { } t && t == TestRecords && Records is not null && edited.Character is not null && edited.MyClassName != RecordCharacter.TestClass;
            if ((toReal ? Records : store)?.WouldBin(toReal ? "" : name, edited, _s.RecordKeep) == true
                && !Warn(WarningKind.PushToBin, "이렇게 고치면 그 캐릭터의 최근 기록 수를 넘어, 보관하지 않은 가장 오래된 기록(이 기록일 수도 있습니다)이 휴지통으로 갑니다."))
                return;
            if (store != TestRecords && BossBook.Taught(r, edited) is var (hp, entry))
            {
                _s.BossBook[hp] = entry;
                Changed();
            }
            // 숙제 follows an edit only when the user says so (user, 2026-11-04): a fight fixed by hand may not be this week's clear.
            bool homework = store == Records && Homework is { } hw && hw.Affects(r, edited)
                && MessageBox.Show(this, "이 수정으로 이번 주 숙제(클리어한 레이드)가 바뀝니다. 숙제도 바꿀까요?", AreaRecords, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            _editing = null;
            if (toReal && store is { } test && Records is { } real)
                RecordAction(() =>
                {
                    var moved = real.Save(edited, _s.RecordKeep);
                    try { test.Erase(name); }   // moved, not deleted: no copy in the 휴지통
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { real.Erase(moved); throw; }   // not in both lists
                });
            else RecordAction(() => store?.Edit(name, edited, _s.RecordKeep));
            if (homework) Homework!.Edited(r, edited);
        };

        var panel = new StackPanel { Margin = new(16, 2, 0, 10) };
        panel.Children.Add(Row("콘텐츠", dungeon));
        panel.Children.Add(Row("난이도", difficulty));
        panel.Children.Add(Row("결과", result));
        panel.Children.Add(Row("캐릭터", who));
        panel.Children.Add(Row(proposal ? "클래스 · 직업각인 (추천)" : "클래스 · 직업각인", character));
        foreach (var u in unsure) panel.Children.Add(Row($"스킬 {SlotKeyName(u.Slot)}", u.Box));
        if (proposal) panel.Children.Add(Note("추천은 스킬 아이콘으로 찾은 클래스입니다. 적용하면 그대로 확정되고, 다른 클래스를 고르면 찾은 스킬 이름은 지워집니다."));
        panel.Children.Add(Note("콘텐츠와 난이도는 목록에서 고르거나 직접 씁니다. 바꾸면 이 보스 HP를 기억해서 다음 전투부터 같은 이름으로 저장합니다. " +
            "캐릭터를 바꾸면 그 캐릭터의 기록으로 옮깁니다. 그쪽이 가득 찼으면 가장 오래된 자동 기록이 지워집니다. 비우면 캐릭터 불명이 됩니다. 캐릭터를 고르면 저장된 클래스 · 직업각인으로 맞추고, 그 뒤에 바꿀 수 있습니다. 클래스는 스킬 아이콘에 씁니다."));
        // 적용 lights up while a box differs from the fight as stored. Attached last, so the who → class handler above runs first.
        apply.Loaded += (_, _) => ShowUnapplied();   // the theme's colours resolve once it is in the page
        foreach (var box in new[] { dungeon, difficulty, result, character }.Concat(unsure.Select(u => u.Box)))
            box.SelectionChanged += (_, _) => ShowUnapplied();
        foreach (var box in new Control[] { dungeon, difficulty, who })   // typing changes the text, not a selection
            box.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => ShowUnapplied()));
        return (panel, [apply, cancel]);
    }

    const int MaxSuggested = 8;   // known characters offered under 캐릭터
}
