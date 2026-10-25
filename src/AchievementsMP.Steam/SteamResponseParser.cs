using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementsMP.Core.Domain;
using AchievementsMP.Steam.Dto;

namespace AchievementsMP.Steam;

/// <summary>Converte JSON da Steam em domínio. Separado do HTTP para ser testado com respostas gravadas.</summary>
internal static class SteamResponseParser
{
    private const string CdnBase = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps";

    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = false,
    };

    public static SteamResult<OwnedGames> ParseOwnedGames(string json) => Parse<OwnedGamesEnvelope, OwnedGames>(json, env =>
    {
        var r = env.Response;
        // Perfil privado (ou "detalhes do jogo" privados) devolve {"response":{}} com HTTP 200.
        if (r is null || (r.GameCount is null && r.Games is null))
            return SteamResult<OwnedGames>.Fail(SteamErrorKind.PrivateProfile, "A Steam não informou a biblioteca. Verifique se o perfil e os detalhes de jogos estão públicos.");

        var games = (r.Games ?? []).Select(g => new Game(
            g.AppId,
            string.IsNullOrWhiteSpace(g.Name) ? $"App {g.AppId}" : g.Name,
            string.IsNullOrEmpty(g.ImgIconUrl) ? null : g.ImgIconUrl,
            g.PlaytimeForever,
            g.RtimeLastPlayed is > 0 ? DateTimeOffset.FromUnixTimeSeconds(g.RtimeLastPlayed.Value) : null,
            g.HasCommunityVisibleStats)).ToList();
        return SteamResult<OwnedGames>.Ok(new OwnedGames(r.GameCount ?? games.Count, games));
    });

    public static SteamResult<IReadOnlyList<AchievementDefinition>> ParseSchema(long appId, string json) =>
        Parse<SchemaEnvelope, IReadOnlyList<AchievementDefinition>>(json, env =>
        {
            // Jogo sem conquistas devolve {"game":{}} ou sem availableGameStats: lista vazia é a resposta correta.
            var list = env.Game?.AvailableGameStats?.Achievements ?? [];
            IReadOnlyList<AchievementDefinition> defs = list
                .Where(a => !string.IsNullOrEmpty(a.Name))
                .Select(a => new AchievementDefinition(
                    appId,
                    a.Name!,
                    string.IsNullOrWhiteSpace(a.DisplayName) ? a.Name! : a.DisplayName,
                    // Conquistas ocultas costumam vir sem descrição.
                    string.IsNullOrWhiteSpace(a.Description) ? null : a.Description,
                    a.Hidden != 0,
                    a.Icon,
                    a.IconGray))
                .ToList();
            return SteamResult<IReadOnlyList<AchievementDefinition>>.Ok(defs);
        });

    public static SteamResult<IReadOnlyList<PlayerAchievementState>> ParsePlayerAchievements(long appId, string json) =>
        Parse<PlayerStatsEnvelope, IReadOnlyList<PlayerAchievementState>>(json, env =>
        {
            var s = env.PlayerStats;
            if (s is null)
                return SteamResult<IReadOnlyList<PlayerAchievementState>>.Fail(SteamErrorKind.InvalidResponse, "Resposta sem 'playerstats'.");

            if (s.Success != true)
                return SteamResult<IReadOnlyList<PlayerAchievementState>>.Fail(ClassifyPlayerStatsError(s.Error), s.Error ?? "A Steam recusou a consulta de conquistas.");

            IReadOnlyList<PlayerAchievementState> states = (s.Achievements ?? [])
                .Where(a => !string.IsNullOrEmpty(a.ApiName))
                .Select(a => new PlayerAchievementState(
                    appId,
                    a.ApiName!,
                    a.Achieved != 0 ? UnlockState.Unlocked : UnlockState.Locked,
                    a.Achieved != 0 && a.UnlockTime > 0 ? DateTimeOffset.FromUnixTimeSeconds(a.UnlockTime) : null))
                .ToList();
            return SteamResult<IReadOnlyList<PlayerAchievementState>>.Ok(states);
        });

    public static SteamResult<IReadOnlyDictionary<string, double>> ParseGlobalPercentages(string json) =>
        Parse<GlobalPercentagesEnvelope, IReadOnlyDictionary<string, double>>(json, env =>
        {
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var a in env.AchievementPercentages?.Achievements ?? [])
            {
                if (!string.IsNullOrEmpty(a.Name))
                    map[a.Name] = Math.Clamp(a.Percent, 0, 100);
            }
            return SteamResult<IReadOnlyDictionary<string, double>>.Ok(map);
        });

    public static SteamResult<ulong> ParseVanity(string json) => Parse<VanityEnvelope, ulong>(json, env =>
        env.Response is { Success: 1, SteamId: { } id } && ulong.TryParse(id, out var steamId)
            ? SteamResult<ulong>.Ok(steamId)
            : SteamResult<ulong>.Fail(SteamErrorKind.NotFound, "Nome personalizado não encontrado na Steam."));

    public static SteamResult<PlayerSummary> ParsePlayerSummary(ulong steamId, string json) => Parse<PlayerSummariesEnvelope, PlayerSummary>(json, env =>
    {
        var p = env.Response?.Players?.FirstOrDefault(x => x.SteamId == steamId.ToString());
        return p is null
            ? SteamResult<PlayerSummary>.Fail(SteamErrorKind.NotFound, "Perfil não encontrado.")
            : SteamResult<PlayerSummary>.Ok(new PlayerSummary(steamId, p.PersonaName ?? steamId.ToString(), p.AvatarFull, p.CommunityVisibilityState == 3));
    });

    internal static SteamErrorKind ClassifyPlayerStatsError(string? error)
    {
        if (error is null) return SteamErrorKind.InvalidResponse;
        if (error.Contains("not public", StringComparison.OrdinalIgnoreCase)) return SteamErrorKind.PrivateProfile;
        if (error.Contains("no stats", StringComparison.OrdinalIgnoreCase)) return SteamErrorKind.NoStats;
        if (error.Contains("invalid", StringComparison.OrdinalIgnoreCase)) return SteamErrorKind.NotFound;
        return SteamErrorKind.InvalidResponse;
    }

    private static SteamResult<TOut> Parse<TEnv, TOut>(string json, Func<TEnv, SteamResult<TOut>> map)
    {
        TEnv? env;
        try
        {
            env = JsonSerializer.Deserialize<TEnv>(json, Options);
        }
        catch (JsonException ex)
        {
            return SteamResult<TOut>.Fail(SteamErrorKind.InvalidResponse, $"JSON inválido da Steam: {ex.Message}");
        }
        return env is null ? SteamResult<TOut>.Fail(SteamErrorKind.InvalidResponse, "Resposta vazia da Steam.") : map(env);
    }

    public static string IconUrl(long appId, string hash) => $"{CdnBase}/{appId}/{hash}.jpg";
}
