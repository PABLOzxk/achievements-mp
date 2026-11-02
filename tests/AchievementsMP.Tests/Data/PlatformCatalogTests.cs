using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Progress;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Tests.Data;

/// <summary>Jogos de plataformas diferentes convivem no mesmo banco sem que a sincronização de uma mexa na outra.</summary>
public class PlatformCatalogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private const long Portal = 620;

    private static Game Steam(long id, string name) => new(id, name, null, 0, null, true);
    private static PlatformGame Xbox(string titleId, string name, string? image = null) => new(titleId, name, image, 0, null);
    private static PlayerAchievementState S(long app, string name, UnlockState st) => new(app, name, st, null);

    [Fact]
    public void Id_ranges_do_not_overlap_and_steam_app_ids_fit()
    {
        Assert.True(Platform.Steam.Owns(uint.MaxValue));
        foreach (var a in Platforms.All)
            foreach (var b in Platforms.All.Where(b => b != a))
                Assert.True(a.LastId() < b.FirstId() || b.LastId() < a.FirstId());
        Assert.False(Platform.Xbox.Owns(Portal));
    }

    [Fact]
    public async Task New_platform_games_get_stable_ids_in_their_own_range()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);

        var first = await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1717113201", "Halo Infinite"), Xbox("219630713", "Forza Horizon 5")], T0, isCompleteLibrary: true);
        var second = await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("219630713", "Forza Horizon 5"), Xbox("1717113201", "Halo Infinite"), Xbox("12345", "Novo")], T0.AddHours(1), isCompleteLibrary: true);

        Assert.All(second.Values, id => Assert.True(Platform.Xbox.Owns(id)));
        Assert.Equal(first["1717113201"], second["1717113201"]);
        Assert.Equal(first["219630713"], second["219630713"]);
        Assert.Equal(3, second.Values.Distinct().Count());

        var library = await repo.GetLibraryAsync();
        Assert.All(library, g => Assert.Equal(Platform.Xbox, g.Platform));
        Assert.Equal(3, library.Count);
    }

    [Fact]
    public async Task Platform_game_uses_its_own_image_url()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);

        await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1", "Halo", "https://images.example/halo.png")], T0, isCompleteLibrary: true);
        await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1", "Halo")], T0.AddHours(1), isCompleteLibrary: true);

        Assert.Equal("https://images.example/halo.png", Assert.Single(await repo.GetLibraryAsync()).IconUrl);
    }

    [Fact]
    public async Task Steam_library_sync_does_not_mark_other_platforms_as_not_owned()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);
        await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1", "Halo")], T0, isCompleteLibrary: true);

        await repo.UpsertLibraryAsync([Steam(Portal, "Portal 2")], T0.AddHours(1), isCompleteLibrary: true);

        var library = await repo.GetLibraryAsync();
        Assert.Contains(library, g => g.Platform == Platform.Xbox && g.Name == "Halo");
        Assert.Contains(library, g => g.Platform == Platform.Steam && g.AppId == Portal);
    }

    [Fact]
    public async Task Platform_library_sync_only_marks_its_own_missing_games()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([Steam(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1", "Halo"), Xbox("2", "Gears")], T0, isCompleteLibrary: true);

        await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("2", "Gears")], T0.AddHours(1), isCompleteLibrary: true);

        var owned = (await repo.GetLibraryAsync()).Select(g => g.Name).Order().ToList();
        Assert.Equal(["Gears", "Portal 2"], owned);
    }

    [Fact]
    public async Task Steam_account_change_keeps_progress_of_other_platforms()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);
        await repo.SetOwnerSteamIdAsync(76561197960287930);
        await repo.UpsertLibraryAsync([Steam(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await repo.UpsertSchemaAsync(Portal, [new(Portal, "X", "X", null, false, null, null)], T0);
        await repo.ApplyPlayerStatesAsync(Portal, [S(Portal, "X", UnlockState.Unlocked)], T0);
        var halo = (await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1", "Halo")], T0, isCompleteLibrary: true))["1"];
        await repo.UpsertSchemaAsync(halo, [new(halo, "1", "Primeira", null, false, null, null)], T0);
        await repo.ApplyPlayerStatesAsync(halo, [S(halo, "1", UnlockState.Unlocked)], T0);

        await repo.SetOwnerSteamIdAsync(76561197960287931);

        var all = await repo.GetLibraryAsync(includeNotOwned: true);
        var haloGame = all.Single(g => g.AppId == halo);
        Assert.True(haloGame.IsOwned);
        Assert.Equal(1, haloGame.Progress.Unlocked);
        var portal = all.Single(g => g.AppId == Portal);
        Assert.False(portal.IsOwned);
        Assert.Equal(0, portal.Progress.Unlocked);
    }

    [Fact]
    public async Task Each_platform_keeps_its_own_owner()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);

        await repo.SetOwnerSteamIdAsync(76561197960287930);
        await repo.SetOwnerAsync(Platform.Xbox, "2533274800000000");

        Assert.Equal(76561197960287930UL, await repo.GetOwnerSteamIdAsync());
        Assert.Equal("2533274800000000", await repo.GetOwnerAsync(Platform.Xbox));
        Assert.Null(await repo.GetOwnerAsync(Platform.PlayStation));
    }

    [Fact]
    public async Task Steam_sync_plan_only_sees_steam_games()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([Steam(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await repo.UpsertPlatformLibraryAsync(Platform.Xbox, [Xbox("1", "Halo")], T0, isCompleteLibrary: true);

        Assert.Equal(Portal, Assert.Single(await repo.GetSyncStatesAsync()).AppId);
        Assert.Equal("Halo", Assert.Single(await repo.GetSyncStatesAsync(Platform.Xbox)).Name);
    }

    [Fact]
    public async Task Steam_cannot_be_written_as_platform_library_and_steam_ids_outside_range_are_ignored()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new CatalogRepository(db.Factory);

        await Assert.ThrowsAsync<ArgumentException>(() => repo.UpsertPlatformLibraryAsync(Platform.Steam, [Xbox("620", "Portal 2")], T0, true));
        await repo.UpsertLibraryAsync([Steam(Platform.Xbox.FirstId(), "Impostor")], T0, isCompleteLibrary: false);
        Assert.Empty(await repo.GetLibraryAsync());
    }

    [Fact]
    public void Library_filter_by_platform()
    {
        LibraryGame G(long id, Platform p) => new(id, $"Jogo {id}", null, 0, null, true, DataAvailability.Available, null, null,
            GameProgress.Empty, CompletionOutlook.From(GameProgress.Empty, null, 0), p);
        var games = new[] { G(Portal, Platform.Steam), G(Platform.Xbox.FirstId(), Platform.Xbox) };

        Assert.Equal(2, LibraryQuery.Apply(games, new LibraryFilter()).Count);
        Assert.Equal(Platform.Xbox, Assert.Single(LibraryQuery.Apply(games, new LibraryFilter { Platform = Platform.Xbox })).Platform);
    }
}
