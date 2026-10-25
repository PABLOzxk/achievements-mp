using AchievementsMP.Core.Domain;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Tests.Data;

/// <summary>As "regras obrigatórias para evitar bugs" do plano, uma por teste.</summary>
public class SyncSafetyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private const long Portal = 620, Tf2 = 440;

    private static Game G(long id, string name, int minutes = 0) => new(id, name, null, minutes, null, true);
    private static AchievementDefinition A(long app, string name) => new(app, name, name, null, false, null, null);
    private static PlayerAchievementState S(long app, string name, UnlockState st) => new(app, name, st, null);

    [Fact]
    public async Task Repeated_sync_does_not_duplicate_games_or_achievements()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SteamCatalogRepository(db.Factory);

        for (var i = 0; i < 3; i++)
        {
            await repo.UpsertLibraryAsync([G(Portal, "Portal 2", 10 + i), G(Tf2, "TF2")], T0.AddMinutes(i), isCompleteLibrary: true);
            await repo.UpsertSchemaAsync(Portal, [A(Portal, "X"), A(Portal, "Y")], T0.AddMinutes(i));
            await repo.ApplyPlayerStatesAsync(Portal, [S(Portal, "X", UnlockState.Unlocked), S(Portal, "Y", UnlockState.Locked)], T0.AddMinutes(i));
        }

        var library = await repo.GetLibraryAsync();
        Assert.Equal(2, library.Count);
        var portal = library.Single(g => g.AppId == Portal);
        Assert.Equal(12, portal.PlaytimeMinutes);
        Assert.Equal(2, portal.Progress.Total);
        Assert.Equal(1, portal.Progress.Unlocked);
    }

    [Fact]
    public async Task Sync_never_deletes_personal_data_even_when_game_and_achievements_disappear()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var steam = new SteamCatalogRepository(db.Factory);
        var personal = new PersonalDataRepository(db.Factory);

        await steam.UpsertLibraryAsync([G(Portal, "Portal 2"), G(Tf2, "TF2")], T0, isCompleteLibrary: true);
        await steam.UpsertSchemaAsync(Portal, [A(Portal, "X"), A(Portal, "Y")], T0);
        var list = await personal.CreateListAsync("Para platinar", null, T0);
        await personal.AddGameToListAsync(list, Portal, T0);
        await personal.SetAchievementNoteAsync(Portal, "Y", "Fazer no capítulo 8", T0);
        await personal.AddPlanTaskAsync(Portal, "Y", "Pegar Y");

        // Portal some da biblioteca (reembolso, família) e a conquista Y some do esquema.
        await steam.UpsertLibraryAsync([G(Tf2, "TF2")], T0.AddHours(1), isCompleteLibrary: true);
        await steam.UpsertSchemaAsync(Portal, [A(Portal, "X")], T0.AddHours(1));

        Assert.Equal([Portal], await personal.GetListGamesAsync(list));
        Assert.Equal("Fazer no capítulo 8", await personal.GetAchievementNoteAsync(Portal, "Y"));
        Assert.Equal(1, await personal.CountPlanTasksAsync(Portal));

        var all = await steam.GetLibraryAsync(includeNotOwned: true);
        Assert.False(all.Single(g => g.AppId == Portal).IsOwned);
        Assert.DoesNotContain(await steam.GetLibraryAsync(), g => g.AppId == Portal);
        Assert.Equal(1, all.Single(g => g.AppId == Portal).Progress.Total); // Y removida não conta
    }

    [Fact]
    public async Task Empty_library_response_does_not_mark_everything_as_not_owned()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SteamCatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([G(Portal, "Portal 2"), G(Tf2, "TF2")], T0, isCompleteLibrary: true);

        await repo.UpsertLibraryAsync([], T0.AddHours(1), isCompleteLibrary: true);

        Assert.Equal(2, (await repo.GetLibraryAsync()).Count);
    }

    [Fact]
    public async Task Failed_sync_keeps_previous_progress_and_flags_it()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SteamCatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([G(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await repo.UpsertSchemaAsync(Portal, [A(Portal, "X"), A(Portal, "Y")], T0);
        await repo.ApplyPlayerStatesAsync(Portal, [S(Portal, "X", UnlockState.Unlocked), S(Portal, "Y", UnlockState.Locked)], T0);

        await repo.SetAvailabilityAsync(Portal, DataAvailability.SyncFailed);

        var g = (await repo.GetLibraryAsync()).Single();
        Assert.Equal(DataAvailability.SyncFailed, g.Availability);
        Assert.Equal(T0, g.PlayerSyncedUtc);
        Assert.Equal(new(2, 1, 1, 0), g.Progress);
    }

    [Fact]
    public async Task Achievements_without_player_data_are_unknown_not_pending()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SteamCatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([G(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await repo.UpsertSchemaAsync(Portal, [A(Portal, "X"), A(Portal, "Y"), A(Portal, "Z")], T0);
        await repo.ApplyPlayerStatesAsync(Portal, [S(Portal, "X", UnlockState.Unlocked), S(Portal, "Y", UnlockState.Unknown)], T0);

        var p = await repo.GetProgressAsync(Portal);

        Assert.Equal(1, p.Unlocked);
        Assert.Equal(0, p.Locked);
        Assert.Equal(2, p.Unknown);
        Assert.False(p.IsExact);
    }

    [Fact]
    public async Task Partial_player_response_does_not_reset_known_states()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SteamCatalogRepository(db.Factory);
        await repo.UpsertLibraryAsync([G(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await repo.UpsertSchemaAsync(Portal, [A(Portal, "X"), A(Portal, "Y")], T0);
        await repo.ApplyPlayerStatesAsync(Portal, [S(Portal, "X", UnlockState.Unlocked), S(Portal, "Y", UnlockState.Unlocked)], T0);

        await repo.ApplyPlayerStatesAsync(Portal, [S(Portal, "X", UnlockState.Unlocked)], T0.AddHours(1));

        Assert.True((await repo.GetProgressAsync(Portal)).IsComplete);
    }

    [Fact]
    public async Task Interrupted_sync_runs_are_recovered_on_startup()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var runs = new SyncRunRepository(db.Factory);
        await runs.StartAsync(SyncRunRepository.LibraryScope, null, T0);

        Assert.Equal(1, await runs.RecoverInterruptedAsync(T0.AddMinutes(1)));
        var last = await runs.GetLastAsync(SyncRunRepository.LibraryScope, null);
        Assert.Equal(SyncStatus.Cancelled, last!.Status);
        Assert.Null(await runs.GetLastAsync(SyncRunRepository.LibraryScope, null, SyncStatus.Succeeded));
    }
}
