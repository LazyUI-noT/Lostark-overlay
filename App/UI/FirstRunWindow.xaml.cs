using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>First start (PLAN.md §6): 키 설정 → 캐릭터 → 스킬창 맞추기. Edits the settings object like 설정 does.</summary>
public partial class FirstRunWindow : Window
{
    readonly Settings _s;
    readonly (string Title, UIElement Content)[] _pages;
    readonly TextBlock _fitResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, Tokens.Space.Gap, 0, 0) };
    readonly Button _fit = new() { Content = "맞추기", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, Tokens.Space.Tight, 0, 0) };
    int _page;
    bool _fitted;

    /// <summary>Raised after every edit.</summary>
    public event Action? SettingsChanged;

    /// <summary>"맞추기" on the last page.</summary>
    public event Action? FitRequested;

    /// <summary>The window closed, by "완료", "나중에 맞추기" or ✕: <see cref="Settings.FirstRunDone"/> is already set, so ✕
    /// is 나중에 too and the overlay and the "no fit" notice are no longer held back (ui-audit-2026-11-18, P1).</summary>
    public event Action? Completed;

    /// <summary>A fit worked here (the closing notice then skips how to fit later).</summary>
    public bool Fitted => _fitted;

    /// <param name="fitted">The game's screen has a fit already (<see cref="FitList.HasFit"/>): 시작하기 다시 보기.</param>
    public FirstRunWindow(Settings settings, CharacterService characters, bool fitted)
    {
        _s = settings;
        _fitted = fitted;
        InitializeComponent();
        UiMotion.Follow(settings);
        UiMotion.PressButtons(this);
        Themes.Apply(this, settings.Theme, Root);
        void Changed() => SettingsChanged?.Invoke();
        _pages =
        [
            ("키 설정", Stack(
                // What the app is for, before any chore: primary text, not a muted help line (ui-audit-2026-11-18)
                new TextBlock { Text = "GaugeOverlay는 스킬 쿨타임과 버프를 게임 화면 위에 보여 줍니다. 세 단계만 정하면 됩니다. 창을 닫아도 설정에서 이어서 할 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, Tokens.Space.Block) },
                Note("게임의 기본 키가 채워져 있습니다. 게임에서 다르게 쓰는 키만 바꾸세요. 그대로 쓰면 다음을 누르세요."),
                SettingsWindow.BuildKeyPanel(settings, Changed))),
            ("직업 인식", Stack(
                Note("지금 플레이할 캐릭터를 고르세요. 캐릭터를 바꾸면 스킬창을 다시 맞출 때 GaugeOverlay가 초각스 아이콘으로 알아봅니다. 트레이 메뉴에서 직접 골라도 됩니다."),
                SettingsWindow.BuildCharacterPanel(settings, characters, Changed))),
            ("스킬창 맞추기", FitPage()),
        ];
        BackButton.Click += (_, _) => Go(_page - 1);
        NextButton.Click += (_, _) =>
        {
            if (_page < _pages.Length - 1) Go(_page + 1);
            else Close();
        };
        // ✕ as well; App marks FirstRunDone unless the app is quitting (종료 or logoff while open shows it again next start)
        Closed += (_, _) => Completed?.Invoke();
        Go(0);
    }

    void Go(int page)
    {
        bool newPage = page != _page;   // the same step again (a fit result) is a refill: it stays still
        _page = page;
        ShowSteps();
        TitleText.Text = _pages[page].Title;
        Page.Content = _pages[page].Content;
        if (newPage) UiMotion.Arrive(Page, UiMotion.PageRise, UiMotion.PageTime);
        BackButton.Visibility = page == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = page < _pages.Length - 1 ? "다음" : _fitted ? "완료" : "나중에 맞추기";
        // The step's one action is the accent button: 다음 and 완료, but on the last page 맞추기 until a fit worked.
        bool nextIsMain = page < _pages.Length - 1 || _fitted;
        if (nextIsMain) Themes.Primary(NextButton); else NextButton.ClearValue(StyleProperty);
        if (nextIsMain) _fit.ClearValue(StyleProperty); else Themes.Primary(_fit);
    }

    /// <summary>"1 / 3   키 설정 › 캐릭터 › 스킬창 맞추기", the current step in bold, so the steps ahead are known.</summary>
    void ShowSteps()
    {
        StepText.Inlines.Clear();
        StepText.Inlines.Add(new System.Windows.Documents.Run($"{_page + 1} / {_pages.Length}   "));
        for (int i = 0; i < _pages.Length; i++)
        {
            if (i > 0) StepText.Inlines.Add(new System.Windows.Documents.Run(" › "));
            StepText.Inlines.Add(new System.Windows.Documents.Run(_pages[i].Title) { FontWeight = i == _page ? FontWeights.SemiBold : FontWeights.Normal });
        }
    }

    /// <summary>Shows a fit result on the last page; after a successful fit the button reads "완료".</summary>
    public void SetFitResult(bool ok, string text)
    {
        _fitted |= ok;
        _fitResult.Text = text;
        Go(_page);
    }

    UIElement FitPage()
    {
        var fit = _fit;
        fit.Click += (_, _) => FitRequested?.Invoke();
        return Stack(
            Note("오버레이가 게임의 스킬창 위치와 크기를 알아냅니다."),
            Step("1", "로스트아크에 접속해 스킬을 쓸 수 있는 곳에서 모든 스킬의 쿨타임이 끝난 상태로 두세요 (수련장에서는 쿨초)."),
            Step("2", "아래 [맞추기]를 누릅니다. 게임 화면은 그대로 두세요."),
            fit,
            _fitResult,
            Heading("트레이 아이콘"),
            Note("GaugeOverlay는 창을 닫아도 작업 표시줄 오른쪽의 알림 영역에서 계속 실행됩니다. 아이콘이 보이지 않으면 시계 옆의 ^ 안에 있습니다. 아이콘을 시계 옆으로 끌어다 놓으면 항상 보입니다."));
    }

    static UIElement Step(string n, string text)
    {
        var g = new Grid { Margin = new(0, 0, 0, Tokens.Space.Block) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = n + ".", FontWeight = FontWeights.SemiBold });
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

}
