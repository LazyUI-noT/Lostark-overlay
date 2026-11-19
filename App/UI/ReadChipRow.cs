using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>The 기능 맵's top row (rail-split.plan.md 4): one round chip per reader on 화면 인식 설정
/// (<see cref="DetectPlace.Chips"/>), coloured by its 읽는 중 mark, grey while off. A click opens 화면 인식 설정 on that box;
/// the map's boxes keep their marks but no longer link there.</summary>
sealed class ReadChipRow : WrapPanel
{
    const double ChipRadius = 10;   // half the chip's height: a pill
    readonly Action<string> _open;
    string? _drawn;
    readonly Dictionary<string, Border> _faces = [];
    IReadOnlySet<string>? _lit;
    static readonly Brush GoldRing = Frozen(FeatureMapView.GoldColour);
    static readonly System.Windows.Media.Effects.DropShadowEffect Glow = FrozenGlow();
    const double Dimmed = 0.4, RingWidth = 3;

    /// <summary>The Live card for a chip (as a map box's read chip shows), and Live on / off for the hovered one (null: none).</summary>
    public Func<string, UIElement?>? Card { get; init; }
    public Action<string?>? Live { get; init; }
    readonly Popup _card = new() { AllowsTransparency = true, Placement = PlacementMode.Bottom, VerticalOffset = 6, IsHitTestVisible = false };
    readonly DispatcherTimer _cardWait = new() { Interval = TimeSpan.FromMilliseconds(350) };   // as the map's cards
    (string Id, UIElement Chip)? _cardFor;
    string? _liveFor;

    public ReadChipRow(Action<string> open)
    {
        _open = open;
        Margin = new(0, Tokens.Space.Gap, 0, 0);
        _cardWait.Tick += (_, _) => ShowCard();
        Unloaded += (_, _) => HideCard();   // another page: no card left open, Live stopped
    }

    void ShowCard()
    {
        _cardWait.Stop();
        if (_cardFor is not var (id, chip) || Card?.Invoke(id) is not { } card) return;
        (_card.Child, _card.PlacementTarget, _card.IsOpen) = (card, chip, true);
        if (_liveFor != id) Live?.Invoke(_liveFor = id);
    }

    void HideCard()
    {
        _cardWait.Stop();
        _cardFor = null;
        (_card.IsOpen, _card.Child) = (false, null);
        if (_liveFor is not null) Live?.Invoke(_liveFor = null);
    }

    /// <summary>The marks from the engine; the chips are rebuilt only when one changed.</summary>
    public void Set(IReadOnlyDictionary<string, Detection> board)
    {
        var drawn = string.Join(",", DetectPlace.Chips.Select(c => board.GetValueOrDefault(c.Id)?.Mark));
        if (drawn == _drawn) return;
        _drawn = drawn;
        Children.Clear();
        _faces.Clear();
        // Named, so the chips read as links to that page and not as map boxes (critique, 2026-11-17).
        Children.Add(UiBuild.Muted(new TextBlock { Text = "화면 인식 설정", FontSize = Tokens.Font.Hint, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, Tokens.Space.Gap, Tokens.Space.Tight) }));
        foreach (var (id, name) in DetectPlace.Chips) Children.Add(Chip(id, name, board.GetValueOrDefault(id)));
        Light(_lit);
    }

    /// <summary>While a map box is hovered, the chips it reads through ringed in gold and the rest dimmed; null: all plain.</summary>
    public void Light(IReadOnlySet<string>? ids)
    {
        _lit = ids;
        foreach (var (id, face) in _faces)
        {
            bool lit = ids?.Contains(id) == true;
            face.BorderBrush = lit ? GoldRing : Brushes.Transparent;   // the ring's width is always there: no jump in size (user, 2026-11-19)
            face.Effect = lit ? Glow : null;   // a glow, so a lit chip stands apart from a yellow ? chip (user, 2026-11-19)
            face.Opacity = ids is null || lit ? 1 : Dimmed;
        }
    }

    static System.Windows.Media.Effects.DropShadowEffect FrozenGlow()
    {
        var glow = new System.Windows.Media.Effects.DropShadowEffect { Color = FeatureMapView.GoldColour, ShadowDepth = 0, BlurRadius = 12, Opacity = 1 };
        glow.Freeze();
        return glow;
    }

    static Brush Frozen(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    UIElement Chip(string id, string name, Detection? d)
    {
        // 디버프 줄 has no mark of its own: its chip only leads to the box.
        var (mark, colour, state) = id == DetectPlace.DebuffRow ? ("", FeatureMapView.Unmarked, "위치만 맞춤") : FeatureMapView.Look(d);
        var face = new Border
        {
            CornerRadius = new(ChipRadius),
            Padding = new(Tokens.Space.Gap - RingWidth, Tokens.Space.Hair, Tokens.Space.Gap - RingWidth, Tokens.Space.Hair),
            BorderThickness = new(RingWidth), BorderBrush = Brushes.Transparent,
            Background = new SolidColorBrush(colour),
            Child = new TextBlock { Text = mark.Length > 0 ? $"{name} {mark}" : name, FontSize = Tokens.Font.Hint, Foreground = new SolidColorBrush(RaidBadgeView.InkOn(colour)) },
        };
        _faces[id] = face;
        string says = $"{name}: {state}. 누르면 화면 인식 설정으로 이동.";
        var go = UiBuild.Bare<Button>(face, says, $"readchip:{id}");
        go.Margin = new(0, 0, Tokens.Space.Tight, Tokens.Space.Tight);
        go.MouseEnter += (_, _) => { _cardFor = (id, go); _cardWait.Start(); };   // the card says the state: no tooltip over it
        go.MouseLeave += (_, _) => HideCard();
        go.Click += (_, _) => _open(id);
        return go;
    }
}
