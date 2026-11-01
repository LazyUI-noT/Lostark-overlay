using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 레이드 → HP 변경 감지 (HpChange): the boss max HP values learned (복사 gives the gate's raids.json line, 삭제 drops
/// one), and this raid's unconfirmed ones, faint.</summary>
public partial class SettingsWindow
{
    IReadOnlyList<FightHp> _fightHp = [];

    /// <summary>This raid's HP values (Engine.FightHpChanged); UI thread.</summary>
    public void SetFightHp(IReadOnlyList<FightHp> list)
    {
        _fightHp = list;
        FillRaid();
    }

    void HpChangeRows(StackPanel p, Action again)
    {
        var r = _s.Raids;
        var saved = RaidGates.SelectMany(g => (r.Gates.GetValueOrDefault(g.Id)?.LearnedHp ?? []).Select(l => (Gate: g, Difficulty: l.Key, l.Value))).ToList();
        var pending = RaidGates.SelectMany(g => (r.Gates.GetValueOrDefault(g.Id)?.PendingHp ?? []).Select(l => (Gate: g, Difficulty: l.Key, l.Value))).ToList();
        var unconfirmed = _fightHp.Where(f => f.Trust == HpTrust.Unconfirmed).ToList();
        if (saved.Count == 0 && pending.Count == 0 && unconfirmed.Count == 0) return;
        p.Children.Add(SubHeading("HP 변경 감지",
            "게임 업데이트로 보스 체력이 바뀌면, 던전 난이도와 맞는 값을 이 PC에 저장해 보스와 난이도를 계속 알아봅니다. " +
            "처음 보는 난이도의 값은 다음 레이드에서 같은 값을 다시 볼 때 저장합니다. " +
            "아무 데도 보내지 않습니다. 복사한 줄을 알려 주시면 다음 버전에 넣습니다."));
        var effective = HpChange.Effective(RaidGates, r, _fightHp);
        foreach (var (gate, difficulty, learned) in saved)
        {
            var copy = new Button { Content = "복사", Padding = new(10, 3, 10, 3), Margin = new(8, 0, 0, 0) };
            copy.Click += (_, _) => Clipboard.SetText(HpChange.HpLine(RaidData.Find(effective, gate.Id)!));
            var remove = new Button { Content = "삭제", Padding = new(10, 3, 10, 3), Margin = new(6, 0, 0, 0) };
            remove.Click += (_, _) => { r.Gates[gate.Id].LearnedHp!.Remove(difficulty); Changed(); again(); };
            p.Children.Add(Row($"{RaidData.Label(gate)} · {difficulty}", Label(Change(learned.Replaced, learned.Hp), 0.8), copy, remove));
        }
        foreach (var (gate, difficulty, first) in pending)   // a first value: saved once a later raid reads it again
        {
            var remove = new Button { Content = "삭제", Padding = new(10, 3, 10, 3), Margin = new(8, 0, 0, 0) };
            remove.Click += (_, _) => { r.Gates[gate.Id].PendingHp!.Remove(difficulty); Changed(); again(); };
            p.Children.Add(Row($"{RaidData.Label(gate)} · {difficulty}",
                Label($"새 값 {first.Hp:N0} · 미확인 ({first.Seen.ToLocalTime():MM-dd} 처음 봄, 다음 레이드에서 다시 보면 저장)", 0.5), remove));
        }
        foreach (var f in unconfirmed)
            if (RaidData.Find(RaidGates, f.GateId) is { } gate)
                p.Children.Add(Row($"{RaidData.Label(gate)} · {f.Difficulty}",
                    Label(Change(gate.Hp.TryGetValue(f.Difficulty, out var was) ? was : null, f.Hp) + " · 미확인 (이번 레이드만)", 0.5)));
    }

    static string Change(long? from, long to) => from is { } f ? $"{f:N0} → {to:N0}" : $"새 값 {to:N0}";
}
