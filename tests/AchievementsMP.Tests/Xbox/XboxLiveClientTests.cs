using System.Net;
using AchievementsMP.Xbox;

namespace AchievementsMP.Tests.Xbox;

public class XboxLiveClientTests
{
    private static readonly XboxIdentity Me = new("2533274800000000", "pablordfs", "1234567890", "XSTS-TOKEN", null);

    private sealed record Seen(HttpMethod Method, Uri Url, string? Authorization, string? Contract, string? Language, string? Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new Seen(request.Method, request.RequestUri!,
                request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
                request.Headers.TryGetValues("x-xbl-contract-version", out var v) ? v.Single() : null,
                request.Headers.AcceptLanguage.FirstOrDefault()?.Value,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return respond(request);
        }
    }

    private static (XboxLiveClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new XboxLiveClient(new HttpClient(handler)), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task Authorize_exchanges_microsoft_token_for_xsts_identity()
    {
        var (client, handler) = Create(r => r.RequestUri!.Host == "user.auth.xboxlive.com"
            ? Json(HttpStatusCode.OK, """{"Token":"USER-TOKEN","DisplayClaims":{"xui":[{"uhs":"1234567890"}]}}""")
            : Json(HttpStatusCode.OK, Fixture.Read("xbox_xsts.json")));

        var r = await client.AuthorizeAsync("MSA-TOKEN");

        Assert.True(r.IsSuccess);
        Assert.Equal(("2533274800000000", "pablordfs"), (r.Value!.Xuid, r.Value.Gamertag));
        Assert.Equal("XBL3.0 x=1234567890;XSTS-TOKEN", r.Value.AuthorizationHeader);
        Assert.DoesNotContain("XSTS-TOKEN", r.Value.ToString());
        Assert.Contains("\"RpsTicket\":\"d=MSA-TOKEN\"", handler.Requests[0].Body);
        Assert.Contains("\"UserTokens\":[\"USER-TOKEN\"]", handler.Requests[1].Body);
        Assert.Contains("\"RelyingParty\":\"http://xboxlive.com\"", handler.Requests[1].Body);
        Assert.All(handler.Requests, s => Assert.Null(s.Authorization));
    }

    [Theory]
    [InlineData("2148916233", XboxErrorKind.NoXboxAccount)]
    [InlineData("2148916238", XboxErrorKind.AccountRestricted)]
    [InlineData("1", XboxErrorKind.Unauthorized)]
    public async Task Xsts_refusal_is_explained(string xerr, XboxErrorKind expected)
    {
        var (client, _) = Create(r => r.RequestUri!.Host == "user.auth.xboxlive.com"
            ? Json(HttpStatusCode.OK, """{"Token":"USER-TOKEN","DisplayClaims":{"xui":[{"uhs":"1"}]}}""")
            : Json(HttpStatusCode.Unauthorized, $$"""{"Identity":"0","XErr":{{xerr}},"Message":"","Redirect":"https://start.ui.xboxlive.com/CreateAccount"}"""));

        var r = await client.AuthorizeAsync("MSA-TOKEN");

        Assert.Equal(expected, r.Error!.Kind);
    }

    [Fact]
    public async Task Rejected_microsoft_token_means_sign_in_again()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.Unauthorized, ""));

        var r = await client.AuthorizeAsync("VENCIDO");

        Assert.Equal(XboxErrorKind.NotSignedIn, r.Error!.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Title_history_sends_token_contract_and_language()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, Fixture.Read("xbox_titlehistory.json")));

        var r = await client.GetTitleHistoryAsync(Me, "pt-BR");

        Assert.True(r.IsSuccess);
        var seen = Assert.Single(handler.Requests);
        Assert.Equal("https://titlehub.xboxlive.com/users/xuid(2533274800000000)/titles/titlehistory/decoration/achievement,image", seen.Url.AbsoluteUri);
        Assert.Equal(("XBL3.0 x=1234567890;XSTS-TOKEN", "2", "pt-BR"), (seen.Authorization, seen.Contract, seen.Language));
    }

    [Fact]
    public async Task Achievements_follow_continuation_pages()
    {
        var (client, handler) = Create(r => r.RequestUri!.Query.Contains("continuationToken=")
            ? Json(HttpStatusCode.OK, """{"achievements":[{"id":"9","name":"Nove","progressState":"NotStarted"}],"pagingInfo":{"continuationToken":null}}""")
            : Json(HttpStatusCode.OK, """{"achievements":[{"id":"1","name":"Um","progressState":"Achieved"}],"pagingInfo":{"continuationToken":"abc+/="}}"""));

        var r = await client.GetAchievementsAsync(Me, "1717113201", "pt-BR");

        Assert.Equal(["1", "9"], r.Value!.Select(a => a.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("https://achievements.xboxlive.com/users/xuid(2533274800000000)/achievements?titleId=1717113201&maxItems=1000", handler.Requests[0].Url.AbsoluteUri);
        Assert.Contains("continuationToken=abc%2B%2F%3D", handler.Requests[1].Url.AbsoluteUri);
        Assert.Equal("2", handler.Requests[0].Contract);
    }

    [Fact]
    public async Task Invalid_title_id_is_not_sent()
    {
        var (client, handler) = Create(_ => throw new InvalidOperationException("não deveria chamar"));

        var r = await client.GetAchievementsAsync(Me, "../../users/me", "pt-BR");

        Assert.Equal(XboxErrorKind.NotFound, r.Error!.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rate_limit_carries_retry_after()
    {
        var (client, _) = Create(_ =>
        {
            var response = Json(HttpStatusCode.TooManyRequests, "");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });

        var r = await client.GetTitleHistoryAsync(Me, "pt-BR");

        Assert.Equal(XboxErrorKind.RateLimited, r.Error!.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), r.Error.RetryAfter);
        Assert.True(r.Error.IsRetryable);
    }

    [Fact]
    public async Task Network_failure_does_not_leak_the_token()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("XBL3.0 x=1234567890;XSTS-TOKEN"));

        var r = await client.GetTitleHistoryAsync(Me, "pt-BR");

        Assert.Equal(XboxErrorKind.Network, r.Error!.Kind);
        Assert.DoesNotContain("XSTS-TOKEN", r.Error.Message);
    }
}
