using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>스킬창 → 내 스킬: the 스킬 칸 표시 switch (the same as 표시's and the tray's), then the 편집 중 pick's skills by
/// group: the group's 표시 and 아이콘, each skill's show switch and colour. The same settings 내 오버레이's 칸 옵션 edits,
/// so the settings file doesn't change.</summary>
public partial class SettingsWindow
{
    readonly CardStack _secSkillList = new();

    void FillSkillList()
    {
        string? cls = BoxKey;
        var p = Refill(_secSkillList, $"내 스킬 ({ClassName(ShownClass)})",
            "편집 중에서 고른 직업각인의 스킬 칸입니다. 반짝임, 경과 초 같은 효과는 내 오버레이에서 칸을 눌러 바꿉니다.");
        p.Children.Add(ShowSwitch(OverlayPart.Skills, Tray.PartNames.First(n => n.Part == OverlayPart.Skills).Name));
        if (!_s.Overlay.SkillRead) p.Children.Add(Note("칸 모양의 스킬창 읽기가 꺼져 있어 스킬 칸이 보이지 않습니다."));

        MineProfile Fx() => ClassProfiles.EditShown(_s, cls).Mine;
        void Again() { Changed(); Dispatcher.BeginInvoke(() => { FillClassSections(); FillBox(); }); }   // 내 오버레이's rows show the same values
        var read = ClassProfiles.Of(_s, ShownClass).Mine;   // as saved; reading doesn't make the pick 개별
        var look = ClassProfiles.Mine(_s, cls);             // the colours as drawn
        var aden = BoxAdenSlots;
        ClassCooldownRows(p, read, Fx);   // 쿨 진행 표시, 남은 쿨 (초): this class's (display-split.plan.md step 5)
        foreach (var g in Enum.GetValues<SlotGroup>())
        {
            var slots = SlotInfo.All.Where(id => SlotInfo.GroupOf(id, aden) == g).ToList();
            if (slots.Count == 0) continue;
            bool icons = g != SlotGroup.아덴스킬 && read.ShowsIcon(g);   // 아덴스킬 has no icon
            // Named with the group, so search tells the groups' switches apart.
            var groupRow = new List<UIElement> { Check($"{g} 표시", read.Groups.GetValueOrDefault(g, true), v => Fx().Groups[g] = v, Again,
                "끄면 이 그룹이 빠지고 배경판이 그만큼 줄어듭니다.") };
            if (g != SlotGroup.아덴스킬)
                groupRow.Add(Check($"{g} 아이콘으로 표시", icons, v => { if (v) Fx().Icons.Add(g); else Fx().Icons.Remove(g); }, Again,
                    "게임 스킬창의 아이콘을 칸에 그립니다. 아이콘은 그 직업각인으로 플레이할 때 읽습니다."));
            p.Children.Add(SubHeading(g.ToString()));
            p.Children.Add(Row("그룹", [.. groupRow]));
            if (icons) p.Children.Add(Note(IconColourNote));
            bool perBox = BoxOptions.PerBox(g);   // 일반스킬: a colour per skill (이 칸만, as 칸 옵션's); the others: one for the group
            if (!perBox) p.Children.Add(ColorRow(Label("색"), look.ColorOf(slots[0], g), v => BoxOptions.SetColor(Fx(), slots[0], g, v, false), swatch: true, warnGold: true));
            foreach (var id in slots)
            {
                var shown = Check(SlotWithKey(_s, id), !read.HiddenSlots.Contains(id), v =>
                {
                    var hidden = Fx().HiddenSlots;
                    if (v) hidden.Remove(id); else hidden.Add(id);
                }, Again, "끄면 이 칸을 숨깁니다. 자리는 비워 둡니다.");
                p.Children.Add(perBox ? ColorRow(shown, look.ColorOf(id, g), v => BoxOptions.SetColor(Fx(), id, g, v, true), swatch: true, warnGold: true) : Row("", shown));
            }
        }
    }
}
