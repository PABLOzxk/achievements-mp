namespace AchievementsMP.Core.Domain;

public sealed record PlayerAchievementState(
    long AppId,
    string ApiName,
    UnlockState State,
    DateTimeOffset? UnlockedAtUtc);
