using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>코어 빌드 in 설정 → 캐릭터, under the character picker: the active 직업각인's choice (Settings.Cores), which the engine reads
/// as a snapshot (CharacterService.CoreWatches, through Configure). Nothing here knows what an effect does; the data does.</summary>
public partial class SettingsWindow
{
    const string NoCoreEffects = "이 직업각인의 코어 효과는 아직 없습니다. 선택은 저장됩니다.";
    const string NoSlotChosen = "선택 안 함";

    /// <summary>What 코어 빌드 is, said under the 지원하는 직업각인 table whose 코어 column lists what each 직업각인's cores do.</summary>
    const string CoresExplained = "코어 빌드: 직업각인마다 고를 수 있는 선택 설정입니다. 기본은 꺼짐이고, 켜지 않으면 아무것도 달라지지 않습니다. " +
        "딜러는 해와 달, 서포터는 별의 레벨(1~3)을 고릅니다. 켜면 쿨타임을 초기화하는 스킬을 쓴 직후에 초기화되는 스킬의 쿨타임을 더 빨리 읽을 뿐이고, " +
        "코어 빌드를 화면에서 알아보지는 않으며 파티원에게 보내지도 않습니다. 선택은 직업각인마다 따로 저장됩니다.";

    /// <summary>The 코어 빌드 rows of the active 직업각인: 적용, the levels its role chooses, and for each skill an effect tracks the slot
    /// it sits in. Edits go to <see cref="Settings.SetCore"/> and the engine at once (<see cref="Changed"/>); the rows after the checkbox
    /// are disabled, not rebuilt, while it is off, so the table above keeps its NEW marks.</summary>
    void CoreRows(Panel p)
    {
        if (_chars.Active is not { Subclass.Length: > 0 } c) return;
        string key = Settings.CharacterKey(c.Class, c.Subclass);
        var choice = _s.CoreOf(key);
        var effects = _chars.ActiveCoreEffects;
        var gated = new List<UIElement>();
        void Edit(Func<CoreChoice, CoreChoice> change) { _s.SetCore(key, change(_s.CoreOf(key))); Changed(); }
        T Gate<T>(T row) where T : UIElement { row.IsEnabled = choice.Apply; gated.Add(row); return row; }

        p.Children.Add(SubHeading($"코어 빌드 ({c.Class} · {c.Subclass})", "고른 코어 레벨에 맞는 효과가 있으면, 그 스킬을 쓴 뒤 오버레이가 해당 칸의 쿨타임을 더 빨리 읽습니다. " +
            "효과는 아래 표의 코어 열에 있습니다. 효과가 없는 직업각인은 선택만 저장되고 아무것도 달라지지 않습니다. " +
            "내 오버레이에서 숨기고 파티에도 보내지 않는 칸은 앱이 읽지 않으니, 그 칸이 스킬 칸이거나 효과를 받는 칸이면 적용되지 않습니다."));
        p.Children.Add(Check("코어 빌드 적용", choice.Apply, v =>
        {
            Edit(x => x with { Apply = v });
            foreach (var row in gated) row.IsEnabled = v;
        }, () => { }, "끄면 아래 선택은 그대로 두고 아무것도 적용하지 않습니다."));

        var levels = new List<UIElement>();
        foreach (var core in CoreEffects.Allowed(_chars.Role))
        {
            levels.Add(Label(core.ToString()));
            levels.Add(LevelCombo($"{core} 레벨", choice.Of(core), n => Edit(x => x.With(core, n))));
        }
        p.Children.Add(Gate(HelpRow("코어 레벨", "딜러는 해와 달, 서포터는 별을 고릅니다.", [.. levels])));

        foreach (var trigger in effects.Where(e => e.Tracks).Select(e => e.Trigger!).Distinct())
            p.Children.Add(Gate(HelpRow($"{Josa.Subject(trigger)} 있는 칸", "앱은 스킬 이름을 읽지 않습니다. 이 스킬을 어느 칸에 두었는지 고르세요. 그 칸이 쿨타임에 들어가는 것을 보면 효과를 적용합니다.",
                SlotCombo(choice.Slots.TryGetValue(trigger, out var slot) ? slot : null, v => Edit(x => x.WithSlot(trigger, v))))));

        p.Children.Add(Note(effects.Count == 0 ? NoCoreEffects : "효과: " + CoreEffects.Describe(effects, SlotKeyName)));
    }

    static ComboBox LevelCombo(string name, int level, Action<int> set)
    {
        var c = new ComboBox { MinWidth = 64, ItemsSource = Enumerable.Range(CoreChoice.MinLevel, CoreChoice.MaxLevel - CoreChoice.MinLevel + 1).ToList(), SelectedItem = level };
        System.Windows.Automation.AutomationProperties.SetName(c, name);
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is int v) set(v); };
        return c;
    }

    /// <summary>선택 안 함 and 일반1–8, each with the user's key ("일반3 (E)").</summary>
    ComboBox SlotCombo(SlotId? slot, Action<SlotId?> set)
    {
        var items = Enumerable.Range(0, 8).Select(i => SlotId.일반1 + i)
            .Select(id => new ComboBoxItem { Content = SlotWithKey(_s, id), Tag = id }).Prepend(new ComboBoxItem { Content = NoSlotChosen }).ToList();
        var c = new ComboBox { MinWidth = 140, ItemsSource = items };
        c.SelectedItem = items.FirstOrDefault(i => i.Tag is SlotId id && id == slot) ?? items[0];
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem i) set(i.Tag as SlotId?); };
        return c;
    }
}
