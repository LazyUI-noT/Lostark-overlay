using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    UIElement RecordEditor(string file, FightRecord r, IReadOnlyList<(string File, FightRecord Record)> all)
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
        var difficulty = Pick(RaidGates.SelectMany(g => g.HpByDifficulty?.Keys ?? []).Concat(books.Select(b => b.Difficulty))
            .Concat(all.Select(x => x.Record.Difficulty)).OfType<string>().Where(n => n.Trim().Length > 0).Distinct(), r.Difficulty, editable: true);
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
        var who = Pick(_s.Homework.Characters.Select(c => c.Name).Concat(all.Select(x => x.Record.Character)).OfType<string>()
            .Where(n => n.Trim().Length > 0).Distinct(), r.Character, editable: true);
        // Picking a character sets its saved class · 직업각인; it can still be changed below.
        who.SelectionChanged += (_, _) =>
        {
            if (RecordCharacter.SavedClass(who.SelectedItem as string, _s.Homework.Characters, all.Select(x => x.Record)) is { } saved
                && characters.FindIndex(x => RecordCharacter.Label(x.Class.Name, x.Sub) == saved) is >= 0 and var at)
                character.SelectedIndex = at;
        };

        var apply = new Button { Content = "적용", Margin = new(0, 6, 8, 0) };
        var cancel = new Button { Content = "취소", Margin = new(0, 6, 0, 0) };
        cancel.Click += (_, _) => { _editing = null; FillRecords(); };
        apply.Click += (_, _) =>
        {
            static string? Text(ComboBox c) => c.Text.Trim() is { Length: > 0 } t ? t : null;
            var edited = r with
            {
                Gate = Text(dungeon), Difficulty = Text(difficulty),
                Cleared = result.SelectedItem switch { Cleared => true, Failed => false, _ => null },
                Character = Text(who) is { } n && n != RecordCharacter.Unknown ? n : null,
            };
            if (character.SelectedIndex >= 0 && characters[character.SelectedIndex] is var (cls, sub))
                edited = edited with
                {
                    MyClass = cls.Id, MyClassName = cls.Name, MySubclass = sub,
                    Members = [.. r.Members.Select(m => m.Name == r.Me ? m with { ClassId = cls.Id } : m)],
                };
            var picks = unsure.Where(u => u.Box.SelectedIndex >= 0 && u.Box.SelectedIndex < u.Options.Count)
                .ToDictionary(u => u.Slot, u => u.Options[u.Box.SelectedIndex]);
            edited = FightSkills.Confirm(r, edited, picks);
            if (store != TestRecords && BossBook.Taught(r, edited) is var (hp, entry))
            {
                _s.BossBook[hp] = entry;
                Changed();
            }
            // 숙제 follows an edit only when the user says so (user, 2026-11-04): a fight fixed by hand may not be this week's clear.
            bool homework = store == Records && Homework is { } hw && hw.Affects(r, edited)
                && MessageBox.Show(this, "이 수정으로 이번 주 숙제(클리어한 레이드)가 바뀝니다. 숙제도 바꿀까요?", AreaRecords, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
            _editing = null;
            // A footage fight given a real character and class leaves [Test] for that character's fights (user, 2026-11-05).
            if (store is { } test && test == TestRecords && Records is { } real && edited.Character is not null && edited.MyClassName != RecordCharacter.TestClass)
                RecordAction(() =>
                {
                    var moved = real.Save(edited, _s.RecordKeep);
                    try { test.Delete(name); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { real.Delete(moved); throw; }   // not in both lists
                });
            else RecordAction(() => store?.Edit(name, edited, _s.RecordKeep));
            if (homework) Homework!.Edited(r, edited);
        };

        var panel = new StackPanel { Margin = new(16, 2, 0, 10) };
        panel.Children.Add(Row("던전", dungeon));
        panel.Children.Add(Row("난이도", difficulty));
        panel.Children.Add(Row("결과", result));
        panel.Children.Add(Row("캐릭터", who));
        panel.Children.Add(Row(proposal ? "클래스 · 직업각인 (추천)" : "클래스 · 직업각인", character));
        foreach (var u in unsure) panel.Children.Add(Row($"스킬 {SlotKeyName(u.Slot)}", u.Box));
        if (proposal) panel.Children.Add(Note("추천은 스킬 아이콘으로 찾은 클래스입니다. 적용하면 그대로 확정되고, 다른 클래스를 고르면 찾은 스킬 이름은 지워집니다."));
        panel.Children.Add(Note("던전과 난이도는 목록에서 고르거나 직접 씁니다. 바꾸면 이 보스 HP를 기억해서 다음 전투부터 같은 이름으로 저장합니다. " +
            "캐릭터를 바꾸면 그 캐릭터의 기록으로 옮깁니다. 그쪽이 가득 찼으면 가장 오래된 자동 기록이 지워집니다. 비우면 캐릭터 불명이 됩니다. 캐릭터를 고르면 저장된 클래스 · 직업각인으로 맞추고, 그 뒤에 바꿀 수 있습니다. 클래스는 스킬 아이콘에 씁니다."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        return panel;
    }
}
