using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>Notification-area icon and menu (ARCHITECTURE.md §5). Raises one event per action and calls no services.
/// The icon is drawn at runtime: normal, dimmed (game not running), with a dot while a party session is active.</summary>
public sealed class Tray : IDisposable
{
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _overlay, _edit, _test, _character, _refit, _detectSubclass, _rereadIcons, _party, _host, _join, _copyToken, _leave;
    readonly ToolStripMenuItem _profiles, _raid;
    readonly ToolStripMenuItem[] _profileItems;
    bool _gameRunning = true, _partyActive;

    public event Action? ToggleOverlay, ToggleEditMode, ToggleTestMode, HostParty, JoinParty, CopyToken, LeaveParty, Refit, DetectSubclass, SubmitSkills, RereadIcons, OpenSettings, OpenLayoutEditor, OpenDebug, Exit;
    /// <summary>직업군, 클래스, 직업각인 picked from the 캐릭터 menu.</summary>
    public event Action<string, string, string>? CharacterPicked;
    /// <summary>기본 (no class) picked from the 캐릭터 menu.</summary>
    public event Action? DefaultPicked;
    /// <summary>오버레이 프로필 picked (0-based).</summary>
    public event Action<int>? ProfileChosen;
    /// <summary>디버프 레이드 picked (없음: off).</summary>
    public event Action<Raid>? RaidChosen;

    /// <summary>What a raid is called in the tray menu and in 설정 → 버프/디버프.</summary>
    public static string RaidName(Raid r) => r switch { Raid.모르둠 => "G3 모르둠", _ => "끔" };

    public Tray()
    {
        ToolStripMenuItem Item(string text, Action raise)
        {
            var i = new ToolStripMenuItem(text);
            i.Click += (_, _) => raise();
            return i;
        }

        _overlay = Item("오버레이 표시", () => ToggleOverlay?.Invoke());
        _overlay.Checked = true;
        _edit = Item("편집 모드", () => ToggleEditMode?.Invoke());
        _test = Item("테스트 모드 (맨 앞 창을 게임으로)", () => ToggleTestMode?.Invoke());
        _profileItems = [.. Enumerable.Range(0, OverlayProfile.Count).Select(i => Item($"프로필 {i + 1}", () => ProfileChosen?.Invoke(i)))];
        _profiles = new ToolStripMenuItem("오버레이 프로필");
        _profiles.DropDownItems.AddRange(_profileItems);
        _character = new ToolStripMenuItem("캐릭터: 없음");
        _raid = new ToolStripMenuItem();
        foreach (var r in Enum.GetValues<Raid>()) _raid.DropDownItems.Add(Item(RaidName(r), () => RaidChosen?.Invoke(r)));
        SetRaid(Raid.없음);
        _refit = Item("스킬바 다시 맞추기", () => Refit?.Invoke());
        _detectSubclass = Item("직업각인 다시 감지", () => DetectSubclass?.Invoke());
        var submitSkills = Item("스킬 등록", () => SubmitSkills?.Invoke());
        _rereadIcons = Item("아이콘 다시 읽기", () => RereadIcons?.Invoke());
        _host = Item("호스트 시작", () => HostParty?.Invoke());
        _join = Item("참가…", () => JoinParty?.Invoke());
        _copyToken = Item("토큰 복사", () => CopyToken?.Invoke());
        _leave = Item("나가기", () => LeaveParty?.Invoke());
        _party = new ToolStripMenuItem("파티: 없음");
        _party.DropDownItems.AddRange([_host, _join, _copyToken, _leave]);

        _menu.Items.AddRange(
        [
            _overlay,
            _edit,
            _test,
            _profiles,
            new ToolStripSeparator(),
            _character,
            _raid,
            new ToolStripSeparator(),
            _party,
            new ToolStripSeparator(),
            _refit,
            _detectSubclass,
            submitSkills,
            _rereadIcons,
            Item("설정 열기", () => OpenSettings?.Invoke()),
            Item("레이아웃 편집기", () => OpenLayoutEditor?.Invoke()),
            Item("디버그 창", () => OpenDebug?.Invoke()),
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

    /// <summary>A short Windows notification from the tray icon (fit needed, fit result, settings reset).</summary>
    /// <summary>A balloon by the tray icon; <paramref name="onClick"/> runs if it's clicked (one notice at a time).</summary>
    public void Notify(string text, Action? onClick = null)
    {
        _onNoticeClick = onClick;
        _icon.ShowBalloonTip(5000, "GaugeOverlay", text, ToolTipIcon.None);
    }
    Action? _onNoticeClick;

    /// <summary>Checkmark on "오버레이 표시".</summary>
    public void SetOverlayVisible(bool visible) => _overlay.Checked = visible;

    /// <summary>Checkmark on the active 오버레이 프로필.</summary>
    public void SetProfile(int active)
    {
        for (int i = 0; i < _profileItems.Length; i++) _profileItems[i].Checked = i == active;
    }

    /// <summary>The 디버프 line and the checkmark on the raid chosen.</summary>
    public void SetRaid(Raid active)
    {
        _raid.Text = $"디버프: {RaidName(active)}";
        foreach (var (item, r) in _raid.DropDownItems.OfType<ToolStripMenuItem>().Zip(Enum.GetValues<Raid>())) item.Checked = r == active;
    }

    /// <summary>Checkmark on "편집 모드".</summary>
    public void SetEditMode(bool on) => _edit.Checked = on;

    /// <summary>Checkmark on "테스트 모드".</summary>
    public void SetTestMode(bool on) => _test.Checked = on;

    /// <summary>Shows the user's hotkeys next to the items.</summary>
    public void SetHotkeys(HotkeySettings keys)
    {
        _overlay.ShortcutKeyDisplayString = KeyNames.Display(keys.ToggleOverlay);
        _edit.ShortcutKeyDisplayString = KeyNames.Display(keys.EditMode);
        _refit.ShortcutKeyDisplayString = KeyNames.Display(keys.DetectCharacter);   // 스킬바 다시 맞추기, which detects too
        _detectSubclass.ShortcutKeyDisplayString = keys.DetectSubclass == "" ? "" : KeyNames.Display(keys.DetectSubclass);
        _rereadIcons.ShortcutKeyDisplayString = keys.RereadIcons == "" ? "" : KeyNames.Display(keys.RereadIcons);
        _test.ShortcutKeyDisplayString = KeyNames.Display(keys.TestMode);
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
    public void SetCharacters(IEnumerable<(string Family, IEnumerable<(string Class, IReadOnlyList<string> Subclasses)> Classes)> tree,
        string? active)
    {
        _character.Text = $"캐릭터: {active ?? "기본"}";
        _character.DropDownItems.Clear();
        var none = new ToolStripMenuItem("기본 (클래스 없음)") { Checked = active is null };
        none.Click += (_, _) => DefaultPicked?.Invoke();
        _character.DropDownItems.Add(none);
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
        _party.Text = $"파티: {status ?? "없음"}";
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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _menu.Dispose();
    }
}
