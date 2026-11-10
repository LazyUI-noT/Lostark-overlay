using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>타임라인's 휴지통 chip (user, 2026-11-10): the fights pruned, un-kept past N or deleted, from every store. 보관 brings
/// one back to its character as 보관 with its recorded time; 삭제 here is for good; the bin empties every week at a chosen time.</summary>
public partial class SettingsWindow
{
    const string BinKey = "bin/";   // a 휴지통 fight's key: "bin/" + its key in its own store's list (test/, imported/ or none)
    const string BinChip = "\u0001휴지통";   // _recordChar for the 휴지통 chip: no character can be named this
    static readonly string[] DayNames = ["일", "월", "화", "수", "목", "금", "토"];   // DayOfWeek order

    /// <summary>Every store's bin, newest fight first.</summary>
    IReadOnlyList<(string File, FightRecord Record)> BinRecords()
    {
        try
        {
            return [.. new[] { ("", Records), (TestKey, TestRecords), (ImportKey, ImportedRecords) }
                .SelectMany(s => s.Item2?.Bin.List().Select(x => (File: BinKey + s.Item1 + x.File, x.Record)) ?? [])
                .OrderByDescending(x => x.Record.StartedAt)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] bin list failed: {ex.Message}"); return []; }
    }

    void FillBin(StackPanel p, IReadOnlyList<(string File, FightRecord Record)> bin)
    {
        p.Children.Add(BinScheduleRow());
        var next = BinSchedule.Next(DateTime.Now, _s.BinEmptyDay, _s.BinEmptyHour);
        p.Children.Add(Note($"밀려난 기록과 삭제한 기록입니다. 되돌리기를 누르면 원래 캐릭터의 보관 기록으로 돌아갑니다(시각은 처음 기록된 그대로). " +
            $"다음 비우기: {next:MM-dd} ({DayNames[(int)next.DayOfWeek]}) {next:HH:mm}"));
        foreach (var (key, r) in bin) p.Children.Add(BinRow(key, r));
    }

    /// <summary>휴지통 비우기: the day and hour. A change counts as a run, so moving the time back never empties at once.</summary>
    UIElement BinScheduleRow()
    {
        var day = Combo();
        day.MinWidth = 70;
        day.ItemsSource = DayNames.Select(d => $"{d}요일").ToList();
        day.SelectedIndex = (int)_s.BinEmptyDay;
        var hour = Combo();
        hour.MinWidth = 70;
        hour.ItemsSource = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
        hour.SelectedIndex = Math.Clamp(_s.BinEmptyHour, 0, 23);
        System.Windows.Automation.AutomationProperties.SetName(hour, "비우는 시각");   // the row label names the day only
        void Set()
        {
            if (day.SelectedIndex < 0 || hour.SelectedIndex < 0) return;
            (_s.BinEmptyDay, _s.BinEmptyHour, _s.BinEmptiedAt) = ((DayOfWeek)day.SelectedIndex, hour.SelectedIndex, DateTime.Now);
            Changed();
            FillRecords();
        }
        day.SelectionChanged += (_, _) => Set();
        hour.SelectionChanged += (_, _) => Set();
        return Row("매주 비우기", day, hour);
    }

    UIElement BinRow(string key, FightRecord r)
    {
        string inner = key[BinKey.Length..];
        var (bin, name) = StoreOf(key);
        var home = StoreOf(inner).Store;
        Thickness gap = new(12, 0, 0, 0);
        var restore = new Button
        {
            Content = "되돌리기", Margin = gap, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "원래 캐릭터의 기록으로 되돌려 보관합니다.",
        };
        restore.Click += (_, _) => RecordAction(() => home?.Restore(name, _s.RecordKeep));
        var erase = DeleteButton(gap, "휴지통에서 지웁니다. 되돌릴 수 없습니다.", "영구 삭제");
        erase.Click += (_, _) =>
        {
            if (Warn(WarningKind.EraseFromBin, "이 기록을 휴지통에서 지울까요? 되돌릴 수 없습니다.")) RecordAction(() => bin?.Delete(name));
        };
        return NameButtons(new WrapPanel { Margin = new(0, 4, 0, 4), Children = { RecordText(inner, r, inBin: true), restore, erase } }, r);
    }
}
