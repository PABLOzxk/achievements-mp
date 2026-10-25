using AchievementsMP.Core.Library;

namespace AchievementsMP.Core.Planning;

/// <summary>Conquista pendente que o usuário marcou como perdível ou de alta prioridade, para a visão geral.</summary>
public sealed record FlaggedAchievement(long AppId, string GameName, AchievementDetail Achievement, AchievementPlan Plan);
