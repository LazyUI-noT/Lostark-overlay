using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.UI;

/// <summary>First start (PLAN.md §6): 키 설정 → 캐릭터 → 스킬창 맞추기. Edits the settings object like 설정 does.</summary>
public partial class FirstRunWindow : Window
{
    readonly Settings _s;
    readonly (string Title, UIElement Content)[] _pages;
    readonly TextBlock _fitResult = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    readonly Button _fit = new() { Content = "맞추기", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 4, 0, 0) };
    int _page;
    bool _fitted;

    /// <summary>Raised after every edit.</summary>
    public event Action? SettingsChanged;

    /// <summary>"맞추기" on the last page.</summary>
    public event Action? FitRequested;

    /// <summary>"완료" / "나중에 맞추기": <see cref="Settings.FirstRunDone"/> is already set.</summary>
    public event Action? Completed;

    public FirstRunWindow(Settings settings, CharacterService characters)
    {
        _s = settings;
        InitializeComponent();
        Themes.Apply(this, settings.Theme, Root);
        void Changed() => SettingsChanged?.Invoke();
        _pages =
        [
            ("키 설정", SettingsWindow.Stack(
                SettingsWindow.Note("게임의 기본 키가 채워져 있습니다. 게임에서 다르게 쓰는 키만 바꾸세요."),
                SettingsWindow.BuildKeyPanel(settings, Changed))),
            ("캐릭터", SettingsWindow.Stack(
                SettingsWindow.Note("지금 플레이할 캐릭터를 고르세요. 캐릭터를 바꾸면 게임 화면의 엠블럼으로 자동 전환되고, 트레이 메뉴에서도 고를 수 있습니다."),
                SettingsWindow.BuildCharacterPanel(settings, characters, Changed))),
            ("스킬창 맞추기", FitPage()),
        ];
        BackButton.Click += (_, _) => Go(_page - 1);
        NextButton.Click += (_, _) =>
        {
            if (_page < _pages.Length - 1) { Go(_page + 1); return; }
            _s.FirstRunDone = true;
            Changed();
            Completed?.Invoke();
            Close();
        };
        Go(0);
    }

    void Go(int page)
    {
        _page = page;
        ShowSteps();
        TitleText.Text = _pages[page].Title;
        Page.Content = _pages[page].Content;
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
        return SettingsWindow.Stack(
            SettingsWindow.Note("오버레이가 게임의 스킬창 위치와 크기를 알아내는 단계입니다."),
            Step("1", "게임에서 수련장에 들어갑니다."),
            Step("2", "수련장에서 쿨초해 모든 스킬이 쿨 돈 상태로 둡니다."),
            Step("3", "아래 [맞추기]를 누릅니다. 게임 화면은 그대로 두세요."),
            fit,
            _fitResult,
            SettingsWindow.Heading("트레이 아이콘"),
            SettingsWindow.Note("GaugeOverlay는 창을 닫아도 작업 표시줄 오른쪽의 알림 영역에서 계속 실행됩니다. 아이콘이 보이지 않으면 시계 옆의 ^ 안에 있습니다. 아이콘을 시계 옆으로 끌어다 놓으면 항상 보입니다."));
    }

    static UIElement Step(string n, string text)
    {
        var g = new Grid { Margin = new(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = n + ".", FontWeight = FontWeights.SemiBold });
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

}
