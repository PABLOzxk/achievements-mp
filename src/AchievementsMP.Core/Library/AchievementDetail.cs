using AchievementsMP.Core.Domain;

namespace AchievementsMP.Core.Library;

/// <summary>Uma conquista com o estado do jogador e a raridade global, para a tela de detalhes.</summary>
public sealed record AchievementDetail(
    long AppId,
    string ApiName,
    string DisplayName,
    string? Description,
    bool Hidden,
    string? IconUrl,
    string? IconGrayUrl,
    UnlockState State,
    DateTimeOffset? UnlockedAtUtc,
    double? GlobalPercent);
