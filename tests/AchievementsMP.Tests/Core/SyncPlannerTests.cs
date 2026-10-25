using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Sync;

namespace AchievementsMP.Tests.Core;

public class SyncPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static GameSyncState Synced(long appId = 1, int playtime = 100) => new(
        appId, $"Jogo {appId}", playtime, Now.AddDays(-3), true, DataAvailability.Available,
        Now.AddDays(-1), "brazilian", Now.AddDays(-1), playtime, 10);

    private static GameSyncWork Work(GameSyncState g, string language = "brazilian", bool force = false) =>
        SyncPlanner.Plan([g], language, Now, SyncPolicy.Default, force).SingleOrDefault()?.Work ?? GameSyncWork.None;

    [Fact]
    public void Never_synced_game_needs_everything()
    {
        var g = Synced() with { SchemaSyncedUtc = null, SchemaLanguage = null, PlayerSyncedUtc = null, PlayerSyncedPlaytime = null, Availability = DataAvailability.NotSynced };
        Assert.Equal(GameSyncWork.Schema | GameSyncWork.Player, Work(g));
    }

    [Fact]
    public void Up_to_date_game_needs_nothing() => Assert.Equal(GameSyncWork.None, Work(Synced()));

    [Fact]
    public void More_playtime_rereads_player_only() =>
        Assert.Equal(GameSyncWork.Player, Work(Synced() with { PlaytimeMinutes = 150 }));

    [Fact]
    public void Played_after_last_read_rereads_player() =>
        Assert.Equal(GameSyncWork.Player, Work(Synced() with { LastPlayedUtc = Now.AddHours(-1) }));

    [Fact]
    public void Old_player_data_is_refreshed() =>
        Assert.Equal(GameSyncWork.Player, Work(Synced() with { PlayerSyncedUtc = Now.AddDays(-8) }));

    [Fact]
    public void Language_change_rereads_schema_and_player() =>
        Assert.Equal(GameSyncWork.Schema | GameSyncWork.Player, Work(Synced(), language: "english"));

    [Theory]
    [InlineData(DataAvailability.SyncFailed)]
    [InlineData(DataAvailability.PrivateProfile)]
    public void Previous_failure_is_retried(DataAvailability availability) =>
        Assert.Equal(GameSyncWork.Player, Work(Synced() with { Availability = availability }));

    [Fact]
    public void No_achievements_is_rechecked_only_after_a_month()
    {
        var g = Synced() with { Availability = DataAvailability.NoAchievements, AchievementCount = 0 };
        Assert.Equal(GameSyncWork.None, Work(g));
        Assert.Equal(GameSyncWork.Schema, Work(g with { SchemaSyncedUtc = Now.AddDays(-31) }));
    }

    [Fact]
    public void Games_no_longer_owned_are_skipped() =>
        Assert.Equal(GameSyncWork.None, Work(Synced() with { IsOwned = false, PlayerSyncedUtc = null }));

    [Fact]
    public void Force_rereads_everything() =>
        Assert.Equal(GameSyncWork.Schema | GameSyncWork.Player, Work(Synced(), force: true));

    [Fact]
    public void Recently_played_games_come_first()
    {
        var old = Synced(1) with { PlayerSyncedUtc = null, LastPlayedUtc = Now.AddDays(-100) };
        var recent = Synced(2) with { PlayerSyncedUtc = null, LastPlayedUtc = Now.AddDays(-1) };
        var never = Synced(3) with { PlayerSyncedUtc = null, LastPlayedUtc = null };

        var plan = SyncPlanner.Plan([old, never, recent], "brazilian", Now, SyncPolicy.Default);

        Assert.Equal([2L, 1L, 3L], plan.Select(t => t.AppId));
    }
}
