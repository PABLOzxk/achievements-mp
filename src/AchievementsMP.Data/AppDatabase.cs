using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Migrations;

namespace AchievementsMP.Data;

/// <summary>Ponto de entrada do banco: cria as pastas, aplica migrações e limpa sincronizações interrompidas.</summary>
public static class AppDatabase
{
    public static async Task<(SqliteConnectionFactory Factory, MigrationResult Migration)> InitializeAsync(AppPaths paths, CancellationToken ct = default)
    {
        paths.EnsureCreated();
        var factory = new SqliteConnectionFactory(paths.Database);
        var result = await new MigrationRunner(factory, paths.Backups).MigrateAsync(ct);
        await new Repositories.SyncRunRepository(factory).RecoverInterruptedAsync(DateTimeOffset.UtcNow, ct);
        return (factory, result);
    }
}
