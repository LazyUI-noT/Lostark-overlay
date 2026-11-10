using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Core.Domain;

/// <summary>When the 원정대 is looked up (lostark-api.plan.md, user 2026-11-08): on a read of the 다른 캐릭터 바로 접속 menu, the
/// first time in a run and then at most every <see cref="Wait"/>; 새로고침 any time. A failed lookup waits too (no retry on
/// every menu read); a refused key stops menu lookups until it changes. Not saved: a new run starts due.</summary>
public sealed record ApiPace(DateTime? Last = null, bool KeyRefused = false)
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(10);

    public bool Due(DateTime now, bool refresh) =>
        refresh || (!KeyRefused && (Last is not { } last || now - last >= Wait));

    /// <summary>The pace after a lookup that came back as <paramref name="status"/>.</summary>
    public ApiPace After(ApiStatus status, DateTime now) =>
        status == ApiStatus.키오류 ? this with { KeyRefused = true } : new(now, false);

    /// <summary>A new key (저장 or 교체): due again.</summary>
    public ApiPace KeyChanged() => new();
}
