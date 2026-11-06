using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>전투 기록 내보내기 / 가져오기 (homework.plan.md, user 2026-11-04): fights saved as one .gofight file for another user;
/// a file opened here (가져오기, or dropped on the page) lands under its own [가져옴] chip, never mixed with mine, kept until
/// deleted, and can be compared with my fights of the same raid and difficulty.</summary>
public partial class SettingsWindow
{
    const string ImportChip = "[가져옴]";
    const string ImportKey = "import/";   // an imported record's key in the list (its file names may repeat mine)
    const string ShareFilter = "전투 기록 (*" + RecordExport.Extension + ")|*" + RecordExport.Extension;
    bool _dropHooked;

    /// <summary>Fights opened from another user's file: their own folder, never pruned.</summary>
    public RecordStore? ImportedRecords { get; set; }

    /// <summary>가져오기 and 내보내기 above the list: <paramref name="shown"/> is what 내보내기 saves.</summary>
    UIElement ShareRow(IReadOnlyList<(string File, FightRecord Record)> shown)
    {
        var import = new Button { Content = "가져오기…", Margin = new(0, 0, 8, 0) };
        import.Click += (_, _) =>
        {
            var pick = new Microsoft.Win32.OpenFileDialog { Filter = ShareFilter, Multiselect = true };
            if (pick.ShowDialog(this) == true) ImportFiles(pick.FileNames);
        };
        var export = new Button { Content = $"이 목록 내보내기 ({shown.Count})", IsEnabled = shown.Count > 0 };
        export.Click += (_, _) => ExportFights([.. shown.Select(x => x.Record)]);
        var row = new WrapPanel { Margin = new(0, 4, 0, 6), Children = { import, export } };
        row.Children.Add(new TextBlock
        {
            Text = "다른 사람에게 보낼 파일로 저장합니다. 캐릭터와 파티원 이름은 빠지고, 클래스 · 직업각인은 남습니다. 받은 파일은 가져오기를 누르거나 이 창에 끌어다 놓으세요.",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        HookDrop();
        return row;
    }

    /// <summary>A .gofight file dropped anywhere on the page is imported.</summary>
    void HookDrop()
    {
        if (_dropHooked) return;
        _dropHooked = true;
        _secRecords.AllowDrop = true;
        _secRecords.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Any(IsShare) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        _secRecords.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f) ImportFiles(f.Where(IsShare)); };
    }

    static bool IsShare(string path) => path.EndsWith(RecordExport.Extension, StringComparison.OrdinalIgnoreCase);

    void ExportFights(IReadOnlyList<FightRecord> fights)
    {
        if (fights.Count == 0) return;
        var first = fights[0];
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Filter = ShareFilter, DefaultExt = RecordExport.Extension,
            FileName = $"{first.StartedAt:yyyyMMdd}_{first.Gate ?? "전투"}{(fights.Count > 1 ? $"_외{fights.Count - 1}" : "")}".Replace(':', '-').Replace('·', '-'),
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            using var file = File.Create(save.FileName);
            RecordExport.Write(file, fights);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"[records] export failed: {ex.Message}");
            MessageBox.Show(this, "파일을 저장하지 못했습니다.", AreaRecords);
        }
    }

    void ImportFiles(IEnumerable<string> paths)
    {
        if (ImportedRecords is not { } store) return;
        int added = 0;
        foreach (var path in paths)
        {
            try
            {
                using var file = File.OpenRead(path);
                foreach (var r in RecordExport.Read(file)) { store.Save(r with { Kept = true }, RecordStore.MaxKeep); added++; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Trace.WriteLine($"[records] import of {path} failed: {ex.Message}"); }
        }
        MessageBox.Show(this, added > 0 ? $"전투 {added}개를 가져왔습니다. {ImportChip}에서 봅니다." : "가져올 전투가 없습니다(전투 기록 파일이 아니거나 읽을 수 없습니다).", AreaRecords);
        if (added > 0) { (_recordChar, _opened) = (ImportChip, null); _compare.Clear(); }
        FillRecords();
    }
}
