using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>편집 중's pick as 설정 shows it: a 직업각인 other than the character's shows its sample on the 내 오버레이 canvas and in
/// the page previews (<see cref="PreviewTarget"/>, with its saved icons), and the 칸 옵션 follow it. The real overlay is never
/// changed by it.</summary>
public partial class SettingsWindow
{
    readonly IconStore _iconStore = new(IconStore.DefaultDir);

    /// <summary>What the canvas and the previews show of my window, or null for the live data: the pick is the character's own
    /// entry or 기본. Never set for the 레이아웃 편집기's copy, which edits the character.</summary>
    public PreviewTarget? PreviewTarget { get; private set; }

    /// <summary>The saved icons (<see cref="IconStore"/>) of <see cref="PreviewTarget"/>'s 직업각인; empty when it has none.</summary>
    public IReadOnlyDictionary<SlotId, Image> PreviewIcons { get; private set; } = new Dictionary<SlotId, Image>();

    System.Windows.Controls.ComboBox? _pickCombo;
    IReadOnlyList<ClassInfo>? _entriesFor;
    IReadOnlyList<string> _entries = [];
    string? _iconsKey;   // the target whose icons are loaded
    Settings? _copy;

    /// <summary>What the per-직업각인 look rows edit (edit-follows-pick.plan.md): 편집 중's copy in the active profile
    /// (<see cref="Settings.EditTarget"/>) when it isn't the played 직업각인, else the live settings. Global and per-profile
    /// rows keep editing <see cref="_s"/>.</summary>
    Settings Target => _copy ?? _s;

    /// <summary>Stores an edit made through <see cref="Target"/> as 편집 중's entry; the live settings need nothing.</summary>
    void KeepCopy()
    {
        if (_copy is null) return;
        _s.KeepEdit(ShownClass, _copy);
        _s.MirrorLive(_copy);   // a live-only edit (타이머 크기, 줄 수 창 …) shows on the canvas drawing the copy
    }

    /// <summary>The 편집 중 list, rebuilt only when the class list is another one.</summary>
    IReadOnlyList<string> PickerEntries
    {
        get
        {
            if (!ReferenceEquals(_entriesFor, _chars.Classes)) (_entries, _entriesFor) = (ClassPicker.PickerEntries(_chars.Classes), _chars.Classes);
            return _entries;
        }
    }

    /// <summary>편집 중's pick as a class and 직업각인 (null for 기본, or a class the profiles don't list): the per-직업각인 options
    /// (단심, 아덴 게이지 막대) edit this one, not the character's (user, 2026-11-13).</summary>
    (ClassInfo Class, string Sub)? ShownSubclass =>
        _chars.Classes.SelectMany(c => c.Subclasses.Keys.Select(sub => (Class: c, Sub: sub)))
            .Where(x => Settings.CharacterKey(x.Class.Name, x.Sub) == ShownClass).Select(x => ((ClassInfo, string)?)x).FirstOrDefault();

    /// <summary>The entry the 칸 옵션 edit: the pick (null = 기본).</summary>
    string? BoxKey => ShownClass == ClassProfiles.Template ? null : ShownClass;

    /// <summary>The 아덴 slots of <see cref="BoxKey"/>: the character's, the pick's own, or none for 기본.</summary>
    IReadOnlyCollection<SlotId> BoxAdenSlots =>
        ShownClass == ActiveOrTemplate ? _chars.AdenSlots : PreviewTarget?.AdenSlots ?? [];

    /// <summary>Picks <paramref name="key"/> (an entry of <see cref="ClassPicker.PickerEntries"/>) in 편집 중, as the user does.</summary>
    internal void PickEntry(string key)
    {
        if (key != ShownClass) { RecordStep(); _box = null; }   // a slot of the 직업각인 left must not stay selected
        _classShown = key;
        ResetBaseline();
        RefreshPick();
        RefreshBadges();
        Dispatcher.BeginInvoke(() =>
        {
            FillClassSections(); FillBox(); FillDisplay(); FillMeter(); FillGauge(); FillCharacter(); RefreshInspector();   // FillDisplay: 칸 모양, 단심, 암수; FillMeter: its look; FillCharacter: 코어 빌드
            _pickCombo?.Focus();   // the rebuild replaced the combo the keyboard was on
        });
    }

    /// <summary>Decides the target for the pick and the character now, loads its icons, points the 설정 preview at it.
    /// Call after the pick or the active character changed, before the sections are built.
    /// The icons are read again only for another target, or with <paramref name="reloadIcons"/> (설정 opened: they may have been read since).</summary>
    void RefreshPick(bool reloadIcons = false)
    {
        PreviewTarget = PreviewTarget.Decide(ShownClass, ClassProfiles.ActiveKey(_s), _chars.Classes);
        _copy = _s.EditTarget(ShownClass);
        if (PreviewTarget?.Key != _iconsKey || reloadIcons)
        {
            _iconsKey = PreviewTarget?.Key;
            PreviewIcons = PreviewTarget is { } t ? _iconStore.Load(t.Class, t.Subclass) : new Dictionary<SlotId, Image>();
        }
        _canvas?.SetTarget(PreviewTarget);
        _canvas?.SetEdit(Target);   // its windows' spots and sizes are 편집 중's too
        PreviewChanged();   // the page's preview is redrawn for the pick (the 기력·충격 창 only for a class with a ring)
    }
}
