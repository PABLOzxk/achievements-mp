using System.Net;
using System.Net.Sockets;
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

    [Fact]
    public async Task Redirects_are_not_followed_so_the_key_stays_with_steam()
    {
        // Servidor local que responde com um redirecionamento para outro endereço.
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        using var server = new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        var requests = new List<string>();
        var serving = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await server.GetContextAsync(); } catch (Exception) { return; }
                lock (requests) requests.Add(ctx.Request.Url!.PathAndQuery);
                ctx.Response.StatusCode = 302;
                ctx.Response.RedirectLocation = $"http://127.0.0.1:{port}/outro-site";
                ctx.Response.Close();
            }
        });

        using var http = SteamWebApiClient.CreateHttpClient("teste");
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/api?key={Key}");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        lock (requests) Assert.Equal([$"/api?key={Key}"], requests);
        server.Stop();
        await serving;
    }

    // ----- Família Steam -----

    private const string AccessToken = "eyJhbGciOiJFZERTQSJ9.eyJzdWIiOiI3NjU2MTE5Nzk2MDQzNTUzMCJ9.c2ln";

    [Fact]
    public async Task Recently_played_uses_the_key_and_lists_borrowed_games()
    {
        var (client, handler) = Create(_ => Json(HttpStatusCode.OK, Fixture.Read("recently_played.json")));

        var r = await client.GetRecentlyPlayedGamesAsync(76561197960435530);

        Assert.True(r.IsSuccess);
        var uri = handler.Requests.Single();
        Assert.Equal("/IPlayerService/GetRecentlyPlayedGames/v1/", uri.AbsolutePath);
        Assert.Contains($"key={Key}", uri.Query);
        var hades = r.Value!.Single(g => g.AppId == 1145360);
        Assert.Equal(("Hades", "abc123", 95), (hades.Name, hades.IconHash, hades.PlaytimeMinutes));
    }

    [Fact]
    public async Task Recently_played_with_nothing_recent_is_an_empty_list()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, """{"response":{"total_count":0}}"""));
        var r = await client.GetRecentlyPlayedGamesAsync(76561197960435530);
        Assert.True(r.IsSuccess);
        Assert.Empty(r.Value!);
    }

    [Fact]
    public async Task Family_calls_send_the_token_and_never_the_key()
    {
        var (client, handler) = Create(req => Json(HttpStatusCode.OK, Fixture.Read(
            req.RequestUri!.AbsolutePath.Contains("GetFamilyGroupForUser") ? "family_group.json" : "shared_library.json")));

        var group = await client.GetFamilyGroupIdAsync(AccessToken, 76561197960435530);
        var apps = await client.GetSharedLibraryAppsAsync(AccessToken, group.Value!.Value, 76561197960435530, "brazilian");

        Assert.Equal(9876543210123UL, group.Value);
        Assert.True(apps.IsSuccess);
        Assert.All(handler.Requests, u =>
        {
            Assert.Equal("https", u.Scheme);
            Assert.Equal("api.steampowered.com", u.Host);
            Assert.Contains($"access_token={AccessToken}", u.Query);
            Assert.DoesNotContain(Key, u.Query);
        });
        var shared = handler.Requests[1];
        Assert.Equal("/IFamilyGroupsService/GetSharedLibraryApps/v1/", shared.AbsolutePath);
        Assert.Contains("family_groupid=9876543210123", shared.Query);
        Assert.Contains("include_own=true", shared.Query);

        var hades = apps.Value!.Single(a => a.AppId == 1145360);
        Assert.Equal([76561197960287930UL], hades.OwnerSteamIds);
        Assert.Equal(95, hades.PlaytimeMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1730000000), hades.LastPlayedUtc);
        Assert.Null(apps.Value!.Single(a => a.AppId == 620).LastPlayedUtc);
    }

    [Fact]
    public async Task Not_in_a_family_is_null_not_an_error()
    {
        var (client, _) = Create(_ => Json(HttpStatusCode.OK, Fixture.Read("family_none.json")));
        var r = await client.GetFamilyGroupIdAsync(AccessToken, 76561197960435530);
        Assert.True(r.IsSuccess);
        Assert.Null(r.Value);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Refused_token_is_its_own_error_and_does_not_leak_the_token(int status)
    {
        var (client, _) = Create(_ => Json((HttpStatusCode)status, "<html>Access Denied</html>"));

        var r = await client.GetFamilyGroupIdAsync(AccessToken, 76561197960435530);

        Assert.Equal(SteamErrorKind.InvalidAccessToken, r.Error!.Kind);
        Assert.DoesNotContain(AccessToken, r.Error.Message);
    }

    [Fact]
    public async Task Blank_token_is_refused_without_calling_steam()
    {
        var (client, handler) = Create(_ => throw new InvalidOperationException("não deveria chamar"));
        var r = await client.GetFamilyGroupIdAsync("  ", 76561197960435530);
        Assert.Equal(SteamErrorKind.InvalidAccessToken, r.Error!.Kind);
        Assert.Empty(handler.Requests);
    }
}
