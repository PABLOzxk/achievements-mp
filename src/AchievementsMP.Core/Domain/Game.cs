namespace AchievementsMP.Core.Domain;

/// <summary>Jogo da biblioteca, como reportado pela Steam. Dados oficiais, nunca editados pelo usuário.</summary>
public sealed record Game(
    long AppId,
    string Name,
    string? IconHash,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayedUtc,
    bool? HasCommunityStats);
