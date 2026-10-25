using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Progress;

namespace AchievementsMP.Tests.Library;

public class LibraryQueryTests
{
    private static LibraryGame G(long id, string name, int total, int unlocked, double? rarest = null, int playtime = 0, DateTimeOffset? lastPlayed = null)
    {
        var progress = new GameProgress(total, unlocked, total - unlocked, 0);
        return new LibraryGame(id, name, null, playtime, lastPlayed, true,
            total == 0 ? DataAvailability.NoAchievements : DataAvailability.Available, null, null,
            progress, CompletionOutlook.From(progress, rarest, 0));
    }

    // TBH: 31/80, rara 0,3% · skate: 8/21, rara 12% · Wallpaper: 6/17, rara 35% · Celeste 100% · TF2 sem conquistas · Novo: 0/10, rara 40%
    private static readonly LibraryGame[] Games =
    [
        G(1, "TBH: Task Bar Hero", 80, 31, rarest: 0.3, playtime: 3234),
        G(2, "skate.", 21, 8, rarest: 12, playtime: 828),
        G(3, "Wallpaper Engine", 17, 6, rarest: 35, playtime: 600, lastPlayed: DateTimeOffset.UnixEpoch.AddDays(10)),
        G(4, "Celeste", 10, 10, playtime: 1200),
        G(5, "Team Fortress 2", 0, 0, playtime: 50),
        G(6, "Novo", 10, 0, rarest: 40),
    ];

    private static long[] Ids(LibraryFilter f) => LibraryQuery.Apply(Games, f).Select(g => g.AppId).ToArray();

    [Theory]
    [InlineData(LibraryStatus.InProgress, new long[] { 2, 1, 3 })]
    [InlineData(LibraryStatus.Completed, new long[] { 4 })]
    [InlineData(LibraryStatus.NotStarted, new long[] { 6 })]
    [InlineData(LibraryStatus.NoAchievements, new long[] { 5 })]
    public void Status_filters(LibraryStatus status, long[] expectedByName) =>
        Assert.Equal(expectedByName.Order(), Ids(new LibraryFilter { Status = status }).Order());

    [Fact]
    public void Fewest_remaining_puts_actionable_games_first()
    {
        var ids = Ids(new LibraryFilter { Sort = LibrarySort.FewestRemaining });
        Assert.Equal(new long[] { 6, 3, 2, 1 }, ids[..4]); // faltam 10, 11, 13, 49
        Assert.Equal(new long[] { 4, 5 }.Order(), ids[4..].Order()); // completos e sem conquistas no fim
    }

    [Fact]
    public void Most_viable_orders_by_rarest_remaining()
    {
        var ids = Ids(new LibraryFilter { Sort = LibrarySort.MostViable, Status = LibraryStatus.InProgress });
        Assert.Equal(new long[] { 3, 2, 1 }, ids); // 35% > 12% > 0,3%
    }

    [Fact]
    public void Min_viability_excludes_hard_unknown_and_done()
    {
        Assert.Equal(new long[] { 6, 3 }.Order(), Ids(new LibraryFilter { MinViability = Viability.High }).Order());
        Assert.Equal(new long[] { 6, 3, 2 }.Order(), Ids(new LibraryFilter { MinViability = Viability.Medium }).Order());
    }

    [Fact]
    public void Search_is_case_insensitive_and_combines_with_list()
    {
        Assert.Equal(new long[] { 2 }, Ids(new LibraryFilter { Search = "SKATE" }));
        Assert.Equal(new long[] { 4 }, Ids(new LibraryFilter { Search = "e", OnlyAppIds = new HashSet<long> { 4, 5 }, Status = LibraryStatus.Completed }));
    }

    [Fact]
    public void Other_sorts()
    {
        Assert.Equal(1, Ids(new LibraryFilter { Sort = LibrarySort.MostPlayed })[0]);
        Assert.Equal(3, Ids(new LibraryFilter { Sort = LibrarySort.RecentlyPlayed })[0]);
        Assert.Equal(4, Ids(new LibraryFilter { Sort = LibrarySort.HighestPercent })[0]);
        Assert.Equal(4, Ids(new LibraryFilter { Sort = LibrarySort.Name })[0]); // Celeste
    }
}
