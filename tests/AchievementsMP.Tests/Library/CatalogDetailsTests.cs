using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Progress;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Tests.Library;

public class CatalogDetailsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private const long App = 620;

    private static async Task<SteamCatalogRepository> SeedAsync(TempDatabase db)
    {
        var repo = new SteamCatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([new Game(App, "Portal 2", "h", 100, null, true)], T0, isCompleteLibrary: true);
        await repo.UpsertSchemaAsync(App,
        [
            new(App, "EASY", "Fácil", "desc", false, "on.jpg", "off.jpg"),
            new(App, "RARE", "Rara", null, true, "on2.jpg", "off2.jpg"),
            new(App, "DONE_RARE", "Já tenho", "d", false, null, null),
        ], T0);
        await repo.UpsertGlobalPercentagesAsync(App, new Dictionary<string, double> { ["EASY"] = 60, ["RARE"] = 3.5, ["DONE_RARE"] = 0.1 }, T0);
        await repo.ApplyPlayerStatesAsync(App,
        [
            new(App, "EASY", UnlockState.Locked, null),
            new(App, "RARE", UnlockState.Locked, null),
            new(App, "DONE_RARE", UnlockState.Unlocked, T0.AddDays(-3)),
        ], T0);
        return repo;
    }

    [Fact]
    public async Task Viability_uses_only_remaining_achievements()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = await SeedAsync(db);

        var game = (await repo.GetGameAsync(App))!;

        // A já desbloqueada (0,1%) não conta; a mais rara que falta é 3,5%.
        Assert.Equal(3.5, game.Outlook.RarestRemainingPercent);
        Assert.Equal(Viability.Low, game.Outlook.Viability);
        Assert.Equal(2, game.Outlook.Remaining);
    }

    [Fact]
    public async Task Achievement_details_join_state_and_rarity_and_skip_removed()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = await SeedAsync(db);
        await repo.UpsertSchemaAsync(App,
        [
            new(App, "EASY", "Fácil", "desc", false, "on.jpg", "off.jpg"),
            new(App, "RARE", "Rara", null, true, "on2.jpg", "off2.jpg"),
        ], T0.AddDays(1));

        var list = await repo.GetAchievementsAsync(App);

        Assert.Equal(["EASY", "RARE"], list.Select(a => a.ApiName).Order());
        var rare = list.Single(a => a.ApiName == "RARE");
        Assert.True(rare.Hidden);
        Assert.Null(rare.Description);
        Assert.Equal(UnlockState.Locked, rare.State);
        Assert.Equal(3.5, rare.GlobalPercent);
    }

    [Fact]
    public async Task Missing_rarity_makes_viability_unknown()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SteamCatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([new Game(App, "Portal 2", null, 1, null, true)], T0, true);
        await repo.UpsertSchemaAsync(App, [new(App, "A", "A", null, false, null, null)], T0);
        await repo.ApplyPlayerStatesAsync(App, [new(App, "A", UnlockState.Locked, null)], T0);

        Assert.Equal(Viability.Unknown, (await repo.GetGameAsync(App))!.Outlook.Viability);
    }
}
