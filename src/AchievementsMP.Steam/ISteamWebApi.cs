using AchievementsMP.Core.Domain;

namespace AchievementsMP.Steam;

public sealed record PlayerSummary(ulong SteamId, string PersonaName, string? AvatarUrl, bool IsProfilePublic);

public sealed record OwnedGames(int GameCount, IReadOnlyList<Game> Games);

/// <summary>
/// Leitura da Steam Web API. Somente leitura, sem senha: usa a chave pessoal do usuário (steamcommunity.com/dev/apikey).
/// Isolada atrás desta interface para que o resto do app possa ser testado com um fake.
/// </summary>
public interface ISteamWebApi
{
    /// <summary>ISteamUser/ResolveVanityURL/v1 — nome personalizado → SteamID64.</summary>
    Task<SteamResult<ulong>> ResolveVanityUrlAsync(string vanityName, CancellationToken ct = default);

    /// <summary>ISteamUser/GetPlayerSummaries/v2 — usado para confirmar o perfil e se ele é público.</summary>
    Task<SteamResult<PlayerSummary>> GetPlayerSummaryAsync(ulong steamId, CancellationToken ct = default);

    /// <summary>IPlayerService/GetOwnedGames/v1 — biblioteca com tempo de jogo. Resposta vazia = perfil/detalhes privados.</summary>
    Task<SteamResult<OwnedGames>> GetOwnedGamesAsync(ulong steamId, CancellationToken ct = default);

    /// <summary>ISteamUserStats/GetSchemaForGame/v2 — lista de conquistas do jogo, no idioma pedido.</summary>
    Task<SteamResult<IReadOnlyList<AchievementDefinition>>> GetSchemaForGameAsync(long appId, string language, CancellationToken ct = default);

    /// <summary>ISteamUserStats/GetPlayerAchievements/v1 — o que o jogador desbloqueou.</summary>
    Task<SteamResult<IReadOnlyList<PlayerAchievementState>>> GetPlayerAchievementsAsync(ulong steamId, long appId, CancellationToken ct = default);

    /// <summary>ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2 — raridade global (não exige chave).</summary>
    Task<SteamResult<IReadOnlyDictionary<string, double>>> GetGlobalAchievementPercentagesAsync(long appId, CancellationToken ct = default);
}
