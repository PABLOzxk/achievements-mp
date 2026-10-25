namespace AchievementsMP.Core.Domain;

/// <summary>Conquista do esquema do jogo. A chave natural é (AppId, ApiName).</summary>
public sealed record AchievementDefinition(
    long AppId,
    string ApiName,
    string DisplayName,
    string? Description,
    bool Hidden,
    string? IconUrl,
    string? IconGrayUrl);
