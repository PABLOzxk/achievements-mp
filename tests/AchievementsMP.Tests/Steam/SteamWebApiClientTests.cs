using System.Net;
using AchievementsMP.Steam;
using AchievementsMP.Steam.Http;

namespace AchievementsMP.Tests.Steam;

public class SteamWebApiClientTests
{
    private const string Key = "SECRETKEY0123456789";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private static (SteamWebApiClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = Key)
    {
        var handler = new StubHandler(respond);
        return (new SteamWebApiClient(new HttpClient(handler), _ => Task.FromResult(key)), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task Missing_key_fails_without_calling_steam()
    {
        var (client, handler) = Create(_ => throw new InvalidOperationException("não deveria chamar"), key: null);

        var r = await client.GetOwnedGamesAsync(76561197960435530);

        Assert.Equal(SteamErrorKind.InvalidApiKey, r.Error!.Kind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Builds_expected_owned_games_request()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, Fixture.Read("owned_games.json")));

        var r = await client.GetOwnedGamesAsync(76561197960435530);

        Assert.True(r.IsSuccess);
        var uri = handler.Requests.Single();
        Assert.Equal("/IPlayerService/GetOwnedGames/v1/", uri.AbsolutePath);
        Assert.Contains("include_appinfo=true", uri.Query);
        Assert.Contains("steamid=76561197960435530", uri.Query);
    }

    [Fact]
    public async Task Global_percentages_do_not_send_the_key()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, Fixture.Read("global_percentages.json")));
        await client.GetGlobalAchievementPercentagesAsync(620);
        Assert.DoesNotContain(Key, handler.Requests.Single().Query);
    }

    [Fact]
    public async Task Rate_limit_is_retryable_and_carries_retry_after()
    {
        var (client, _) = Create(_ =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)429);
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return resp;
        });

        var r = await client.GetSchemaForGameAsync(620, "brazilian");

        Assert.Equal(SteamErrorKind.RateLimited, r.Error!.Kind);
        Assert.True(r.Error.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(30), r.Error.RetryAfter);
    }

    [Fact]
    public async Task Private_profile_403_on_player_achievements_is_read_from_body()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Forbidden, Fixture.Read("player_achievements_private.json")));
        var r = await client.GetPlayerAchievementsAsync(76561197960435530, 620);
        Assert.Equal(SteamErrorKind.PrivateProfile, r.Error!.Kind);
    }

    [Fact]
    public async Task Plain_403_means_bad_key()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.Forbidden, "<html>Forbidden</html>"));
        var r = await client.GetOwnedGamesAsync(76561197960435530);
        Assert.Equal(SteamErrorKind.InvalidApiKey, r.Error!.Kind);
    }

    [Fact]
    public async Task Network_errors_never_leak_the_key()
    {
        var (client, _) = Create(req => throw new HttpRequestException($"falhou {req.RequestUri}"));
        var r = await client.GetOwnedGamesAsync(76561197960435530);
        Assert.Equal(SteamErrorKind.Network, r.Error!.Kind);
        Assert.DoesNotContain(Key, r.Error.Message);
    }

    [Fact]
    public async Task Cancellation_is_reported_as_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var (client, _) = Create(_ => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return null!; });
        var r = await client.GetOwnedGamesAsync(76561197960435530, cts.Token);
        Assert.Equal(SteamErrorKind.Cancelled, r.Error!.Kind);
    }
}
