namespace GaugeOverlay.Core.Domain;

/// <summary>설정's reset buttons, one per area, each clearing exactly what its label names. Window places and sizes, the
/// plates' colours (the 레이아웃 편집기's), characters, overlay profiles and learned gains are never touched.</summary>
public static class SettingsResets
{
    /// <summary>내 오버레이 → 전체: visibility, opacity, timer, cooldown look, 금색 테두리, 초각기 표시.</summary>
    public static void Display(Settings s)
    {
        var o = s.Overlay;
        s.Overlay = new()
        {
            Left = o.Left, Top = o.Top, Width = o.Width, Height = o.Height, Scale = o.Scale, PlateColor = o.PlateColor,
            LayoutSnap = o.LayoutSnap, LayoutLinkSizes = o.LayoutLinkSizes,
        };
    }

    /// <summary>내 오버레이 → 버프/디버프 → 버프: the buff bars; the buff window's place and size stay.</summary>
    public static void Buffs(Settings s)
    {
        var b = s.Buffs;
        s.Buffs = new() { Left = b.Left, Top = b.Top, Width = b.Width, Height = b.Height };
    }

    /// <summary>내 오버레이 → 기타 → 단심: how it is read and shown; the 직업각인 it is on for, the game's 데미지 폰트 크기 and
    /// the window's place and size stay.</summary>
    public static void Judgment(Settings s)
    {
        var j = s.Judgment;
        s.Judgment = new() { Characters = j.Characters, FontSize = j.FontSize, Left = j.Left, Top = j.Top, Width = j.Width, Height = j.Height };
    }

    /// <summary>내 오버레이 → 버프/디버프 → 디버프: the 디버프 칸 크기; the raid chosen and the window's place and size stay.</summary>
    public static void Debuffs(Settings s) => s.Debuffs.Boxes.Clear();

    /// <summary>내 오버레이 → 아덴 게이지: the gauge style and every 클래스·직업각인's bar.</summary>
    public static void Gauge(Settings s)
    {
        s.Gauge = new();
        s.GaugeBars.Clear();
    }

    /// <summary>Every class's boxes (나 and 파티원) back to fresh; 개별 stays.</summary>
    public static void Classes(Settings s)
    {
        foreach (var c in s.Classes.Values) (c.Mine, c.Members) = (new(), new());
    }

    /// <summary>파티: everything but the party windows' places and plate, and your display name.</summary>
    public static void Party(Settings s)
    {
        var old = s.Party;
        // 신뢰한 기기 is who may come in, not how the party looks: it stays (삭제 or 내보내기 removes a device).
        var p = s.Party = new() { DisplayName = old.DisplayName, Together = old.Together, Left = old.Left, Top = old.Top, PlateColor = old.PlateColor,
            TrustedDevices = old.TrustedDevices };
        for (int i = 0; i < Math.Min(old.ShowOthersBySlot.Count, p.ShowOthersBySlot.Count); i++)
            (p.ShowOthersBySlot[i].Left, p.ShowOthersBySlot[i].Top) = (old.ShowOthersBySlot[i].Left, old.ShowOthersBySlot[i].Top);
    }

    public static void Keys(Settings s) => s.Keys = new Settings().Keys;

    public static void Hotkeys(Settings s) => s.Hotkeys = new();

    /// <summary>캐릭터·인식 → 스킬창: every saved fit.</summary>
    public static void Fits(Settings s) => s.Fits.Clear();

    public static void All(Settings s)
    {
        Display(s);
        Buffs(s);
        Debuffs(s);
        Judgment(s);
        Gauge(s);
        Classes(s);
        Party(s);
        Keys(s);
        Hotkeys(s);
        Fits(s);
    }
}
