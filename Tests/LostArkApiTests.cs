using System.Net;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.Tests;

/// <summary>로아 API (lostark-api.plan.md): a pasted key cleaned before it is stored, and what 연결 확인 says for each answer.</summary>
public class LostArkApiTests
{
    const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJpc3MiOiJ0ZXN0In0.c2lnbmF0dXJlXy1f";

    [Theory]
    [InlineData(Jwt)]
    [InlineData("  " + Jwt + "\r\n")]
    [InlineData("bearer " + Jwt)]
    [InlineData("Bearer  " + Jwt)]
    public void A_pasted_key_is_trimmed_and_loses_a_bearer_prefix(string pasted) =>
        Assert.Equal(Jwt, ApiKey.Clean(pasted));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bearer ")]
    [InlineData("eyJ abc")]          // a space inside
    [InlineData("eyJ\"abc")]         // a quote would break the header
    [InlineData("키를붙여넣기")]      // not ASCII
    [InlineData("abcdef")]           // not a JWT: one part
    [InlineData("eyJa.eyJb")]        // two parts
    [InlineData("eyJa..c2ln")]       // an empty part
    [InlineData("eyJa.eyJb.c2l+")]   // '+' is not base64url
    public void Something_that_cannot_be_a_key_is_refused(string pasted) =>
        Assert.Null(ApiKey.Clean(pasted));

    [Fact]
    public void A_key_longer_than_the_vault_holds_is_refused_and_one_at_the_limit_is_kept()
    {
        static string Of(int length) => "eyJa.eyJb." + new string('c', length - 10);
        Assert.Equal(Of(ApiKey.MaxLength), ApiKey.Clean(Of(ApiKey.MaxLength)));
        Assert.Null(ApiKey.Clean(Of(ApiKey.MaxLength + 1)));
    }

    [Fact]
    public async Task An_unexpected_failure_is_a_status_not_an_exception()
    {
        var status = await new LostArkApi(new Throws(new InvalidOperationException("boom"))).CheckAsync(Jwt, CancellationToken.None);
        Assert.Equal(ApiStatus.연결, status);
    }

    sealed class Throws(Exception e) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw e;
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, ApiStatus.정상)]
    [InlineData(HttpStatusCode.Unauthorized, ApiStatus.키오류)]
    [InlineData(HttpStatusCode.Forbidden, ApiStatus.키오류)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiStatus.한도)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiStatus.점검)]
    [InlineData(HttpStatusCode.InternalServerError, ApiStatus.연결)]
    public void Each_answer_has_its_status(HttpStatusCode code, ApiStatus expected) =>
        Assert.Equal(expected, LostArkApi.StatusOf(code));

    [Fact]
    public void Each_status_has_its_own_message_for_the_user() =>
        Assert.Equal(Enum.GetValues<ApiStatus>().Length, Enum.GetValues<ApiStatus>().Select(LostArkApi.Message).Where(m => m.Length > 0).Distinct().Count());

    [Fact]
    public async Task The_check_sends_the_key_as_a_bearer_token_to_the_lost_ark_api()
    {
        var handler = new Answer(HttpStatusCode.OK);
        var status = await new LostArkApi(handler).CheckAsync(Jwt, CancellationToken.None);

        Assert.Equal(ApiStatus.정상, status);
        Assert.Equal("bearer", handler.Seen!.Headers.Authorization!.Scheme);
        Assert.Equal(Jwt, handler.Seen.Headers.Authorization.Parameter);
        Assert.Equal("developer-lostark.game.onstove.com", handler.Seen.RequestUri!.Host);
        Assert.Equal("https", handler.Seen.RequestUri.Scheme);
    }

    [Fact]
    public async Task A_network_failure_is_a_status_not_an_exception()
    {
        var status = await new LostArkApi(new Answer(null)).CheckAsync(Jwt, CancellationToken.None);
        Assert.Equal(ApiStatus.연결, status);
    }

    /// <summary>Answers every request with <paramref name="code"/>, or throws as a dropped connection when null.</summary>
    sealed class Answer(HttpStatusCode? code) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return code is { } c ? Task.FromResult(new HttpResponseMessage(c) { Content = new StringContent("[]") })
                : throw new HttpRequestException("dropped");
        }
    }
}
