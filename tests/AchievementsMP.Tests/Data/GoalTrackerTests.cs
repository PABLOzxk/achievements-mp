using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Data.Planning;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Tests.Data;

public class GoalTrackerTests
{
    // Quarta-feira, 21/10/2026, 15:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private const long Hades = 1145360;

    private static async Task<(TempDatabase Db, SteamCatalogRepository Steam, PlannerRepository Planner, GoalTracker Tracker)> SetupAsync()
    {
        var db = await TempDatabase.CreateMigratedAsync();
        var steam = new SteamCatalogRepository(db.Factory);
        var planner = new PlannerRepository(db.Factory);
        await steam.UpsertLibraryAsync([new Game(Hades, "Hades", null, 300, null, true)], Now.AddDays(-30), isCompleteLibrary: true);
        await steam.UpsertSchemaAsync(Hades,
            [new(Hades, "a", "Escapou do Tártaro", null, false, null, null), new(Hades, "b", "Ceifador", null, false, null, null)], Now.AddDays(-30));
        return (db, steam, planner, new GoalTracker(planner, steam));
    }

    [Fact]
    public async Task Complete_game_goal_finishes_itself_and_stays_finished()
    {
        var (db, steam, planner, tracker) = await SetupAsync();
        await using var dbScope = db;
        await planner.CreateGoalAsync(new GoalDraft("100% em Hades", GoalKind.CompleteGame, Hades), Now.AddDays(-20));
        await steam.ApplyPlayerStatesAsync(Hades, [new(Hades, "a", UnlockState.Unlocked, Now.AddDays(-2)), new(Hades, "b", UnlockState.Locked, null)], Now.AddDays(-1));

        var before = Assert.Single(await tracker.GetGoalsAsync(Now, Utc));
        Assert.Equal(GoalStatus.Active, before.Goal.Status);
        Assert.Equal(50, before.Progress.Percent);
        Assert.Equal("Hades", before.Game?.Name);

        await steam.ApplyPlayerStatesAsync(Hades, [new(Hades, "a", UnlockState.Unlocked, Now.AddDays(-2)), new(Hades, "b", UnlockState.Unlocked, Now)], Now);
        var after = Assert.Single(await tracker.GetGoalsAsync(Now, Utc));
        Assert.Equal(GoalStatus.Completed, after.Goal.Status);
        Assert.Equal(Now, after.Goal.DoneUtc);

        // Conquista nova (DLC): a meta continua concluída.
        await steam.UpsertSchemaAsync(Hades,
            [new(Hades, "a", "a", null, false, null, null), new(Hades, "b", "b", null, false, null, null), new(Hades, "c", "c", null, false, null, null)], Now.AddDays(1));
        await steam.ApplyPlayerStatesAsync(Hades, [new(Hades, "c", UnlockState.Locked, null)], Now.AddDays(1));
        var later = Assert.Single(await tracker.GetGoalsAsync(Now.AddDays(1), Utc));
        Assert.Equal(GoalStatus.Completed, later.Goal.Status);
        Assert.True(later.Progress.IsMet);
    }

    [Fact]
    public async Task Goal_without_steam_data_is_unknown_not_failed()
    {
        var (db, _, planner, tracker) = await SetupAsync();
        await using var dbScope = db;
        await planner.CreateGoalAsync(new GoalDraft("100% em Hades", GoalKind.CompleteGame, Hades), Now.AddDays(-20));
        await planner.CreateGoalAsync(new GoalDraft("Ceifador", GoalKind.UnlockAchievement, Hades, "b"), Now.AddDays(-20));
        await planner.CreateGoalAsync(new GoalDraft("3 por semana", GoalKind.UnlockCount, null, null, 3, GoalPeriod.Weekly), Now.AddDays(-20));

        var goals = await tracker.GetGoalsAsync(Now, Utc);

        Assert.All(goals, g => Assert.False(g.Progress.IsKnown));
        Assert.All(goals, g => Assert.Equal(GoalStatus.Active, g.Goal.Status));
    }

    [Fact]
    public async Task Achievement_goal_completes_with_the_unlock_date_and_shows_the_name()
    {
        var (db, steam, planner, tracker) = await SetupAsync();
        await using var dbScope = db;
        await planner.CreateGoalAsync(new GoalDraft("Ceifador", GoalKind.UnlockAchievement, Hades, "b"), Now.AddDays(-20));
        await steam.ApplyPlayerStatesAsync(Hades, [new(Hades, "b", UnlockState.Unlocked, Now.AddDays(-3))], Now);

        var goal = Assert.Single(await tracker.GetGoalsAsync(Now, Utc));

        Assert.Equal(GoalStatus.Completed, goal.Goal.Status);
        Assert.Equal(Now.AddDays(-3), goal.Goal.DoneUtc);
        Assert.Equal("Ceifador", goal.AchievementName);
    }

    [Fact]
    public async Task Weekly_goal_counts_this_week_and_never_auto_completes()
    {
        var (db, steam, planner, tracker) = await SetupAsync();
        await using var dbScope = db;
        await planner.CreateGoalAsync(new GoalDraft("1 por semana", GoalKind.UnlockCount, null, null, 1, GoalPeriod.Weekly), Now.AddDays(-20));
        await steam.ApplyPlayerStatesAsync(Hades,
            [new(Hades, "a", UnlockState.Unlocked, Now.AddDays(-9)), new(Hades, "b", UnlockState.Unlocked, Now.AddDays(-1))], Now);

        var goal = Assert.Single(await tracker.GetGoalsAsync(Now, Utc));

        Assert.Equal(1, goal.Progress.Current);
        Assert.True(goal.Progress.IsMet);
        Assert.Equal(GoalStatus.Active, goal.Goal.Status);

        var nextWeek = Assert.Single(await tracker.GetGoalsAsync(Now.AddDays(7), Utc));
        Assert.Equal(0, nextWeek.Progress.Current);
        Assert.False(nextWeek.Progress.IsMet);
    }

    [Fact]
    public async Task Custom_goal_is_never_auto_completed()
    {
        var (db, _, planner, tracker) = await SetupAsync();
        await using var dbScope = db;
        await planner.CreateGoalAsync(new GoalDraft("Zerar no modo difícil", GoalKind.Custom), Now.AddDays(-20));

        var goal = Assert.Single(await tracker.GetGoalsAsync(Now, Utc));

        Assert.Equal(GoalStatus.Active, goal.Goal.Status);
        Assert.False(goal.Progress.IsMet);
    }
}
