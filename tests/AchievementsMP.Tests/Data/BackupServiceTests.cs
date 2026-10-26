using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Data;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Migrations;
using AchievementsMP.Data.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AchievementsMP.Tests.Data;

public class BackupServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private const long Hades = 1145360;

    private static string CloudFolder(TempDatabase db) => Path.Combine(db.Paths.Root, "OneDrive", "Backups");

    private static async Task<IReadOnlyList<string>> GoalTitlesAsync(SqliteConnectionFactory factory) =>
        (await new PlannerRepository(factory).GetGoalsAsync()).Select(g => g.Title).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task Backup_is_a_single_complete_file_with_the_personal_data()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var planner = new PlannerRepository(db.Factory);
        await planner.CreateGoalAsync(new GoalDraft("100% em Hades", GoalKind.CompleteGame, Hades), T0);
        await planner.SavePlanAsync(new AchievementPlan(Hades, "X", Effort.Quick, Note: "antes do chefe"), T0);

        var backup = await new BackupService(db.Factory, db.Paths).CreateAsync(CloudFolder(db), T0);

        Assert.StartsWith(BackupService.FilePrefix, Path.GetFileName(backup.Path));
        Assert.Equal([backup.Path], Directory.GetFiles(CloudFolder(db)));
        var summary = await BackupService.InspectAsync(backup.Path);
        Assert.Equal(Schema.All[^1].Version, summary.SchemaVersion);
        Assert.Equal((1, 1, 1), (summary.Goals, summary.Plans, summary.Notes));
    }

    [Fact]
    public async Task Only_the_latest_backups_are_kept_and_other_files_are_never_touched()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var folder = CloudFolder(db);
        Directory.CreateDirectory(folder);
        string[] foreign = ["fotos.db", "AchievementsMP-backup-anotacoes.txt", "AchievementsMP-backup-2020-01-01_000000.db.bak"];
        foreach (var name in foreign)
            await File.WriteAllTextAsync(Path.Combine(folder, name), "não é meu");

        var service = new BackupService(db.Factory, db.Paths);
        var made = new List<BackupFile>();
        for (var i = 0; i < BackupService.KeepCount + 2; i++)
            made.Add(await service.CreateAsync(folder, T0.AddDays(i)));

        var kept = BackupService.List(folder);
        Assert.Equal(made.Skip(2).Reverse().Select(b => b.Path), kept.Select(b => b.Path));
        Assert.All(foreign, name => Assert.True(File.Exists(Path.Combine(folder, name)), name));
    }

    [Fact]
    public async Task Automatic_backup_runs_only_when_configured_and_once_per_interval()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var service = new BackupService(db.Factory, db.Paths);
        var folder = CloudFolder(db);

        Assert.Null(await service.CreateIfDueAsync(null, T0));
        Assert.NotNull(await service.CreateIfDueAsync(folder, T0));
        Assert.Null(await service.CreateIfDueAsync(folder, T0.AddHours(5)));
        Assert.NotNull(await service.CreateIfDueAsync(folder, T0.AddHours(25)));
        Assert.Equal(2, BackupService.List(folder).Count);
    }

    [Fact]
    public async Task Inspect_rejects_files_that_are_not_usable_backups()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var folder = CloudFolder(db);
        Directory.CreateDirectory(folder);

        var text = Path.Combine(folder, "texto.db");
        await File.WriteAllTextAsync(text, "isto não é um banco");
        await Assert.ThrowsAsync<InvalidBackupException>(() => BackupService.InspectAsync(text));

        var otherApp = Path.Combine(folder, "outro-app.db");
        await using (var c = new SqliteConnection($"Data Source={otherApp};Pooling=False"))
        {
            await c.OpenAsync();
            await c.ExecuteAsync("CREATE TABLE fotos(id INTEGER); PRAGMA user_version = 1;");
        }
        await Assert.ThrowsAsync<InvalidBackupException>(() => BackupService.InspectAsync(otherApp));

        var backup = await new BackupService(db.Factory, db.Paths).CreateAsync(folder, T0);
        await using (var c = new SqliteConnection($"Data Source={backup.Path};Pooling=False"))
        {
            await c.OpenAsync();
            await c.ExecuteAsync("PRAGMA user_version = 99;");
        }
        var tooNew = await Assert.ThrowsAsync<InvalidBackupException>(() => BackupService.InspectAsync(backup.Path));
        Assert.Contains("mais nova", tooNew.Message);

        await Assert.ThrowsAsync<InvalidBackupException>(() => BackupService.InspectAsync(Path.Combine(folder, "sumiu.db")));
    }

    [Fact]
    public async Task Restore_happens_on_next_start_and_keeps_a_copy_of_the_replaced_data()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var planner = new PlannerRepository(db.Factory);
        var service = new BackupService(db.Factory, db.Paths);
        await planner.CreateGoalAsync(new GoalDraft("antes", GoalKind.Custom), T0);
        var backup = await service.CreateAsync(CloudFolder(db), T0);
        await planner.CreateGoalAsync(new GoalDraft("depois", GoalKind.Custom), T0);

        var summary = await service.StageRestoreAsync(backup.Path);

        Assert.Equal(1, summary.Goals);
        Assert.True(service.HasPendingRestore);
        Assert.Equal(["antes", "depois"], await GoalTitlesAsync(db.Factory)); // nada muda até reabrir

        var (factory, _, restore) = await AppDatabase.InitializeAsync(db.Paths);

        Assert.NotNull(restore);
        Assert.True(restore.Restored, restore.Error);
        Assert.False(service.HasPendingRestore);
        Assert.Equal(["antes"], await GoalTitlesAsync(factory));
        Assert.True(File.Exists(restore.SafetyCopyPath));
        Assert.Equal(2, (await BackupService.InspectAsync(restore.SafetyCopyPath!)).Goals);
        Assert.True(File.Exists(backup.Path)); // o backup escolhido continua lá
    }

    [Fact]
    public async Task Backup_from_an_older_version_is_upgraded_after_restore()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var old = Path.Combine(db.Paths.Root, "antigo.db");
        var oldFactory = new SqliteConnectionFactory(old);
        await new MigrationRunner(oldFactory, db.Paths.Backups, Schema.All.Take(1).ToList()).MigrateAsync();
        await using (var c = await oldFactory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO goals(title, priority, status, created_utc) VALUES ('meta da v1', 1, 0, 0)");

        await new BackupService(db.Factory, db.Paths).StageRestoreAsync(old);
        var (factory, migration, restore) = await AppDatabase.InitializeAsync(db.Paths);

        Assert.True(restore!.Restored, restore.Error);
        Assert.Equal((1, Schema.All[^1].Version), (migration.FromVersion, migration.ToVersion));
        Assert.Equal(["meta da v1"], await GoalTitlesAsync(factory));
    }

    [Fact]
    public async Task Broken_pending_restore_is_discarded_and_current_data_is_kept()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await new PlannerRepository(db.Factory).CreateGoalAsync(new GoalDraft("minha meta", GoalKind.Custom), T0);
        await File.WriteAllTextAsync(db.Paths.PendingRestore, "arquivo cortado no meio");

        var (factory, _, restore) = await AppDatabase.InitializeAsync(db.Paths);

        Assert.NotNull(restore);
        Assert.False(restore.Restored);
        Assert.NotNull(restore.Error);
        Assert.False(File.Exists(db.Paths.PendingRestore));
        Assert.Equal(["minha meta"], await GoalTitlesAsync(factory));
    }

    [Fact]
    public async Task Starting_without_a_pending_restore_changes_nothing()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();

        var (_, _, restore) = await AppDatabase.InitializeAsync(db.Paths);

        Assert.Null(restore);
        Assert.Empty(Directory.GetFiles(db.Paths.Backups));
    }
}
