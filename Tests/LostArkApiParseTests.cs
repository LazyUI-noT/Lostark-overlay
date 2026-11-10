using System.Net;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

/// <summary>The Lost Ark API's answers as measured on a live call (2026-11-08, lostark-api.plan.md), with made-up names:
/// numbers come as strings with thousands commas, 원정대 lists every server, the 직업각인 is the ark passive's title.</summary>
public class LostArkApiParseTests
{
    const string Siblings = """
        [{"ServerName":"카마인","CharacterName":"가나다","CharacterLevel":70,"CharacterClassName":"워로드","ItemAvgLevel":"1,791.67"},
         {"ServerName":"카제로스","CharacterName":"부캐","CharacterLevel":10,"CharacterClassName":"환수사","ItemAvgLevel":"50.00"},
         {"ServerName":"카마인","CharacterName":"라마바","CharacterLevel":70,"CharacterClassName":"발키리","ItemAvgLevel":"1,736.67"}]
        """;

    static string Armory(string combatPower = "\"5,004.50\"", string arkPassive = """{"Title":"드레드 로어","IsArkPassive":true}""") => $$"""
        {"ArmoryProfile":{"CharacterName":"가나다","ServerName":"카마인","CharacterClassName":"가디언나이트","ItemAvgLevel":"1,755.00",
           "CombatPower":{{combatPower}},"Stats":[{"Type":"치명","Value":"797"},{"Type":"신속","Value":"1577"}]},
         "ArmoryEngraving":{"ArkPassiveEffects":[{"Name":"원한","Grade":"유물","Level":4,"AbilityStoneLevel":2},{"Name":"예리한 둔기","Grade":"유물","Level":4}]},
         "ArkPassive":{{arkPassive}}}
        """;

    [Fact]
    public void Siblings_keep_only_the_server_asked_for_with_their_item_levels()
    {
        var all = LostArkApi.ParseSiblings(Siblings, "카마인");
        Assert.Equal(["가나다", "라마바"], all.Select(c => c.Name));
        Assert.Equal(1791.67, all[0].ItemLevel, 2);
        Assert.Equal("발키리", all[1].ClassName);
    }

    [Fact]
    public void The_armory_gives_combat_power_item_level_subclass_engravings_and_stats()
    {
        var p = LostArkApi.ParseArmory(Armory())!;
        Assert.Equal(("가나다", "카마인", "가디언나이트"), (p.Name, p.Server, p.ClassName));
        Assert.Equal(1755.0, p.ItemLevel, 2);
        Assert.Equal(5004.5, p.CombatPower!.Value, 2);
        Assert.Equal("드레드 로어", p.Subclass);
        Assert.Equal([new ApiEngraving("원한", "유물", 4, 2), new ApiEngraving("예리한 둔기", "유물", 4, null)], p.Engravings);
        Assert.Equal([new ApiStat("치명", 797), new ApiStat("신속", 1577)], p.Stats);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"-\"")]
    public void A_missing_combat_power_is_null_not_zero(string value) =>
        Assert.Null(LostArkApi.ParseArmory(Armory(combatPower: value))!.CombatPower);

    [Fact]
    public void Without_ark_passive_there_is_no_subclass() =>
        Assert.Null(LostArkApi.ParseArmory(Armory(arkPassive: "null"))!.Subclass);

    [Theory]
    [InlineData("null")]            // an unknown character answers null
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void An_answer_without_a_profile_is_null(string json) =>
        Assert.Null(LostArkApi.ParseArmory(json));

    [Theory]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Siblings_that_cannot_be_read_are_empty(string json) =>
        Assert.Empty(LostArkApi.ParseSiblings(json, "카마인"));

    [Fact]
    public async Task A_lookup_escapes_the_name_into_the_path_and_reads_the_answer()
    {
        var handler = new Answer(HttpStatusCode.OK, Armory());
        var (status, profile) = await new LostArkApi(handler).ArmoryAsync("eyJa.eyJb.c2ln", "가나/다", CancellationToken.None);

        Assert.Equal(ApiStatus.정상, status);
        Assert.Equal("가디언나이트", profile!.ClassName);
        Assert.Equal("/armories/characters/%EA%B0%80%EB%82%98%2F%EB%8B%A4", handler.Seen!.RequestUri!.AbsolutePath);
        Assert.Contains("filters=profiles%2Bengravings%2Barkpassive", handler.Seen.RequestUri.Query);
    }

    [Fact]
    public async Task A_refused_lookup_gives_its_status_and_nothing_else()
    {
        var (status, list) = await new LostArkApi(new Answer(HttpStatusCode.TooManyRequests, "")).SiblingsAsync("eyJa.eyJb.c2ln", "가나다", "카마인", CancellationToken.None);
        Assert.Equal(ApiStatus.한도, status);
        Assert.Empty(list);
    }

    sealed class Answer(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }
}
