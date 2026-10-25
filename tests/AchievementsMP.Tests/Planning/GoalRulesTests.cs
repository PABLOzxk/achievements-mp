using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Core.Progress;

namespace AchievementsMP.Tests.Planning;

public class GoalRulesTests
{
    // Quarta-feira, 21/10/2026, 01:30 UTC = terça 20/10, 22:30 em Brasília.
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 1, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

    private static Goal G(GoalKind kind, int? target = null, GoalPeriod period = GoalPeriod.None, DateTimeOffset? deadline = null,
        GoalStatus status = GoalStatus.Active) =>
        new(1, "meta", kind, 620, "X", target, period, deadline, Priority.Normal, status, Now.AddDays(-10), null);

    [Fact]
    public void Weekly_window_starts_on_local_monday()
    {
        var (start, end) = GoalRules.CountingWindow(G(GoalKind.UnlockCount, 5, GoalPeriod.Weekly), Now, Brasilia);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(-3)), start);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(-3)), end);
    }

    [Fact]
    public void Daily_window_uses_the_local_day_not_utc()
    {
        var (start, end) = GoalRules.CountingWindow(G(GoalKind.UnlockCount, 1, GoalPeriod.Daily), Now, Brasilia);

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(-3)), start);
        Assert.Equal(TimeSpan.FromDays(1), end!.Value - start);
    }

    [Fact]
    public void Window_without_period_runs_from_creation_to_deadline()
    {
        var deadline = Now.AddDays(5);
        var goal = G(GoalKind.UnlockCount, 10, deadline: deadline);

        var (start, end) = GoalRules.CountingWindow(goal, Now, Brasilia);

        Assert.Equal(goal.CreatedUtc, start);
        Assert.Equal(deadline, end);
    }

    [Fact]
    public void Complete_game_is_met_only_at_100_percent_and_unknown_without_data()
    {
        Assert.True(GoalRules.Evaluate(G(GoalKind.CompleteGame), new(new GameProgress(10, 10, 0, 0)), Now).IsMet);

        var partial = GoalRules.Evaluate(G(GoalKind.CompleteGame), new(new GameProgress(10, 4, 6, 0)), Now);
        Assert.False(partial.IsMet);
        Assert.True(partial.IsKnown);
        Assert.Equal(40, partial.Percent);

        var missing = GoalRules.Evaluate(G(GoalKind.CompleteGame), new(new GameProgress(10, 4, 4, 2)), Now);
        Assert.False(missing.IsKnown);

        Assert.False(GoalRules.Evaluate(G(GoalKind.CompleteGame), new GoalFacts(), Now).IsKnown);
    }

    [Fact]
    public void Unlock_achievement_unknown_state_is_not_treated_as_locked()
    {
        var unknown = GoalRules.Evaluate(G(GoalKind.UnlockAchievement), new(AchievementState: UnlockState.Unknown), Now);
        var locked = GoalRules.Evaluate(G(GoalKind.UnlockAchievement), new(AchievementState: UnlockState.Locked), Now);
        var unlocked = GoalRules.Evaluate(G(GoalKind.UnlockAchievement), new(AchievementState: UnlockState.Unlocked), Now);

        Assert.False(unknown.IsKnown);
        Assert.True(locked.IsKnown);
        Assert.False(locked.IsMet);
        Assert.True(unlocked.IsMet);
    }

    [Fact]
    public void Unlock_count_compares_against_target_and_needs_data()
    {
        var goal = G(GoalKind.UnlockCount, 3, GoalPeriod.Weekly);

        Assert.True(GoalRules.Evaluate(goal, new(UnlocksInWindow: 3), Now).IsMet);
        var half = GoalRules.Evaluate(goal, new(UnlocksInWindow: 1), Now);
        Assert.False(half.IsMet);
        Assert.Equal(1, half.Current);
        Assert.False(GoalRules.Evaluate(goal, new GoalFacts(), Now).IsKnown);
    }

    [Theory]
    [InlineData(-1, DeadlineState.Overdue)]
    [InlineData(2, DeadlineState.DueSoon)]
    [InlineData(10, DeadlineState.OnTrack)]
    public void Deadline_state(int daysLeft, DeadlineState expected)
    {
        var goal = G(GoalKind.CompleteGame, deadline: Now.AddDays(daysLeft));

        Assert.Equal(expected, GoalRules.Evaluate(goal, new(new GameProgress(10, 4, 6, 0)), Now).Deadline);
    }

    [Fact]
    public void Met_or_finished_goals_have_no_deadline_pressure()
    {
        var late = Now.AddDays(-3);
        Assert.Equal(DeadlineState.None, GoalRules.Evaluate(G(GoalKind.CompleteGame, deadline: late), new(new GameProgress(5, 5, 0, 0)), Now).Deadline);
        Assert.Equal(DeadlineState.None, GoalRules.Evaluate(G(GoalKind.Custom, deadline: late, status: GoalStatus.Abandoned), new GoalFacts(), Now).Deadline);
    }

    [Fact]
    public void Completed_goal_stays_met_even_if_steam_data_changes()
    {
        var goal = G(GoalKind.CompleteGame, status: GoalStatus.Completed);

        var progress = GoalRules.Evaluate(goal, new(new GameProgress(12, 10, 2, 0)), Now); // DLC trouxe 2 conquistas novas

        Assert.True(progress.IsMet);
    }

    [Fact]
    public void Custom_goal_follows_its_status()
    {
        Assert.False(GoalRules.Evaluate(G(GoalKind.Custom), new GoalFacts(), Now).IsMet);
        Assert.True(GoalRules.Evaluate(G(GoalKind.Custom, status: GoalStatus.Completed), new GoalFacts(), Now).IsMet);
    }

    [Fact]
    public void Default_titles_read_naturally()
    {
        Assert.Equal("100% em Hades até 31/12", GoalRules.DefaultTitle(GoalKind.CompleteGame, "Hades", null, null, GoalPeriod.None, new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("1 conquista por dia", GoalRules.DefaultTitle(GoalKind.UnlockCount, null, null, 1, GoalPeriod.Daily, null));
        Assert.Equal("5 conquistas por semana em Hades", GoalRules.DefaultTitle(GoalKind.UnlockCount, "Hades", null, 5, GoalPeriod.Weekly, null));
        Assert.Equal("Desbloquear “Ceifador” em Hades", GoalRules.DefaultTitle(GoalKind.UnlockAchievement, "Hades", "Ceifador", null, GoalPeriod.None, null));
    }
}
