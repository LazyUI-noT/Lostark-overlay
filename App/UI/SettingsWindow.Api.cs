using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

using static GaugeOverlay.App.UI.UiBuild;

namespace GaugeOverlay.App.UI;

/// <summary>설정 → 로아 API (lostark-api.plan.md, user 2026-11-08): paste the key once into a masked box; afterwards only "저장됨"
/// with 연결 확인, 교체 and 삭제. The key goes to Windows Credential Manager, never to settings or the log, and is never shown.</summary>
public partial class SettingsWindow
{
    static readonly LostArkApi Api = new(new HttpClientHandler { AllowAutoRedirect = false });   // the bearer key goes to the API host only
    bool _apiReplacing;
    string _apiStatus = "";

    void FillApi()
    {
        var p = Refill(_secApi, AreaApi, "로스트아크 오픈 API 키를 넣으면 캐릭터 정보(원정대, 아이템 레벨, 전투력)를 서버에서 직접 받아 옵니다. 키는 내 PC에만 둡니다.");
        if (CredentialVault.Has() && !_apiReplacing) ApiSavedRows(p);
        else ApiEntryRows(p);
        if (_apiStatus.Length > 0) p.Children.Add(Note(_apiStatus));
        if (!CredentialVault.Has() || ApiLookups is not { } api) return;
        ApiMainRow(p, api);
        ApiLookupRows(p, api);
        if (api.Played is { } me) ApiProfileRows(p, me);
        else if (api.Last is { Characters: > 0 } && Homework?.Current is { } now)
            p.Children.Add(Note($"지금 캐릭터 '{now}'는 대표 캐릭터의 원정대(같은 서버)에 없어 프로필을 보여 주지 못합니다."));
        p.Children.Add(Row("", Check("게임 메뉴에 전투력 표시", _s.ApiShowCombatPower, v => _s.ApiShowCombatPower = v, Changed,
            "게임 메뉴의 캐릭터 칸과 초상화 옆, 아이템 레벨 오른쪽에 저장된 전투력 중 가장 높은 값을 보여 줍니다. 딜러는 주황, 서포터는 초록입니다.")));
        ApiBookRows(p);
    }

    /// <summary>The 원정대 lookup (null in --demo: no game, no lookups).</summary>
    internal ApiService? ApiLookups { get; set; }

    /// <summary>A lookup was applied: the page again when shown (UI thread).</summary>
    public void ApiUpdated()
    {
        if (_shownArea == AreaApi) FillApi();
        if (_shownArea == AreaHomework) FillHomework();
    }

    void ApiLookupRows(Panel p, ApiService api)
    {
        string last = api.Last is { } l
            ? $"{l.At.ToLocalTime():HH:mm} {(l.Status != ApiStatus.정상 ? LostArkApi.Message(l.Status) : l.Characters > 0 ? $"원정대 {l.Characters}명 받음" : $"'{ApiRoster.Root(_s.ApiMainCharacter, Homework?.Current)}' 캐릭터를 찾지 못했습니다 (대표 캐릭터 이름을 확인하거나 게임 메뉴를 다시 열어 주세요)")}"
            : "아직 없음 (ESC로 게임 메뉴를 열면 받아 옵니다)";
        var refresh = ApiButton("새로고침");
        refresh.IsEnabled = !api.Busy && ApiRoster.Root(_s.ApiMainCharacter, Homework?.Current) is not null;
        refresh.Click += (_, _) => { api.Lookup(Homework?.Current, refresh: true); refresh.IsEnabled = false; };
        p.Children.Add(Row("마지막 조회", Label(last), refresh));
    }

    /// <summary>The 대표 캐릭터 the 원정대 is looked up from (user, 2026-11-08): typed exactly, so a misread screen name can't pick
    /// the wrong roster or rename a 숙제 character to a look-alike.</summary>
    void ApiMainRow(Panel p, ApiService api)
    {
        var box = new TextBox { Text = _s.ApiMainCharacter ?? "", Width = 160, MaxLength = 24, VerticalContentAlignment = VerticalAlignment.Center };
        var save = ApiButton("저장");
        save.Click += (_, _) =>
        {
            if (ApiRoster.Root(box.Text, null) == _s.ApiMainCharacter) return;   // unchanged: keep the pace and a running lookup
            _s.ApiMainCharacter = ApiRoster.Root(box.Text, null);
            Changed();
            api.KeyChanged();
            api.Lookup(Homework?.Current, refresh: true);
            FillApi();
        };
        p.Children.Add(Row("대표 캐릭터", box, save));
        p.Children.Add(Note("원정대를 이 캐릭터 이름으로 찾습니다 (정확히 입력). 비우면 게임 메뉴에서 읽은 지금 캐릭터로 찾습니다."));
    }

    void ApiProfileRows(Panel p, ApiProfile me)
    {
        p.Children.Add(Row("캐릭터", Label($"{me.Name} · {me.Server} · {me.ClassName}{(me.Subclass is { } s ? $" ({s})" : "")}")));
        p.Children.Add(Row("아이템 레벨", Label($"{me.ItemLevel:#,0.00}{(me.CombatPower is { } cp ? $"   전투력 {cp:#,0.00}" : "")}")));
        if (me.Engravings.Count > 0)
            p.Children.Add(Row("각인", Label(string.Join(", ", me.Engravings.Select(e => $"{e.Name} {e.Grade} {e.Level}{(e.Stone is { } st ? $" (돌 {st})" : "")}")))));
        if (me.Stats.Count > 0)
            p.Children.Add(Row("특성", Label(string.Join(", ", me.Stats.Select(t => $"{t.Type} {t.Value:#,0}")))));
    }

    /// <summary>Every stored 전투력 (best per character × 직업각인) with its own 삭제.</summary>
    void ApiBookRows(Panel p)
    {
        if (_s.CombatPower.Count == 0) return;
        p.Children.Add(Note("저장된 전투력 (직업각인마다 가장 높은 값을 둡니다. 잘못 남은 값은 삭제하세요)"));
        foreach (var (name, subs) in _s.CombatPower.OrderBy(c => c.Key))
            foreach (var (sub, value) in subs.OrderByDescending(s => s.Value))
            {
                var delete = ApiButton("삭제");
                delete.Click += (_, _) =>
                {
                    _s.CombatPower = CombatPowerBook.Delete(_s.CombatPower, name, sub);
                    Changed();
                    FillApi();
                };
                p.Children.Add(Row(name, Label($"{(sub.Length > 0 ? sub : "직업각인 없음")}  {value:#,0.00}"), delete));
            }
    }

    void ApiSavedRows(Panel p)
    {
        p.Children.Add(Row("API 키", Label("저장됨 (Windows 자격 증명 관리자)")));
        var check = ApiButton("연결 확인");
        check.Click += async (_, _) => await Guarded(async () =>
        {
            check.IsEnabled = false;
            if (ApiKey.Clean(CredentialVault.Read()) is not { } key) { _apiStatus = "저장된 키가 없거나 키 형식이 아닙니다. 교체로 다시 넣어 주세요."; return; }
            var status = await Api.CheckAsync(key, CancellationToken.None);
            Trace.WriteLine($"[api] check: {status}");
            _apiStatus = LostArkApi.Message(status);
        });
        var replace = ApiButton("교체");
        replace.Click += (_, _) => { (_apiReplacing, _apiStatus) = (true, ""); FillApi(); };
        var delete = ApiButton("삭제");
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "저장한 API 키를 지울까요? 다시 쓰려면 키를 새로 붙여 넣어야 합니다.", AreaApi, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            bool gone = CredentialVault.Delete();
            ApiLookups?.KeyChanged();
            Trace.WriteLine($"[api] key removed: {gone}");
            _apiStatus = gone ? "API 키를 지웠습니다." : "API 키를 지우지 못했습니다. Windows 자격 증명 관리자에서 직접 지울 수 있습니다.";
            FillApi();
        };
        p.Children.Add(Row("관리", check, replace, delete));
    }

    void ApiEntryRows(Panel p)
    {
        var box = new PasswordBox { Width = 320 };
        var save = ApiButton("저장");
        save.Click += async (_, _) => await Guarded(() => SaveApiKey(box, save));
        var controls = new List<UIElement> { box, save };
        if (_apiReplacing)
        {
            var cancel = ApiButton("취소");
            cancel.Click += (_, _) => { (_apiReplacing, _apiStatus) = (false, ""); FillApi(); };
            controls.Add(cancel);
        }
        p.Children.Add(Row("API 키", [.. controls]));
        p.Children.Add(Note("developer-lostark.game.onstove.com 에 스토브 계정으로 로그인해 클라이언트를 만들고, 받은 API 키를 복사해 붙여 넣으세요. " +
            "키는 이 PC의 Windows 자격 증명 관리자에 암호화해 두고 다시 보여 주지 않습니다. 설정 파일과 로그에도 남기지 않습니다."));
    }

    /// <summary>Checks the pasted key once, then stores it unless the server refused it (offline or 점검 still stores: it may
    /// be right). The box is cleared either way.</summary>
    async Task SaveApiKey(PasswordBox box, Button save)
    {
        var key = ApiKey.Clean(box.Password);
        box.Clear();
        if (key is null) { _apiStatus = "API 키 형식이 아닙니다. 사이트에서 복사한 키를 그대로 붙여 넣어 주세요."; return; }
        save.IsEnabled = false;
        var status = await Api.CheckAsync(key, CancellationToken.None);
        if (status == ApiStatus.키오류) _apiStatus = LostArkApi.Message(status) + " 저장하지 않았습니다.";
        else if (!CredentialVault.Save(key)) _apiStatus = "Windows 자격 증명 관리자에 저장하지 못했습니다.";
        else
        {
            _apiReplacing = false;
            ApiLookups?.KeyChanged();
            _apiStatus = "저장했습니다. " + LostArkApi.Message(status);
            Trace.WriteLine($"[api] key saved; check: {status}");
            ClearClipboardIf(key);
        }
    }

    /// <summary>Runs a button's work; whatever happens, the page is redrawn and nothing escapes the click (an async void
    /// handler would take the app down). Only the exception's type is logged: its message could carry request details.</summary>
    async Task Guarded(Func<Task> work)
    {
        try { await work(); }
        catch (Exception e)
        {
            Trace.WriteLine($"[api] failed: {e.GetType().Name}");
            _apiStatus = LostArkApi.Message(ApiStatus.연결);
        }
        FillApi();
    }

    /// <summary>"Never shown again" covers the clipboard: the pasted key is cleared from it once stored.</summary>
    static void ClearClipboardIf(string key)
    {
        try { if (ApiKey.Clean(Clipboard.GetText()) == key) Clipboard.Clear(); }
        catch (System.Runtime.InteropServices.COMException) { }   // another app holds the clipboard: leave it
    }

    static Button ApiButton(string text) => new() { Content = text, Margin = new(8, 0, 0, 0), Padding = new(10, 2, 10, 2) };
}
