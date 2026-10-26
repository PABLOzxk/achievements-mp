using System.Net;
using AchievementsMP.Core.Domain;

namespace AchievementsMP.Steam.Http;

/// <summary>
/// Cliente HTTP fino: monta a URL, faz a chamada e classifica falhas. Não faz retry nem cache (isso é da sincronização, Fase 2).
/// A chave nunca aparece em mensagens de erro ou logs.
/// </summary>
public sealed class SteamWebApiClient(HttpClient http, Func<CancellationToken, Task<string?>> apiKeyProvider) : ISteamWebApi
{
    public static readonly Uri BaseAddress = new("https://api.steampowered.com/");

    /// <summary>
    /// HttpClient do app. Sem seguir redirecionamentos: a chave vai na URL, e um redirecionamento a levaria junto
    /// para outro endereço. A Steam Web API não redireciona; se um dia redirecionar, vira "resposta inesperada".
    /// </summary>
    public static HttpClient CreateHttpClient(string userAgent)
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return http;
    }

    public async Task<SteamResult<ulong>> ResolveVanityUrlAsync(string vanityName, CancellationToken ct = default)
    {
        var r = await GetAsync("ISteamUser/ResolveVanityURL/v1/", new() { ["vanityurl"] = vanityName }, requiresKey: true, ct);
        return r.IsSuccess ? SteamResponseParser.ParseVanity(r.Value!.Body) : SteamResult<ulong>.Fail(r.Error!);
    }

    public async Task<SteamResult<PlayerSummary>> GetPlayerSummaryAsync(ulong steamId, CancellationToken ct = default)
    {
        var r = await GetAsync("ISteamUser/GetPlayerSummaries/v2/", new() { ["steamids"] = steamId.ToString() }, requiresKey: true, ct);
        return r.IsSuccess ? SteamResponseParser.ParsePlayerSummary(steamId, r.Value!.Body) : SteamResult<PlayerSummary>.Fail(r.Error!);
    }

    public async Task<SteamResult<OwnedGames>> GetOwnedGamesAsync(ulong steamId, CancellationToken ct = default)
    {
        var r = await GetAsync("IPlayerService/GetOwnedGames/v1/", new()
        {
            ["steamid"] = steamId.ToString(),
            ["include_appinfo"] = "true",
            ["include_played_free_games"] = "true",
        }, requiresKey: true, ct);
        return r.IsSuccess ? SteamResponseParser.ParseOwnedGames(r.Value!.Body) : SteamResult<OwnedGames>.Fail(r.Error!);
    }

    public async Task<SteamResult<IReadOnlyList<AchievementDefinition>>> GetSchemaForGameAsync(long appId, string language, CancellationToken ct = default)
    {
        var r = await GetAsync("ISteamUserStats/GetSchemaForGame/v2/", new() { ["appid"] = appId.ToString(), ["l"] = language }, requiresKey: true, ct);
        return r.IsSuccess ? SteamResponseParser.ParseSchema(appId, r.Value!.Body) : SteamResult<IReadOnlyList<AchievementDefinition>>.Fail(r.Error!);
    }

    public async Task<SteamResult<IReadOnlyList<PlayerAchievementState>>> GetPlayerAchievementsAsync(ulong steamId, long appId, CancellationToken ct = default)
    {
        var r = await GetAsync("ISteamUserStats/GetPlayerAchievements/v1/", new() { ["steamid"] = steamId.ToString(), ["appid"] = appId.ToString() },
            requiresKey: true, ct, bodyOnClientError: true);
        // Esta rota responde 400/403 com corpo útil ("Profile is not public", "Requested app has no stats").
        return r.IsSuccess ? SteamResponseParser.ParsePlayerAchievements(appId, r.Value!.Body) : SteamResult<IReadOnlyList<PlayerAchievementState>>.Fail(r.Error!);
    }

    public async Task<SteamResult<IReadOnlyDictionary<string, double>>> GetGlobalAchievementPercentagesAsync(long appId, CancellationToken ct = default)
    {
        var r = await GetAsync("ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/", new() { ["gameid"] = appId.ToString() }, requiresKey: false, ct);
        return r.IsSuccess ? SteamResponseParser.ParseGlobalPercentages(r.Value!.Body) : SteamResult<IReadOnlyDictionary<string, double>>.Fail(r.Error!);
    }

    private sealed record RawResponse(HttpStatusCode Status, string Body);

    private async Task<SteamResult<RawResponse>> GetAsync(string path, Dictionary<string, string> query, bool requiresKey, CancellationToken ct, bool bodyOnClientError = false)
    {
        if (requiresKey)
        {
            var key = await apiKeyProvider(ct);
            if (string.IsNullOrWhiteSpace(key))
                return SteamResult<RawResponse>.Fail(SteamErrorKind.InvalidApiKey, "Chave da Steam Web API não configurada.");
            query["key"] = key;
        }
        query["format"] = "json";

        var url = new Uri(BaseAddress, path + "?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")));
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = response.StatusCode;

            if (response.IsSuccessStatusCode || (bodyOnClientError && (int)status is 400 or 403 && body.Contains("playerstats", StringComparison.Ordinal)))
                return SteamResult<RawResponse>.Ok(new RawResponse(status, body));

            return SteamResult<RawResponse>.Fail(Classify(status, response.Headers.RetryAfter?.Delta));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SteamResult<RawResponse>.Fail(SteamErrorKind.Cancelled, "Operação cancelada.");
        }
        catch (TaskCanceledException)
        {
            return SteamResult<RawResponse>.Fail(SteamErrorKind.Network, "Tempo limite esgotado ao falar com a Steam.");
        }
        catch (HttpRequestException ex)
        {
            // ex.Message pode conter a URL (com a chave) em alguns runtimes; não repassamos.
            return SteamResult<RawResponse>.Fail(SteamErrorKind.Network, $"Falha de rede ao falar com a Steam ({ex.HttpRequestError}).");
        }
    }

    internal static SteamError Classify(HttpStatusCode status, TimeSpan? retryAfter) => (int)status switch
    {
        401 or 403 => new(SteamErrorKind.InvalidApiKey, "A Steam recusou a chave da API (401/403)."),
        404 => new(SteamErrorKind.NotFound, "Recurso não encontrado na Steam (404)."),
        429 => new(SteamErrorKind.RateLimited, "Limite de consultas da Steam atingido (429).", retryAfter ?? TimeSpan.FromMinutes(5)),
        >= 500 => new(SteamErrorKind.Transient, $"A Steam está com problemas ({(int)status})."),
        _ => new(SteamErrorKind.InvalidResponse, $"Resposta inesperada da Steam ({(int)status})."),
    };
}
