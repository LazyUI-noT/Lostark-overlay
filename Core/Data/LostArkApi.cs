using System.Net;
using System.Net.Http.Headers;

namespace GaugeOverlay.Core.Data;

/// <summary>What a call to the Lost Ark Open API came back as (lostark-api.plan.md): 정상, the key refused (키오류), the
/// per-key limit hit (한도), the server down for 점검, or no usable answer (연결).</summary>
public enum ApiStatus { 정상, 키오류, 한도, 점검, 연결 }

/// <summary>The user's Lost Ark Open API key as pasted (developer-lostark.game.onstove.com): cleaned before it is stored.</summary>
public static class ApiKey
{
    /// <summary>The most Windows Credential Manager holds: 2560 bytes of blob, the key as UTF-16 (real keys are a few hundred).</summary>
    public const int MaxLength = 1280;

    const string Bearer = "bearer ";

    /// <summary>The key without surrounding blanks or a pasted "bearer " prefix; null when it isn't JWT-shaped (three non-empty
    /// base64url parts joined by '.') or is too long, so nothing unusable is stored or put in a header.</summary>
    public static string? Clean(string? pasted)
    {
        var key = (pasted ?? "").Trim();
        if (key.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase)) key = key[Bearer.Length..].Trim();
        if (key.Length > MaxLength) return null;
        var parts = key.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0 && p.All(IsBase64Url)) ? key : null;
    }

    static bool IsBase64Url(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
}

/// <summary>The Lost Ark Open API client. The key is passed per call and never kept, logged or put in an exception: callers
/// read it from the Windows vault when they need it. Every failure comes back as an <see cref="ApiStatus"/>, never thrown.</summary>
public sealed partial class LostArkApi(HttpMessageHandler handler)
{
    public const string Host = "https://developer-lostark.game.onstove.com";

    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>A whole armory is about 80 KB (2026-11-08); a far larger answer is refused rather than read.</summary>
    const int MaxAnswer = 1 << 20;

    readonly HttpClient _http = new(handler, disposeHandler: false)
        { BaseAddress = new Uri(Host), Timeout = RequestTimeout, MaxResponseContentBufferSize = MaxAnswer };

    /// <summary>The status a response code means.</summary>
    public static ApiStatus StatusOf(HttpStatusCode code) => code switch
    {
        HttpStatusCode.OK => ApiStatus.정상,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApiStatus.키오류,
        HttpStatusCode.TooManyRequests => ApiStatus.한도,
        HttpStatusCode.ServiceUnavailable => ApiStatus.점검,
        _ => ApiStatus.연결,
    };

    /// <summary>What 설정 → 로아 API says for a status.</summary>
    public static string Message(ApiStatus status) => status switch
    {
        ApiStatus.정상 => "연결되었습니다.",
        ApiStatus.키오류 => "API 키가 맞지 않거나 만료되었습니다.",
        ApiStatus.한도 => "요청 한도(분당 100회)를 넘었습니다. 잠시 뒤 다시 해 주세요.",
        ApiStatus.점검 => "로스트아크 API가 점검 중입니다.",
        _ => "서버에 연결하지 못했습니다. 인터넷 연결을 확인해 주세요.",
    };

    /// <summary>연결 확인: one small call that needs a valid key and no character name (the game's notices).</summary>
    public async Task<ApiStatus> CheckAsync(string key, CancellationToken ct) => (await GetAsync(key, "/news/notices", ct)).Status;

    /// <summary>The 원정대 on <paramref name="server"/> of the account that owns <paramref name="name"/>.</summary>
    public async Task<(ApiStatus Status, IReadOnlyList<ApiCharacter> Characters)> SiblingsAsync(string key, string name, string server, CancellationToken ct)
    {
        var (status, body) = await GetAsync(key, $"/characters/{Uri.EscapeDataString(name)}/siblings", ct);
        return (status, body is null ? [] : ParseSiblings(body, server));
    }

    /// <summary>One character's armory: profile (전투력, item level, stats), 각인 and ark passive (the 직업각인), in one call.</summary>
    public async Task<(ApiStatus Status, ApiProfile? Profile)> ArmoryAsync(string key, string name, CancellationToken ct)
    {
        var (status, body) = await GetAsync(key, $"/armories/characters/{Uri.EscapeDataString(name)}?filters=profiles%2Bengravings%2Barkpassive", ct);
        return (status, body is null ? null : ParseArmory(body));
    }

    /// <summary>A GET with the key as a bearer token: the status, and the body when it is 정상.</summary>
    async Task<(ApiStatus Status, string? Body)> GetAsync(string key, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await _http.SendAsync(request, ct);   // buffered, capped at MaxAnswer
            var status = StatusOf(response.StatusCode);
            return (status, status == ApiStatus.정상 ? await response.Content.ReadAsStringAsync(ct) : null);
        }
        catch (Exception)
        {
            // Offline, DNS, timeout, an answer over the cap, or anything else: callers are click handlers and the engine, so
            // nothing may escape. The exception is dropped, not logged: its message could carry request details.
            return (ApiStatus.연결, null);
        }
    }
}
