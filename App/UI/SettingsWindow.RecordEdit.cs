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
        var character = Pick(characters.Select(x => RecordCharacter.Label(x.Class.Name, x.Sub)), RecordCharacter.Of(r), editable: false);

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
            };
            if (character.SelectedIndex >= 0 && characters[character.SelectedIndex] is var (cls, sub))
                edited = edited with
                {
                    MyClass = cls.Id, MyClassName = cls.Name, MySubclass = sub,
                    Members = [.. r.Members.Select(m => m.Name == r.Me ? m with { ClassId = cls.Id } : m)],
                };
            if (store != TestRecords && BossBook.Taught(r, edited) is var (hp, entry))
            {
                _s.BossBook[hp] = entry;
                Changed();
            }
            _editing = null;
            RecordAction(() => store?.Edit(name, edited, _s.RecordKeep));
        };

        var panel = new StackPanel { Margin = new(16, 2, 0, 10) };
        panel.Children.Add(Row("던전", dungeon));
        panel.Children.Add(Row("난이도", difficulty));
        panel.Children.Add(Row("결과", result));
        panel.Children.Add(Row("클래스 · 직업각인", character));
        panel.Children.Add(Note("던전과 난이도는 목록에서 고르거나 직접 씁니다. 바꾸면 이 보스 HP를 기억해 다음 전투부터 같은 이름으로 저장합니다. " +
            "클래스를 바꾸면 그 클래스의 기록으로 옮기고, 가득 찼으면 그 클래스의 가장 오래된 자동 기록을 지웁니다."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        return panel;
    }
}
