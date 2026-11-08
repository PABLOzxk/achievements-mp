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

    /// <summary>
    /// IPlayerService/GetRecentlyPlayedGames/v1 — jogados nas últimas 2 semanas. Diferente do GetOwnedGames, inclui jogos
    /// emprestados da Família Steam: é a única rota com a chave que mostra esses jogos.
    /// </summary>
    Task<SteamResult<IReadOnlyList<Game>>> GetRecentlyPlayedGamesAsync(ulong steamId, CancellationToken ct = default);

    /// <summary>ISteamUserStats/GetSchemaForGame/v2 — lista de conquistas do jogo, no idioma pedido.</summary>
    Task<SteamResult<IReadOnlyList<AchievementDefinition>>> GetSchemaForGameAsync(long appId, string language, CancellationToken ct = default);

    /// <summary>ISteamUserStats/GetPlayerAchievements/v1 — o que o jogador desbloqueou.</summary>
    Task<SteamResult<IReadOnlyList<PlayerAchievementState>>> GetPlayerAchievementsAsync(ulong steamId, long appId, CancellationToken ct = default);

    /// <summary>ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2 — raridade global (não exige chave).</summary>
    Task<SteamResult<IReadOnlyDictionary<string, double>>> GetGlobalAchievementPercentagesAsync(long appId, CancellationToken ct = default);
}

/// <summary>Um jogo da biblioteca da Família Steam, como a Steam informa para quem pergunta.</summary>
/// <param name="PlaytimeMinutes">Tempo de jogo de quem pergunta (não do dono).</param>
/// <param name="LastPlayedUtc">Última vez que quem pergunta jogou.</param>
public sealed record SharedFamilyApp(
    long AppId,
    string Name,
    string? IconHash,
    IReadOnlyList<ulong> OwnerSteamIds,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayedUtc);

/// <summary>
/// Biblioteca da Família Steam (IFamilyGroupsService). Essa interface da Steam recusa a chave da Web API: exige o token da
/// sessão da loja (webapi_token), que vale cerca de um dia. Quem chama passa o token a cada chamada; nada aqui guarda o token.
/// A Valve não documenta essas rotas, então podem mudar sem aviso.
/// </summary>
public interface ISteamFamilyApi
{
    /// <summary>IFamilyGroupsService/GetFamilyGroupForUser/v1 — id da família do usuário, ou null se ele não está em nenhuma.</summary>
    Task<SteamResult<ulong?>> GetFamilyGroupIdAsync(string accessToken, ulong steamId, CancellationToken ct = default);

    /// <summary>IFamilyGroupsService/GetSharedLibraryApps/v1 — jogos compartilhados na família, com o tempo de jogo de quem pergunta.</summary>
    Task<SteamResult<IReadOnlyList<SharedFamilyApp>>> GetSharedLibraryAppsAsync(string accessToken, ulong familyGroupId, ulong steamId,
        string language, CancellationToken ct = default);
}
