using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Tests;

/// <summary>When the 원정대 is looked up (user, 2026-11-08): when the 다른 캐릭터 바로 접속 menu is read, the first time in a run
/// and then at most every 10 minutes; 새로고침 any time; a refused key waits until it changes.</summary>
public class ApiPaceTests
{
    static readonly DateTime T0 = new(2026, 11, 8, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_menu_read_of_a_run_looks_up() =>
        Assert.True(new ApiPace().Due(T0, refresh: false));

    [Fact]
    public void After_a_lookup_the_menu_waits_ten_minutes()
    {
        var pace = new ApiPace().After(ApiStatus.정상, T0);
        Assert.False(pace.Due(T0 + ApiPace.Wait - TimeSpan.FromSeconds(1), refresh: false));
        Assert.True(pace.Due(T0 + ApiPace.Wait, refresh: false));
    }

    [Fact]
    public void Refresh_skips_the_wait() =>
        Assert.True(new ApiPace().After(ApiStatus.정상, T0).Due(T0 + TimeSpan.FromSeconds(5), refresh: true));

    [Theory]
    [InlineData(ApiStatus.한도)]
    [InlineData(ApiStatus.점검)]
    [InlineData(ApiStatus.연결)]
    public void A_failed_lookup_also_waits_rather_than_retrying_on_every_menu(ApiStatus status) =>
        Assert.False(new ApiPace().After(status, T0).Due(T0 + TimeSpan.FromMinutes(1), refresh: false));

    [Fact]
    public void A_refused_key_stops_menu_lookups_until_the_key_changes()
    {
        var pace = new ApiPace().After(ApiStatus.키오류, T0);
        Assert.False(pace.Due(T0 + TimeSpan.FromHours(1), refresh: false));
        Assert.True(pace.Due(T0 + TimeSpan.FromHours(1), refresh: true));   // 새로고침 tries again
        Assert.True(pace.KeyChanged().Due(T0 + TimeSpan.FromSeconds(1), refresh: false));
    }
}
