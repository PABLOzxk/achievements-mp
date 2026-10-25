using AchievementsMP.Core.Progress;

namespace AchievementsMP.Tests.Core;

public class CompletionOutlookTests
{
    private static readonly GameProgress Started = new(10, 6, 4, 0);

    [Theory]
    [InlineData(45.0, Viability.High)]
    [InlineData(20.0, Viability.High)]
    [InlineData(19.9, Viability.Medium)]
    [InlineData(5.0, Viability.Medium)]
    [InlineData(4.9, Viability.Low)]
    [InlineData(1.0, Viability.Low)]
    [InlineData(0.4, Viability.VeryLow)]
    public void Viability_follows_the_rarest_remaining_achievement(double rarest, Viability expected)
    {
        var o = CompletionOutlook.From(Started, rarest, remainingWithoutRarity: 0);
        Assert.Equal(expected, o.Viability);
        Assert.Equal(4, o.Remaining);
    }

    [Fact]
    public void Complete_game_is_done() =>
        Assert.Equal(Viability.Done, CompletionOutlook.From(new(10, 10, 0, 0), null, 0).Viability);

    [Fact]
    public void Missing_rarity_for_any_remaining_achievement_is_unknown() =>
        Assert.Equal(Viability.Unknown, CompletionOutlook.From(Started, 50, remainingWithoutRarity: 1).Viability);

    [Fact]
    public void Unknown_player_state_is_unknown_even_with_rarity() =>
        Assert.Equal(Viability.Unknown, CompletionOutlook.From(new(10, 6, 3, 1), 50, 0).Viability);

    [Fact]
    public void Game_without_achievements_is_unknown() =>
        Assert.Equal(Viability.Unknown, CompletionOutlook.From(GameProgress.Empty, null, 0).Viability);
}
