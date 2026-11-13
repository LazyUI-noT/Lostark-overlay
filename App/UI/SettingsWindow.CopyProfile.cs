using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>The 복사 page's profile block (class-profiles.plan.md C): three modes, from → to, 복사할 것, the confirm line and
/// 되돌리기 for the latest 복사. What may be copied and what is overwritten is <see cref="ProfileCopy"/>'s and
/// <see cref="Settings.Copy"/>'s; this file only asks and shows.</summary>
public partial class SettingsWindow
{
    CopyMode _copyMode = CopyMode.AllClasses;
    int? _cpFromProfile, _cpToProfile;
    string? _cpFromClass, _cpToClass;
    readonly HashSet<LookGroup> _cpLooks = [.. Enum.GetValues<LookGroup>()];
    bool _cpClassOnly = true;   // what the user wants; shown ticked only while the two sides allow it
    CopyUndo? _lastCopy;   // the latest 복사, until the next one or SettingsSwapped (not saved)
    string? _profileCopyResult;

    /// <summary>The request the page shows now (sides defaulted, clamped to what exists).</summary>
    CopyRequest CopyRequestNow()
    {
        int from = _cpFromProfile ?? _s.ActiveProfile;
        string fromClass = ClassPicker.Normalize(_cpFromClass, _s.PlaceKey ?? ClassProfiles.Template, PickerEntries);
        string toClass = ClassPicker.Normalize(_cpToClass, fromClass, PickerEntries);
        int to = _copyMode == CopyMode.AllClasses ? from : _cpToProfile ?? (from + 1) % OverlayProfile.Count;
        if (_copyMode == CopyMode.WholeProfile && to == from) to = (from + 1) % OverlayProfile.Count;
        var request = new CopyRequest(_copyMode, from, fromClass, to, _copyMode == CopyMode.ClassToClass ? toClass : null, new HashSet<LookGroup>(_cpLooks), false);
        return request with { ClassOnly = _cpClassOnly && ProfileCopy.ClassOnlyAllowed(request) };
    }

    string ConfirmText(CopyRequest r) =>
        ProfileCopy.Confirm(r, string.IsNullOrEmpty(r.ToClass) ? null : r.ToClass);

    static IEnumerable<int> ProfileNumbers => Enumerable.Range(0, OverlayProfile.Count);

    void FillProfileCopy(Panel p)
    {
        void Refill() => Dispatcher.BeginInvoke(FillCopyPage);
        var r = CopyRequestNow();
        p.Children.Add(CopyModes(Refill));
        var fromProfile = ProfileCombo(ProfileNumbers, r.FromProfile, v => { _cpFromProfile = v; Refill(); });
        p.Children.Add(Row("가져올 곳", r.Mode == CopyMode.WholeProfile ? [fromProfile] : [fromProfile, .. ClassSide(r.FromClass, v => { _cpFromClass = v; Refill(); }, "가져올 곳")]));
        p.Children.Add(r.Mode switch
        {
            CopyMode.AllClasses => Row("대상", Label($"프로필 {r.ToProfile + 1}의 모든 클래스")),
            CopyMode.ClassToClass => Row("대상", [ProfileCombo(ProfileNumbers, r.ToProfile, v => { _cpToProfile = v; Refill(); }),
                .. ClassSide(r.ToClass, v => { _cpToClass = v; Refill(); }, "대상")]),
            _ => Row("대상", ProfileCombo(ProfileNumbers.Where(i => i != r.FromProfile), r.ToProfile, v => { _cpToProfile = v; Refill(); })),
        });

        var confirm = ConfirmNote(ConfirmText(r));
        if (r.Mode != CopyMode.WholeProfile) p.Children.Add(CopyWhat(r, confirm));
        var copy = CopyButton("복사", () => RefuseProfileCopy(CopyRequestNow()),
            () => ("정말 복사", ConfirmText(CopyRequestNow())), text => { _profileCopyResult = text; Refill(); }, RunProfileCopy);
        Themes.Primary(copy);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.ToolTip = "그 프로필의 화면(스크린샷)은 남습니다.";
        p.Children.Add(copy);
        if (_profileCopyResult is { } result) p.Children.Add(Note(result));
        if (_lastCopy is { } undo) p.Children.Add(UndoButton(undo, Refill));
        p.Children.Add(confirm);
    }

    /// <summary>The three modes, one filled (the same toggle as the profile buttons).</summary>
    UIElement CopyModes(Action refill)
    {
        var modes = new WrapPanel { Margin = new(0, 0, 0, 8) };
        foreach (var (mode, text) in new[] { (CopyMode.AllClasses, "모든 클래스에"), (CopyMode.ClassToClass, "클래스 → 클래스"), (CopyMode.WholeProfile, "프로필 전체") })
        {
            var b = Toggle(text, "copyMode", mode == _copyMode);
            b.Checked += (_, _) => { if (_copyMode != mode) { _copyMode = mode; _profileCopyResult = null; refill(); } };
            modes.Children.Add(b);
        }
        return modes;
    }

    /// <summary>A class picker with its 직업 검색, as 편집 중 has.</summary>
    UIElement[] ClassSide(string? current, Action<string> set, string name)
    {
        var pick = PickCombo(ClassPicker.FavoritesFirst(PickerEntries, _s.FavoriteClasses), current ?? ClassProfiles.Template, set);
        return [pick, PickSearch(pick, name)];
    }

    /// <summary>복사할 것: the look boxes, and 클래스 전용, greyed with its reason unless the two sides are the same class.</summary>
    UIElement CopyWhat(CopyRequest r, TextBlock confirm)
    {
        void Update() => confirm.Text = ConfirmText(CopyRequestNow());
        var boxes = Enum.GetValues<LookGroup>().Select(look => (UIElement)Check(LookShare.Name(look), _cpLooks.Contains(look),
            v => _ = v ? _cpLooks.Add(look) : _cpLooks.Remove(look), Update, LookShare.Covers(look))).ToList();
        bool allowed = ProfileCopy.ClassOnlyAllowed(r);
        var classOnly = Check("클래스 전용", allowed && _cpClassOnly, v => _cpClassOnly = v, Update,
            "게이지 구분선, 구간 색, 빛남, 아덴1·아덴2 칸 표시처럼 클래스마다 있는 설정입니다.");
        classOnly.IsEnabled = allowed;
        boxes.Add(classOnly);
        var what = Stack(Row("복사할 것", [.. boxes]));
        if (!allowed) what.Children.Add(Muted(new TextBlock { Text = ProfileCopy.ClassOnlyReason(r), TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 6) }));
        return what;
    }

    static string? RefuseProfileCopy(CopyRequest r) =>
        ProfileCopy.Valid(r, OverlayProfile.Count) ? null
        : r.Mode != CopyMode.WholeProfile && r.Looks.Count == 0 && !r.ClassOnly ? "복사할 것을 고르세요." : "같은 곳으로는 복사할 수 없습니다.";

    string RunProfileCopy()
    {
        var r = CopyRequestNow();
        if (!ProfileCopy.Valid(r, _s.Profiles.Count)) return "복사할 수 없습니다.";   // nothing to record
        RecordStep();
        if (_s.Copy(r) is not { } undo) return "복사할 수 없습니다.";
        _lastCopy = undo;
        Changed();
        if (r.ToProfile == _s.ActiveProfile) ProfileOverwritten?.Invoke();
        return $"프로필 {r.ToProfile + 1}에 복사했습니다.";
    }

    /// <summary>되돌리기 for the latest 복사 only: until the next one (not saved).</summary>
    UIElement UndoButton(CopyUndo undo, Action refill)
    {
        var b = new Button { Content = "되돌리기", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 0),
            ToolTip = "방금 한 복사를 취소합니다. 다음 복사 전까지 쓸 수 있습니다." };
        b.Click += (_, _) =>
        {
            if (undo.Profile >= _s.Profiles.Count) return;
            RecordStep();
            _profileCopyResult = _s.UndoCopy(undo) ? $"프로필 {undo.Profile + 1}을(를) 복사 전으로 되돌렸습니다." : "되돌릴 수 없습니다.";
            _lastCopy = null;
            Changed();
            if (undo.Profile == _s.ActiveProfile) ProfileOverwritten?.Invoke();
            refill();
        };
        return b;
    }
}

public partial class SettingsWindow
{
    /// <summary>--pick=copy --copy-demo=1|2|2diff|2run|3: the 복사 page in that mode (2diff: another class as target, 클래스 전용 greyed;
    /// 2run: after a 복사, 되돌리기 shown), for screenshots.</summary>
    internal void DemoCopy(string demo)
    {
        _copyMode = demo[0] switch { '1' => CopyMode.AllClasses, '3' => CopyMode.WholeProfile, _ => CopyMode.ClassToClass };
        _cpToProfile = 1;
        if (demo == "2diff") _cpToClass = PickerEntries.FirstOrDefault(e => e != CopyRequestNow().FromClass && e.Length > 0);
        if (demo == "2run") _profileCopyResult = RunProfileCopy();
        FillCopyPage();
    }
}
