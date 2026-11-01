using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AchievementsMP.Xbox;

/// <summary>
/// Cliente HTTP fino dos serviços do Xbox: monta a chamada e classifica falhas. Não faz retry nem cache (isso é da
/// sincronização). Tokens nunca aparecem em mensagens de erro ou logs.
/// </summary>
public sealed class XboxLiveClient(HttpClient http) : IXboxLiveApi
{
    private static readonly Uri UserAuth = new("https://user.auth.xboxlive.com/user/authenticate");
    private static readonly Uri Xsts = new("https://xsts.auth.xboxlive.com/xsts/authorize");
    private const int PageSize = 1000;
    private const int MaxPages = 20;

    /// <summary>HttpClient do app. Sem seguir redirecionamentos: o token vai no cabeçalho e não deve sair do Xbox.</summary>
    public static HttpClient CreateHttpClient(string userAgent)
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    public async Task<XboxResult<XboxIdentity>> AuthorizeAsync(string microsoftAccessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(microsoftAccessToken))
            return XboxResult<XboxIdentity>.Fail(XboxErrorKind.NotSignedIn, "Conecte o Xbox em Configurações.");

        // Token de app registrado no Entra usa o prefixo "d=".
        var user = await SendAsync(HttpMethod.Post, UserAuth, null, "1", null, new
        {
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftAccessToken },
        }, ct);
        if (!user.IsSuccess)
            return XboxResult<XboxIdentity>.Fail(user.Error!.Kind == XboxErrorKind.Unauthorized
                ? new XboxError(XboxErrorKind.NotSignedIn, "O login da Microsoft venceu. Conecte o Xbox de novo em Configurações.")
                : user.Error);
        var userToken = XboxResponseParser.ParseUserToken(user.Value!.Body);
        if (!userToken.IsSuccess)
            return XboxResult<XboxIdentity>.Fail(userToken.Error!);

        var xsts = await SendAsync(HttpMethod.Post, Xsts, null, "1", null, new
        {
            RelyingParty = "http://xboxlive.com",
            TokenType = "JWT",
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken.Value!.Token } },
        }, ct, bodyOnUnauthorized: true);
        if (!xsts.IsSuccess)
            return XboxResult<XboxIdentity>.Fail(xsts.Error!);
        if (xsts.Value!.Status == HttpStatusCode.Unauthorized)
            return XboxResult<XboxIdentity>.Fail(XboxResponseParser.ParseXstsError(xsts.Value.Body));
        return XboxResponseParser.ParseXsts(xsts.Value.Body);
    }

    public async Task<XboxResult<IReadOnlyList<XboxTitle>>> GetTitleHistoryAsync(XboxIdentity identity, string language, CancellationToken ct = default)
    {
        var url = new Uri($"https://titlehub.xboxlive.com/users/xuid({Xuid(identity)})/titles/titlehistory/decoration/achievement,image");
        var r = await SendAsync(HttpMethod.Get, url, identity, "2", language, null, ct);
        return r.IsSuccess ? XboxResponseParser.ParseTitleHistory(r.Value!.Body) : XboxResult<IReadOnlyList<XboxTitle>>.Fail(r.Error!);
    }

    public async Task<XboxResult<IReadOnlyList<XboxAchievement>>> GetAchievementsAsync(XboxIdentity identity, string titleId, string language, CancellationToken ct = default)
    {
        if (!XboxResponseParser.IsDigits(titleId))
            return XboxResult<IReadOnlyList<XboxAchievement>>.Fail(XboxErrorKind.NotFound, "Jogo do Xbox com identificação inválida.");

        var all = new List<XboxAchievement>();
        string? continuation = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var query = $"titleId={titleId}&maxItems={PageSize}" + (continuation is null ? "" : $"&continuationToken={Uri.EscapeDataString(continuation)}");
            var url = new Uri($"https://achievements.xboxlive.com/users/xuid({Xuid(identity)})/achievements?{query}");
            var r = await SendAsync(HttpMethod.Get, url, identity, "2", language, null, ct);
            if (!r.IsSuccess)
                return XboxResult<IReadOnlyList<XboxAchievement>>.Fail(r.Error!);
            var parsed = XboxResponseParser.ParseAchievements(r.Value!.Body);
            if (!parsed.IsSuccess)
                return XboxResult<IReadOnlyList<XboxAchievement>>.Fail(parsed.Error!);

            all.AddRange(parsed.Value!.Achievements);
            continuation = parsed.Value.ContinuationToken;
            if (continuation is null)
                return XboxResult<IReadOnlyList<XboxAchievement>>.Ok(all.DistinctBy(a => a.Id, StringComparer.Ordinal).ToList());
        }
        return XboxResult<IReadOnlyList<XboxAchievement>>.Fail(XboxErrorKind.InvalidResponse, "O Xbox devolveu páginas demais de conquistas.");
    }

    private static string Xuid(XboxIdentity identity) =>
        XboxResponseParser.IsDigits(identity.Xuid) ? identity.Xuid : throw new ArgumentException("XUID inválido.", nameof(identity));

    private sealed record RawResponse(HttpStatusCode Status, string Body);

    private async Task<XboxResult<RawResponse>> SendAsync(HttpMethod method, Uri url, XboxIdentity? identity, string contractVersion, string? language,
        object? json, CancellationToken ct, bool bodyOnUnauthorized = false)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("x-xbl-contract-version", contractVersion);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation("Authorization", identity.AuthorizationHeader);
        if (!string.IsNullOrEmpty(language))
            request.Headers.AcceptLanguage.ParseAdd(language);
        if (json is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var status = response.StatusCode;
            if (response.IsSuccessStatusCode || (bodyOnUnauthorized && status == HttpStatusCode.Unauthorized))
                return XboxResult<RawResponse>.Ok(new RawResponse(status, body));
            return XboxResult<RawResponse>.Fail(Classify(status, response.Headers.RetryAfter?.Delta));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return XboxResult<RawResponse>.Fail(XboxErrorKind.Cancelled, "Operação cancelada.");
        }
        catch (TaskCanceledException)
        {
            return XboxResult<RawResponse>.Fail(XboxErrorKind.Network, "Tempo limite esgotado ao falar com o Xbox.");
        }
        catch (HttpRequestException ex)
        {
            return XboxResult<RawResponse>.Fail(XboxErrorKind.Network, $"Falha de rede ao falar com o Xbox ({ex.HttpRequestError}).");
        }
    }

    internal static XboxError Classify(HttpStatusCode status, TimeSpan? retryAfter) => (int)status switch
    {
        401 or 403 => new(XboxErrorKind.Unauthorized, $"O Xbox recusou o acesso ({(int)status})."),
        404 => new(XboxErrorKind.NotFound, "Não encontrado no Xbox (404)."),
        429 => new(XboxErrorKind.RateLimited, "Limite de consultas do Xbox atingido (429).", retryAfter ?? TimeSpan.FromMinutes(1)),
        >= 500 => new(XboxErrorKind.Transient, $"O Xbox está com problemas ({(int)status})."),
        _ => new(XboxErrorKind.InvalidResponse, $"Resposta inesperada do Xbox ({(int)status})."),
    };
}
