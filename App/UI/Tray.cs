using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Notification-area icon and menu (ARCHITECTURE.md §5). Raises one event per action and calls no services.
/// The icon is drawn at runtime: normal, dimmed (game not running), with a dot while a party session is active.</summary>
public sealed class Tray : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _overlay, _edit, _test, _character, _refit, _detectSubclass, _rereadIcons, _party, _host, _join, _copyToken, _leave;
    readonly ToolStripMenuItem _profiles, _raid, _raidCount, _testFront, _testOff, _testReinforced, _parts;
    readonly Dictionary<OverlayPart, ToolStripMenuItem> _partItems = [];

    /// <summary>기능 Map ▸'s names, in menu order (as 설정 → 기능 Map).</summary>
    internal static readonly (OverlayPart Part, string Name)[] PartNames =
        [(OverlayPart.Skills, "스킬 칸"), (OverlayPart.Gauge, "아덴 게이지"), (OverlayPart.Buffs, "버프 막대"), (OverlayPart.Debuffs, "디버프 창"),
         (OverlayPart.Judgment, "단심 창"), (OverlayPart.Amsu, "암수 창"), (OverlayPart.Meter, "DPS 미터기"), (OverlayPart.BarCount, "줄 수 창"), (OverlayPart.Party, "파티원 창")];
    readonly ToolStripMenuItem[] _profileItems;
    bool _gameRunning = true, _partyActive;

    public event Action? ToggleOverlay, ToggleEditMode, ToggleTestMode, HostParty, JoinParty, CopyToken, LeaveParty, Refit, DetectSubclass, SubmitSkills, RereadIcons, OpenSettings, OpenDiagnostics, Exit;
    /// <summary>직업군, 클래스, 직업각인 picked from the 캐릭터 menu.</summary>
    public event Action<string, string, string>? CharacterPicked;
    /// <summary>기본 (no class) picked from the 캐릭터 menu.</summary>
    public event Action? DefaultPicked;
    /// <summary>기능 Map ▸: a part turned on (true) or off.</summary>
    public event Action<OverlayPart, bool>? PartToggled;
    /// <summary>오버레이 프로필 picked (0-based).</summary>
    public event Action<int>? ProfileChosen;
    /// <summary>The 테스트 모드 submenu is opening: answer with <see cref="SetTestWindows"/>.</summary>
    public event Action? TestMenuOpening;
    /// <summary>A window picked from the 테스트 모드 list.</summary>
    public event Action<IntPtr>? TestWindowPicked;
    /// <summary>테스트 모드 → 끄기.</summary>
    public event Action? TestOff;
    /// <summary>테스트 모드 → 강화 읽기 clicked: the new state.</summary>
    public event Action<bool>? ReinforcedToggled;
    /// <summary>레이드 → 자동 인식 (null) or a gate picked by hand (its id).</summary>
    public event Action<string?>? RaidPicked;
    /// <summary>레이드 → 줄 수 읽기 (모든 레이드) turned on (true) or off.</summary>
    public event Action<bool>? CountToggled;

    public Tray()
    {
        ToolStripMenuItem Item(string text, Action raise)
        {
            var i = new ToolStripMenuItem(text);
            i.Click += (_, _) => raise();
            return i;
        }

        _overlay = Item("오버레이 켜기/끄기", () => ToggleOverlay?.Invoke());
        _overlay.Checked = true;
        _edit = Item("편집 모드", () => ToggleEditMode?.Invoke());
        _test = new ToolStripMenuItem("테스트 모드");
        _testFront = Item("맨 앞 창 따라가기", () => ToggleTestMode?.Invoke());
        _testOff = Item("끄기", () => TestOff?.Invoke());
        _testReinforced = Item("강화 읽기 (압축된 영상)", () => ReinforcedToggled?.Invoke(!_testReinforced!.Checked));
        _testReinforced.ToolTipText = "스트림·720p처럼 압축된 영상의 버프, 디버프, 전분, 보스 체력, 암수를 더 잘 읽습니다. CPU를 더 씁니다. 테스트 모드에서만.";
        // The window list is filled each time the submenu opens (App answers TestMenuOpening with SetTestWindows).
        _test.DropDownItems.Add(_testFront);
        _test.DropDownOpening += (_, _) => TestMenuOpening?.Invoke();
        _profileItems = [.. Enumerable.Range(0, OverlayProfile.Count).Select(i => Item($"배치 {i + 1}", () => ProfileChosen?.Invoke(i)))];
        _profiles = new ToolStripMenuItem("배치");
        _profiles.DropDownItems.AddRange(_profileItems);
        _parts = new ToolStripMenuItem("기능 맵");
        foreach (var (part, name) in PartNames)
        {
            var item = new ToolStripMenuItem(name);
            item.Click += (_, _) => PartToggled?.Invoke(part, !item.Checked);
            _partItems[part] = item;
            _parts.DropDownItems.Add(item);
        }
        _character = new ToolStripMenuItem("직업 인식: 없음");
        _raid = new ToolStripMenuItem();
        _raidCount = Item("줄 수 읽기 (모든 레이드)", () => CountToggled?.Invoke(!_raidCount!.Checked));
        _raid.DropDownItems.AddRange([_raidCount, new ToolStripSeparator(), Item("자동 인식", () => RaidPicked?.Invoke(null))]);
        SetRaid(new RaidSettings(), [], null);
        _refit = Item("스킬창 다시 맞추기 (+직업 확인)", () => Refit?.Invoke());
        _detectSubclass = Item("직업각인 다시 읽기", () => DetectSubclass?.Invoke());
        var submitSkills = Item("스킬 등록", () => SubmitSkills?.Invoke());
        _rereadIcons = Item("아이콘 다시 읽기", () => RereadIcons?.Invoke());
        _host = Item("호스트 시작", () => HostParty?.Invoke());
        _join = Item("참가…", () => JoinParty?.Invoke());
        _copyToken = Item("참가 코드 복사", () => CopyToken?.Invoke());
        _leave = Item("나가기", () => LeaveParty?.Invoke());
        _party = new ToolStripMenuItem("공유: 없음");
        _party.DropDownItems.AddRange([_host, _join, _copyToken, _leave]);

        // Rarely used tools in one submenu, so the top level keeps what is used between gates.
        var tools = new ToolStripMenuItem("인식 도구");
        tools.DropDownItems.AddRange([_detectSubclass, submitSkills, _rereadIcons, new ToolStripSeparator(),
            Item("진단", () => OpenDiagnostics?.Invoke())]);
        // 설정 열기 first and bold: it is what double-clicking the icon does (Windows' default-item convention).
        var settings = Item("설정 열기", () => OpenSettings?.Invoke());
        settings.Font = _bold = new Font(settings.Font, FontStyle.Bold);
        // Windows' dark mode or high contrast switched while running: redraw the menu for it.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemColors;

        _menu.Items.AddRange(
        [
            settings,
            new ToolStripSeparator(),
            _overlay,
            _edit,
            _test,
            _profiles,
            _parts,
            new ToolStripSeparator(),
            _character,
            _raid,
            _party,
            new ToolStripSeparator(),
            _refit,
            tools,
            new ToolStripSeparator(),
            Item("종료", () => Exit?.Invoke()),
        ]);
        SetParty(null);

        _icon = new NotifyIcon { Text = "GaugeOverlay", ContextMenuStrip = _menu, Visible = true };
        // Left click opens the menu too (Windows convention); NotifyIcon only does that for the right button.
        _icon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowContextMenu?.Invoke(_icon, null); };
        _icon.DoubleClick += (_, _) => { _menu.Close(); OpenSettings?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => { var click = _onNoticeClick; _onNoticeClick = null; click?.Invoke(); };
        _icon.BalloonTipClosed += (_, _) => _onNoticeClick = null;
        UpdateIcon();
    }

    /// <summary>Whether notices of a kind show (설정 → 경고 → Windows 알림, <see cref="Notices.Shows"/>); App sets it.</summary>
    public Func<NoticeKind, bool> NoticeShown { get; set; } = _ => true;

    /// <summary>A balloon by the tray icon (fit needed, fit result, settings reset), unless its <paramref name="kind"/> is turned
    /// off; <paramref name="onClick"/> runs if it's clicked (one notice at a time).</summary>
    public void Notify(string text, Action? onClick = null, NoticeKind kind = NoticeKind.Guide)
    {
        // A kind turned off still reaches the log: some notices (settings reset, a hotkey not registered) show nowhere else.
        // A muted notice's onClick is dropped with it; no caller passes one today.
        if (!NoticeShown(kind)) { System.Diagnostics.Trace.WriteLine($"[tray] notice off ({kind}): {text}"); return; }
        _onNoticeClick = onClick;
        _icon.ShowBalloonTip(5000, "GaugeOverlay", text, ToolTipIcon.None);
    }
    Action? _onNoticeClick;

    /// <summary>The menu drawn in 설정's theme: dark for 딥 틸 and 어둡게 (and Windows' dark mode), Windows' own otherwise.</summary>
    public void SetTheme(UiTheme theme)
    {
        if (theme == _theme) return;   // ApplyUi runs on every change; the renderer only when the theme does
        _theme = theme;
        _menu.Renderer = TrayRenderer.For(theme);
    }
    UiTheme? _theme;
    readonly Font _bold;

    void OnSystemColors(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Accessibility
            or Microsoft.Win32.UserPreferenceCategory.Color) || _theme is not { } theme) return;
        _menu.Renderer = TrayRenderer.For(theme);
    }

    /// <summary>Checkmark on "오버레이 표시".</summary>
    public void SetOverlayVisible(bool visible) => _overlay.Checked = visible;

    /// <summary>기능 Map ▸'s ticks as the settings say.</summary>
    public void SetParts(Func<OverlayPart, bool> shown)
    {
        foreach (var (part, item) in _partItems) item.Checked = shown(part);
    }

    /// <summary>Checkmark on the active 오버레이 프로필.</summary>
    public void SetProfile(int active)
    {
        for (int i = 0; i < _profileItems.Length; i++) _profileItems[i].Checked = i == active;
    }

    /// <summary>The 레이드 line: 자동 with what it recognises now (<paramref name="seen"/>, a gate id), or the gate picked by hand;
    /// the gates are listed under 자동 인식 the first time they are known.</summary>
    public void SetRaid(RaidSettings raids, IReadOnlyList<RaidGate> gates, string? seen)
    {
        _raidCount.Checked = raids.CountEnabled;
        if (_raid.DropDownItems.Count == 3 && gates.Count > 0)
        {
            _raid.DropDownItems.Add(new ToolStripSeparator());
            foreach (var tab in gates.GroupBy(g => g.TabName))   // 카제로스 레이드 › 종막: 최후의 날 · 1관문 …
            {
                var group = new ToolStripMenuItem(tab.Key);
                foreach (var g in tab)
                {
                    var item = new ToolStripMenuItem(RaidData.Label(g)) { Tag = g.Id };
                    item.Click += (_, _) => RaidPicked?.Invoke(g.Id);
                    group.DropDownItems.Add(item);
                }
                _raid.DropDownItems.Add(group);
            }
        }
        var manual = RaidData.Find(gates, raids.ManualGate);
        _raid.Text = raids.Auto || manual is null
            ? $"레이드: 자동 ({(RaidData.Find(gates, seen) is { } g0 ? RaidData.Label(g0) : "인식 안 됨")})"
            : $"레이드: {RaidData.Label(manual)}";
        foreach (var item in _raid.DropDownItems.OfType<ToolStripMenuItem>().Where(i => i != _raidCount))
        {
            if (item.HasDropDownItems)   // a tab: checked when the gate picked by hand is in it
            {
                foreach (var gate in item.DropDownItems.OfType<ToolStripMenuItem>()) gate.Checked = !raids.Auto && (string?)gate.Tag == raids.ManualGate;
                item.Checked = item.DropDownItems.OfType<ToolStripMenuItem>().Any(x => x.Checked);
            }
            else item.Checked = raids.Auto || manual is null;   // 자동 인식
        }
    }

    /// <summary>Checkmark on "편집 모드".</summary>
    public void SetEditMode(bool on) => _edit.Checked = on;

    /// <summary>Checkmark on "테스트 모드", and on 맨 앞 창 따라가기 while no window is picked.</summary>
    public void SetTestMode(bool on, IntPtr picked)
    {
        _test.Checked = on;
        _testFront.Checked = on && picked == 0;
    }

    /// <summary>Checkmark on 테스트 모드 → 강화 읽기.</summary>
    public void SetReinforced(bool on) => _testReinforced.Checked = on;

    const int MaxTitle = 50;

    /// <summary>The 테스트 모드 submenu: 맨 앞 창 따라가기, the open windows (the picked one checked), 끄기 while on.</summary>
    public void SetTestWindows(IReadOnlyList<GameWindow.Choice> windows, bool on, IntPtr picked)
    {
        _test.DropDownItems.Clear();
        _test.DropDownItems.Add(_testFront);
        _test.DropDownItems.Add(new ToolStripSeparator());
        if (windows.Count == 0) _test.DropDownItems.Add(new ToolStripMenuItem("열린 창 없음") { Enabled = false });
        foreach (var w in windows)
        {
            var title = w.Title.Length > MaxTitle ? w.Title[..MaxTitle] + "…" : w.Title;
            var item = new ToolStripMenuItem(title.Replace("&", "&&")) { Checked = on && w.Hwnd == picked, ToolTipText = w.Title };
            item.Click += (_, _) => TestWindowPicked?.Invoke(w.Hwnd);
            _test.DropDownItems.Add(item);
        }
        _test.DropDownItems.Add(new ToolStripSeparator());
        _test.DropDownItems.Add(_testReinforced);
        _testOff.Enabled = on;
        _test.DropDownItems.Add(_testOff);
    }

    /// <summary>Shows the user's hotkeys next to the items.</summary>
    public void SetHotkeys(HotkeySettings keys)
    {
        _overlay.ShortcutKeyDisplayString = KeyNames.Display(keys.ToggleOverlay);
        _edit.ShortcutKeyDisplayString = KeyNames.Display(keys.EditMode);
        _refit.ShortcutKeyDisplayString = KeyNames.Display(keys.DetectCharacter);   // 스킬창 다시 맞추기, which detects too
        _detectSubclass.ShortcutKeyDisplayString = keys.DetectSubclass == "" ? "" : KeyNames.Display(keys.DetectSubclass);
        _rereadIcons.ShortcutKeyDisplayString = keys.RereadIcons == "" ? "" : KeyNames.Display(keys.RereadIcons);
        _testFront.ShortcutKeyDisplayString = KeyNames.Display(keys.TestMode);
        string[] profiles = [keys.Profile1, keys.Profile2, keys.Profile3, keys.Profile4, keys.Profile5];
        for (int i = 0; i < _profileItems.Length; i++) _profileItems[i].ShortcutKeyDisplayString = profiles[i] == "" ? "" : KeyNames.Display(profiles[i]);
    }

    /// <summary>Dims the icon while the game isn't running.</summary>
    public void SetGameRunning(bool running)
    {
        _gameRunning = running;
        UpdateIcon();
    }

    /// <summary>Character submenu: 기본 (no class), then every class (직업군 apart by separators) with its 직업각인; the
    /// active one checked.</summary>
    /// <param name="favorites">★ in 설정 (a class, or "클래스 · 직업각인"): listed again at the top for a quick pick.</param>
    public void SetCharacters(IEnumerable<(string Family, IEnumerable<(string Class, IReadOnlyList<string> Subclasses)> Classes)> tree,
        string? active, IReadOnlyCollection<string> favorites)
    {
        _character.Text = $"직업 인식: {active ?? "기본"}";
        _character.DropDownItems.Clear();
        var none = new ToolStripMenuItem("기본 (클래스 없음)") { Checked = active is null };
        none.Click += (_, _) => DefaultPicked?.Invoke();
        _character.DropDownItems.Add(none);
        var starred = tree.SelectMany(f => f.Classes.SelectMany(c => c.Subclasses.Select(sub => (f.Family, c.Class, Sub: sub))))
            .Where(x => favorites.Contains(x.Class) || favorites.Contains($"{x.Class} · {x.Sub}")).ToList();
        if (starred.Count > 0) _character.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (family, cls, sub) in starred)
        {
            var s = new ToolStripMenuItem($"★ {cls} · {sub}") { Checked = active == $"{cls} · {sub}" };
            s.Click += (_, _) => CharacterPicked?.Invoke(family, cls, sub);
            _character.DropDownItems.Add(s);
        }
        foreach (var (family, classes) in tree)
        {
            _character.DropDownItems.Add(new ToolStripSeparator());
            foreach (var (cls, subs) in classes)
            {
                var c = new ToolStripMenuItem(cls) { Checked = active?.StartsWith($"{cls} · ", StringComparison.Ordinal) == true };
                foreach (var sub in subs)
                {
                    var s = new ToolStripMenuItem(sub) { Checked = active == $"{cls} · {sub}" };
                    s.Click += (_, _) => CharacterPicked?.Invoke(family, cls, sub);
                    c.DropDownItems.Add(s);
                }
                _character.DropDownItems.Add(c);
            }
        }
    }

    /// <summary>Party line, e.g. "호스트 중 (3/4)"; null when not in a session. Also sets the icon's dot.</summary>
    public void SetParty(string? status)
    {
        _partyActive = status is not null;
        _party.Text = $"공유: {status ?? "없음"}";
        _host.Visible = _join.Visible = !_partyActive;
        _copyToken.Visible = _leave.Visible = _partyActive;
        if (_icon is not null) UpdateIcon();
    }

    void UpdateIcon()
    {
        var old = _icon.Icon;
        _icon.Icon = Draw(SystemInformation.SmallIconSize.Width, !_gameRunning, _partyActive);
        old?.Dispose();
    }

    /// <summary>A tiny version of the overlay: 각성기 column, 2×2 skills, 아덴 column.</summary>
    static Icon Draw(int n, bool dimmed, bool dot)
    {
        using var bmp = new Bitmap(n, n);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = n / 16f;
            using var plate = new SolidBrush(Color.FromArgb(dimmed ? 150 : 235, 12, 13, 16));
            g.FillRectangle(plate, 0, 2 * u, n, 12 * u);
            Color C(int r, int gr, int b) => dimmed ? Color.FromArgb(110, 110, 110) : Color.FromArgb(r, gr, b);
            void Box(Color c, float x, float y, float w, float h) { using var br = new SolidBrush(c); g.FillRectangle(br, x * u, y * u, w * u, h * u); }
            Box(C(164, 216, 43), 1, 3, 3, 10);
            Box(C(255, 107, 112), 5, 3, 3, 4.5f);
            Box(C(255, 107, 112), 9, 3, 3, 4.5f);
            Box(C(255, 107, 112), 5, 8.5f, 3, 4.5f);
            Box(C(110, 40, 44), 9, 8.5f, 3, 4.5f);
            Box(C(128, 152, 255), 13, 3, 2, 10);
            if (dot)
            {
                using var ring = new SolidBrush(Color.FromArgb(12, 13, 16));
                using var fill = new SolidBrush(Color.FromArgb(63, 185, 80));
                g.FillEllipse(ring, 9 * u, 9 * u, 7 * u, 7 * u);
                g.FillEllipse(fill, 10 * u, 10 * u, 5 * u, 5 * u);
            }
        }
        nint h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    // NotifyIcon's own right-click path: it foregrounds the menu so it closes when you click elsewhere.
    static readonly MethodInfo? ShowContextMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(nint handle);

    /// <summary>--demo --tray-menu: the menu opened near the top left of the screen, for screenshots.</summary>
    internal void DemoShowMenu() => _menu.Show(new Point(40, 40));

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemColors;
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
        _bold.Dispose();
    }
}
