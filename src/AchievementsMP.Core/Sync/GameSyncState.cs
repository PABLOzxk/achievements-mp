using AchievementsMP.Core.Domain;

namespace AchievementsMP.Core.Sync;

/// <summary>O que o banco sabe sobre a última sincronização de um jogo. Entrada do <see cref="SyncPlanner"/>.</summary>
public sealed record GameSyncState(
    long AppId,
    string Name,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayedUtc,
    bool IsOwned,
    DataAvailability Availability,
    DateTimeOffset? SchemaSyncedUtc,
    string? SchemaLanguage,
    DateTimeOffset? PlayerSyncedUtc,
    int? PlayerSyncedPlaytime,
    int AchievementCount);

[Flags]
public enum GameSyncWork
{
    None = 0,
    /// <summary>Baixar o esquema (lista de conquistas) e a raridade global.</summary>
    Schema = 1,
    /// <summary>Baixar o que o jogador desbloqueou.</summary>
    Player = 2,
}

public sealed record GameSyncTask(long AppId, string Name, GameSyncWork Work, bool HasKnownAchievements);

public sealed record SyncPolicy
{
    /// <summary>Esquemas mudam pouco (DLCs, atualizações). Reler de tempos em tempos.</summary>
    public TimeSpan SchemaMaxAge { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Jogo sem conquistas: conferir de novo de vez em quando (alguns ganham conquistas depois).</summary>
    public TimeSpan NoAchievementsRecheck { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Rede de segurança: mesmo sem sinal de que você jogou, reler o progresso depois deste prazo.</summary>
    public TimeSpan PlayerMaxAge { get; init; } = TimeSpan.FromDays(7);

    public static readonly SyncPolicy Default = new();
}
