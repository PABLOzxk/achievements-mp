using System.Globalization;
using AchievementsMP.App.ViewModels;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;

namespace AchievementsMP.UiTests;

/// <summary>Rara = obtida por menos de 10% dos jogadores (o mesmo corte da Steam). Exatamente 10% não é rara.</summary>
public class RarityTests
{
    [Theory]
    [InlineData(45.0, Rarity.Common)]
    [InlineData(20.0, Rarity.Common)]
    [InlineData(19.9, Rarity.Uncommon)]
    [InlineData(10.0, Rarity.Uncommon)]
    [InlineData(9.99, Rarity.Rare)]
    [InlineData(5.0, Rarity.Rare)]
    [InlineData(1.0, Rarity.Rare)]
    [InlineData(0.99, Rarity.UltraRare)]
    public void Rarity_tiers_follow_the_global_percent(double percent, Rarity expected) =>
        Assert.Equal(expected, AchievementItem.DescribeRarity(percent).Rarity);

    [Fact]
    public void Unknown_percent_is_unknown_rarity() =>
        Assert.Equal(Rarity.Unknown, AchievementItem.DescribeRarity(null).Rarity);

    [Theory]
    [InlineData(9.99, true)]
    [InlineData(7.0, true)]
    [InlineData(0.3, true)]
    [InlineData(10.0, false)]
    [InlineData(55.0, false)]
    public void Unlocked_achievement_below_ten_percent_gets_the_rare_highlight(double percent, bool rare) =>
        Assert.Equal(rare, Unlocked(percent).IsRareUnlock);

    [Fact]
    public void Locked_rare_achievement_has_no_highlight()
    {
        var a = AchievementItem.From(new AchievementDetail(1, "a", "A", null, false, null, null, UnlockState.Locked, null, 2.0));
        Assert.False(a.IsRareUnlock);
    }

    [Theory]
    [InlineData(9.96, "9,9%")]   // arredondar daria "10%" numa conquista rara
    [InlineData(19.96, "19,9%")]
    [InlineData(10.0, "10%")]
    [InlineData(12.34, "12,3%")]
    [InlineData(0.996, "0,99%")] // arredondar daria "1%" numa ultrarrara
    [InlineData(0.3, "0,3%")]
    [InlineData(2.3, "2,3%")]
    public void Percent_is_cut_not_rounded_so_it_never_jumps_to_the_next_tier(double value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try { Assert.Equal(expected, LibraryItem.FormatPercent(value)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static AchievementItem Unlocked(double percent) =>
        AchievementItem.From(new AchievementDetail(1, "a", "A", null, false, null, null, UnlockState.Unlocked, null, percent));
}
