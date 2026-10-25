using AchievementsMP.Data.Migrations;
using Dapper;

namespace AchievementsMP.Tests.Data;

public class MigrationRunnerTests
{
    [Fact]
    public async Task Fresh_database_reaches_latest_version_without_backup()
    {
        await using var db = TempDatabase.CreateEmpty();

        var result = await new MigrationRunner(db.Factory, db.Paths.Backups).MigrateAsync();

        Assert.Equal(0, result.FromVersion);
        Assert.Equal(Schema.LatestVersion, result.ToVersion);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public async Task Running_twice_is_a_no_op()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();

        var result = await new MigrationRunner(db.Factory, db.Paths.Backups).MigrateAsync();

        Assert.Equal(result.FromVersion, result.ToVersion);
        Assert.Empty(Directory.GetFiles(db.Paths.Backups));
    }

    [Fact]
    public async Task Upgrade_backs_up_and_keeps_data()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO user_lists(name, created_utc) VALUES ('Favoritos', 0)");

        var v2 = Schema.All.Append(new Migration(Schema.LatestVersion + 1, "add_column", "ALTER TABLE user_lists ADD COLUMN color TEXT;")).ToList();
        var result = await new MigrationRunner(db.Factory, db.Paths.Backups, v2).MigrateAsync();

        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        await using var after = await db.Factory.OpenAsync();
        Assert.Equal("Favoritos", await after.QuerySingleAsync<string>("SELECT name FROM user_lists"));
    }

    [Fact]
    public async Task Version_1_database_upgrades_to_latest_keeping_games()
    {
        await using var db = TempDatabase.CreateEmpty();
        await new MigrationRunner(db.Factory, db.Paths.Backups, Schema.All.Take(1).ToList()).MigrateAsync();
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO games(app_id, name, first_seen_utc, library_synced_utc) VALUES (620, 'Portal 2', 0, 0)");

        var result = await new MigrationRunner(db.Factory, db.Paths.Backups).MigrateAsync();

        Assert.Equal(1, result.FromVersion);
        Assert.Equal(Schema.LatestVersion, result.ToVersion);
        Assert.NotNull(result.BackupPath);
        var games = await new AchievementsMP.Data.Repositories.SteamCatalogRepository(db.Factory).GetSyncStatesAsync();
        Assert.Equal("Portal 2", Assert.Single(games).Name);
    }

    [Fact]
    public async Task Failed_migration_rolls_back_and_leaves_version_untouched()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var broken = Schema.All
            .Append(new Migration(Schema.LatestVersion + 1, "broken", "CREATE TABLE half_done(x INTEGER); INSERT INTO tabela_que_nao_existe VALUES (1);"))
            .ToList();

        await Assert.ThrowsAnyAsync<Exception>(() => new MigrationRunner(db.Factory, db.Paths.Backups, broken).MigrateAsync());

        await using var c = await db.Factory.OpenAsync();
        Assert.Equal(Schema.LatestVersion, await MigrationRunner.GetVersionAsync(c));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'half_done'"));
        Assert.Single(Directory.GetFiles(db.Paths.Backups));
    }

    [Fact]
    public async Task Database_from_newer_app_is_refused_not_touched()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync($"PRAGMA user_version = {Schema.LatestVersion + 5}");

        var ex = await Assert.ThrowsAsync<DatabaseTooNewException>(() => new MigrationRunner(db.Factory, db.Paths.Backups).MigrateAsync());
        Assert.Equal(Schema.LatestVersion + 5, ex.FoundVersion);
    }

    [Fact]
    public void Non_sequential_migrations_are_rejected()
    {
        var bad = new[] { new Migration(1, "a", ""), new Migration(3, "c", "") };
        Assert.Throws<InvalidOperationException>(() => new MigrationRunner(null!, "", bad));
    }
}
