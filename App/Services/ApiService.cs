using System.Diagnostics;
using System.Net.Http;
using System.Windows.Threading;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.App.Services;

/// <summary>The 원정대 lookup (lostark-api.plan.md): on a 게임 메뉴 read when <see cref="ApiPace"/> says so, or on 새로고침. The played
/// character's armory gives the server; its siblings there and each one's armory follow, one at a time, off the UI thread. The
/// answers land on the UI thread: 전투력 into <see cref="Settings.CombatPower"/> (best kept), the 원정대 into 숙제. The key is
/// read from the vault per lookup and goes nowhere else; with no key nothing happens.</summary>
public sealed class ApiService(Settings settings, HomeworkService homework, Dispatcher ui, Action settingsChanged)
{
    static readonly LostArkApi Api = new(new HttpClientHandler { AllowAutoRedirect = false });   // the bearer key goes to the API host only

    ApiPace _pace = new();
    int _busy, _keyGeneration;

    /// <summary>The last lookup: when, how it came back, how many characters; null before the first.</summary>
    public (DateTime At, ApiStatus Status, int Characters)? Last { get; private set; }

    /// <summary>The played character's armory from the last lookup (설정's profile).</summary>
    public ApiProfile? Played { get; private set; }

    /// <summary>Each character's item level from this run's lookups, saved with its fights (<see cref="RecordPower.Stamp"/>).
    /// Replaced, never changed: the save thread reads it.</summary>
    public IReadOnlyDictionary<string, double> Levels { get; private set; } = new Dictionary<string, double>();

    /// <summary>After a lookup was applied (UI thread).</summary>
    public event Action? Updated;

    public bool Busy => Volatile.Read(ref _busy) == 1;

    /// <summary>Looks up when due (<paramref name="refresh"/>: 새로고침, now). UI thread.</summary>
    public void Lookup(string? character, bool refresh)
    {
        if (ApiRoster.Root(settings.ApiMainCharacter, character) is not { } root || !_pace.Due(DateTime.UtcNow, refresh)) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Run(root, string.IsNullOrEmpty(character) ? root : character);
    }

    /// <summary>A new key or 대표 캐릭터 (saved or removed): due again, and a lookup still running with the old one is dropped (an
    /// old key's 키오류 must not mark the new one refused).</summary>
    public void KeyChanged() => (_pace, _keyGeneration) = (_pace.KeyChanged(), _keyGeneration + 1);

    /// <summary>The 원정대 from <paramref name="root"/>; <paramref name="played"/>'s profile is the one 설정 shows.</summary>
    async Task Run(string root, string played)
    {
        int generation = _keyGeneration;   // UI thread, before the first await
        string character = played;
        (ApiStatus, IReadOnlyList<ApiCharacter>, IReadOnlyList<ApiProfile>)? answer = null;
        try
        {
            if (ApiKey.Clean(CredentialVault.Read()) is not { } key) return;   // no key: the pace stays due, Updated re-enables 새로고침
            answer = await Fetch(key, root).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.WriteLine($"[api] lookup failed: {e.GetType().Name}");   // the type only: a message could carry request details
            answer = (ApiStatus.연결, [], []);   // paced like any failed lookup: no retry on every menu read
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
            _ = ui.BeginInvoke(() =>
            {
                try
                {
                    if (generation != _keyGeneration) Lookup(character, refresh: true);   // superseded (new key or 대표 캐릭터): look up again now
                    else if (answer is var (s, r, p)) Apply(character, s, r, p);
                }
                catch (Exception e) { Trace.WriteLine($"[api] apply failed: {e.GetType().Name}"); }
                Updated?.Invoke();
            });
        }
    }

    /// <summary>The played character first (for its server), then the others on that server; a refused or limited answer stops
    /// the rest (the next due lookup tries again).</summary>
    static async Task<(ApiStatus, IReadOnlyList<ApiCharacter>, IReadOnlyList<ApiProfile>)> Fetch(string key, string character)
    {
        var (status, me) = await Api.ArmoryAsync(key, character, CancellationToken.None).ConfigureAwait(false);
        if (status != ApiStatus.정상 || me is null) return (status, [], []);
        var (rosterStatus, roster) = await Api.SiblingsAsync(key, character, me.Server, CancellationToken.None).ConfigureAwait(false);
        var profiles = new List<ApiProfile> { me };
        if (rosterStatus != ApiStatus.정상) return (rosterStatus, [], profiles);
        foreach (var c in roster.Where(c => c.Name != me.Name))
        {
            var (s, p) = await Api.ArmoryAsync(key, c.Name, CancellationToken.None).ConfigureAwait(false);
            if (s != ApiStatus.정상) return (s, roster, profiles);
            if (p is not null) profiles.Add(p);
        }
        return (ApiStatus.정상, roster, profiles);
    }

    void Apply(string character, ApiStatus status, IReadOnlyList<ApiCharacter> roster, IReadOnlyList<ApiProfile> profiles)
    {
        var now = DateTime.UtcNow;
        // a misread screen name: the next menu read may read it right (a typed 대표 캐릭터 won't change, so it waits like a failure)
        bool unknown = status == ApiStatus.정상 && profiles.Count == 0 && string.IsNullOrWhiteSpace(settings.ApiMainCharacter);
        if (!unknown) _pace = _pace.After(status, now);
        Last = (now, status, roster.Count);
        Played = profiles.FirstOrDefault(p => p.Name == character) ?? (Played?.Name == character ? Played : null);
        var book = settings.CombatPower;
        foreach (var p in profiles) book = CombatPowerBook.Merge(book, p);
        settings.CombatPower = book;   // a new dictionary: the save thread reads it
        if (profiles.Count > 0)
        {
            var levels = new Dictionary<string, double>(Levels);
            foreach (var p in profiles) levels[p.Name] = p.ItemLevel;
            Levels = levels;   // a new dictionary: the save thread reads it
        }
        if (roster.Count > 0) homework.FromApi(roster, profiles);
        settingsChanged();
        Trace.WriteLine($"[api] lookup {status}: {roster.Count} characters, {profiles.Count(p => p.CombatPower > 0)} with 전투력");
    }
}
