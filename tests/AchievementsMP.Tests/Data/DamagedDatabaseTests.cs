using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Tests.Data;

/// <summary>Banco local que não abre mais (disco com defeito, arquivo cortado): o app precisa de uma saída sem perder nada.</summary>
public class DamagedDatabaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static async Task<IReadOnlyList<string>> GoalTitlesAsync(SqliteConnectionFactory factory) =>
        (await new PlannerRepository(factory).GetGoalsAsync()).Select(g => g.Title).ToList();

    private static async Task DamageAsync(AppPaths paths)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await File.WriteAllBytesAsync(paths.Database, Enumerable.Repeat((byte)0x5A, 8192).ToArray());
    }

    [Fact]
    public async Task Damaged_database_is_recognized_on_startup()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await DamageAsync(db.Paths);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => AppDatabase.InitializeAsync(db.Paths));

        Assert.True(AppDatabase.IsDamaged(ex), ex.ToString());
    }

    [Fact]
    public async Task Damaged_database_can_be_replaced_by_a_backup_and_is_kept_aside()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await new PlannerRepository(db.Factory).CreateGoalAsync(new GoalDraft("salva no backup", GoalKind.Custom), T0);
        var backup = await new BackupService(db.Factory, db.Paths).CreateAsync(Path.Combine(db.Paths.Root, "nuvem"), T0);
        await DamageAsync(db.Paths);
        var damagedBytes = await File.ReadAllBytesAsync(db.Paths.Database);

        // Sem o banco aberto: é o caminho da tela de recuperação.
        await BackupService.StageRestoreAsync(db.Paths, backup.Path);
        var (factory, _, restore) = await AppDatabase.InitializeAsync(db.Paths);

        Assert.True(restore!.Restored, restore.Error);
        Assert.Equal(["salva no backup"], await GoalTitlesAsync(factory));
        Assert.Equal(damagedBytes, await File.ReadAllBytesAsync(restore.SafetyCopyPath!));
    }

    [Fact]
    public async Task Starting_over_moves_the_damaged_file_aside_instead_of_deleting_it()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await DamageAsync(db.Paths);
        await File.WriteAllTextAsync(db.Paths.Database + "-wal", "wal antigo");
        var damagedBytes = await File.ReadAllBytesAsync(db.Paths.Database);

        var kept = AppDatabase.SetAsideDamaged(db.Paths, T0);
        var (factory, _, _) = await AppDatabase.InitializeAsync(db.Paths);

        Assert.Equal(damagedBytes, await File.ReadAllBytesAsync(kept));
        Assert.Equal("wal antigo", await File.ReadAllTextAsync(kept + "-wal"));
        Assert.StartsWith(db.Paths.Backups, kept);
        Assert.Empty(await GoalTitlesAsync(factory));
        Assert.NotEqual(kept, AppDatabase.SetAsideDamaged(db.Paths, T0)); // nunca sobrescreve um guardado antes
    }

    [Fact]
    public void Other_errors_are_not_mistaken_for_damage()
    {
        Assert.False(AppDatabase.IsDamaged(new IOException("disco cheio")));
        Assert.False(AppDatabase.IsDamaged(new Microsoft.Data.Sqlite.SqliteException("database is locked", 5)));
    }
}
