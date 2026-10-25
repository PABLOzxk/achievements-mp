using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Planning;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Data.Planning;

/// <summary>Uma meta com o progresso calculado e os nomes para exibir.</summary>
public sealed record GoalView(Goal Goal, GoalProgress Progress, LibraryGame? Game, string? AchievementName);

/// <summary>
/// Calcula o progresso das metas com os dados da Steam já sincronizados. Metas não recorrentes cumpridas são
/// marcadas como concluídas (com a data) e ficam assim, mesmo que a Steam mude depois.
/// </summary>
public sealed class GoalTracker(PlannerRepository planner, SteamCatalogRepository catalog)
{
    public async Task<IReadOnlyList<GoalView>> GetGoalsAsync(DateTimeOffset now, TimeZoneInfo zone, CancellationToken ct = default)
    {
        var goals = await planner.GetGoalsAsync(ct);
        if (goals.Count == 0) return [];

        var games = (await catalog.GetLibraryAsync(includeNotOwned: true, ct)).ToDictionary(g => g.AppId);
        var achievementsByGame = new Dictionary<long, IReadOnlyDictionary<string, AchievementDetail>>();

        async Task<AchievementDetail?> FindAchievementAsync(long appId, string apiName)
        {
            if (!achievementsByGame.TryGetValue(appId, out var map))
            {
                map = (await catalog.GetAchievementsAsync(appId, ct)).ToDictionary(a => a.ApiName, StringComparer.Ordinal);
                achievementsByGame[appId] = map;
            }
            return map.GetValueOrDefault(apiName);
        }

        var result = new List<GoalView>(goals.Count);
        foreach (var original in goals)
        {
            var goal = original;
            var game = goal.AppId is { } id ? games.GetValueOrDefault(id) : null;
            AchievementDetail? achievement = null;

            GoalFacts facts;
            switch (goal.Kind)
            {
                case GoalKind.CompleteGame:
                    facts = new GoalFacts(GameProgress: game?.Progress);
                    break;
                case GoalKind.UnlockAchievement when goal.AppId is { } appId && goal.ApiName is { } apiName:
                    achievement = await FindAchievementAsync(appId, apiName);
                    facts = new GoalFacts(AchievementState: achievement?.State, AchievementUnlockedUtc: achievement?.UnlockedAtUtc);
                    break;
                case GoalKind.UnlockCount:
                    var (start, end) = GoalRules.CountingWindow(goal, now, zone);
                    var counted = await planner.CountUnlocksAsync(goal.AppId, start, end, ct);
                    // Sem nenhuma leitura de conquistas da Steam, zero desbloqueios não prova nada.
                    var hasData = goal.AppId is not null
                        ? game?.PlayerSyncedUtc is not null
                        : games.Values.Any(g => g.PlayerSyncedUtc is not null);
                    facts = new GoalFacts(UnlocksInWindow: hasData ? counted : null);
                    break;
                default:
                    facts = new GoalFacts();
                    break;
            }

            var progress = GoalRules.Evaluate(goal, facts, now);
            if (goal.Status == GoalStatus.Active && goal.Kind != GoalKind.Custom && !goal.IsRecurring && progress is { IsMet: true, IsKnown: true })
            {
                var doneAt = facts.AchievementUnlockedUtc ?? now;
                await planner.SetGoalStatusAsync(goal.Id, GoalStatus.Completed, doneAt, ct);
                goal = goal with { Status = GoalStatus.Completed, DoneUtc = doneAt };
                progress = progress with { Deadline = DeadlineState.None };
            }

            result.Add(new GoalView(goal, progress, game, achievement?.DisplayName));
        }
        return result;
    }
}
