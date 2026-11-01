namespace AchievementsMP.Core.Domain;

/// <summary>De onde vêm os dados oficiais de um jogo. O número é gravado no banco: nunca renumere.</summary>
public enum Platform
{
    Steam = 0,
    Xbox = 1,
    PlayStation = 2,
}

public static class Platforms
{
    // Cada plataforma tem uma faixa própria de números internos de jogo. Na Steam o número é o próprio AppId
    // (cabe em 32 bits). Nas outras o ID original é texto (games.external_id) e o número interno é sequencial
    // dentro da faixa: assim metas, listas, checklists, guias e notas continuam usando um único número por jogo
    // e nunca há colisão com um AppId da Steam.
    private const long RangeSize = 1L << 40;

    public static IReadOnlyList<Platform> All { get; } = [Platform.Steam, Platform.Xbox, Platform.PlayStation];

    public static string DisplayName(this Platform platform) => platform switch
    {
        Platform.Steam => "Steam",
        Platform.Xbox => "Xbox",
        Platform.PlayStation => "PlayStation",
        _ => platform.ToString(),
    };

    public static long FirstId(this Platform platform) => (long)platform * RangeSize;

    public static long LastId(this Platform platform) => FirstId(platform) + RangeSize - 1;

    public static bool Owns(this Platform platform, long gameId) => gameId >= platform.FirstId() && gameId <= platform.LastId();
}

/// <summary>
/// Jogo de uma plataforma que não é a Steam, como ela informa. O número interno do jogo é dado pelo banco
/// na primeira vez que o jogo aparece e nunca muda.
/// </summary>
public sealed record PlatformGame(
    string ExternalId,
    string Name,
    string? ImageUrl,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayedUtc);
