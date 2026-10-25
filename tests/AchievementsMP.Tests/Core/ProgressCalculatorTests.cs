using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Progress;

namespace AchievementsMP.Tests.Core;

public class ProgressCalculatorTests
{
    private static AchievementDefinition Def(string name) => new(1, name, name, null, false, null, null);
    private static PlayerAchievementState State(string name, UnlockState s) => new(1, name, s, null);

    [Fact]
    public void Plan_example_32_of_40_is_80_percent()
    {
        var schema = Enumerable.Range(0, 40).Select(i => Def($"A{i}")).ToList();
        var states = schema.Select((d, i) => State(d.ApiName, i < 32 ? UnlockState.Unlocked : UnlockState.Locked));

        var p = ProgressCalculator.Calculate(schema, states);

        Assert.Equal(new GameProgress(40, 32, 8, 0), p);
        Assert.Equal(80.0, p.Percent);
        Assert.True(p.IsExact);
        Assert.False(p.IsComplete);
    }

    [Fact]
    public void Missing_state_is_unknown_not_locked()
    {
        var p = ProgressCalculator.Calculate([Def("A"), Def("B"), Def("C")], [State("A", UnlockState.Unlocked)]);

        Assert.Equal(1, p.Unlocked);
        Assert.Equal(0, p.Locked);
        Assert.Equal(2, p.Unknown);
        Assert.False(p.IsExact);
        Assert.Equal(0, p.KnownRemaining);
    }

    [Fact]
    public void States_for_achievements_outside_schema_are_ignored()
    {
        var p = ProgressCalculator.Calculate([Def("A")], [State("A", UnlockState.Unlocked), State("GHOST", UnlockState.Unlocked)]);
        Assert.Equal(new GameProgress(1, 1, 0, 0), p);
        Assert.True(p.IsComplete);
    }

    [Fact]
    public void Game_without_achievements_has_no_percent_and_is_never_complete()
    {
        var p = ProgressCalculator.Calculate([], []);
        Assert.Null(p.Percent);
        Assert.False(p.IsComplete);
    }
}
