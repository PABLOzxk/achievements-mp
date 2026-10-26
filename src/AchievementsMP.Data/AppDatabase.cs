using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Migrations;

namespace AchievementsMP.Data;

/// <summary>
/// Ponto de entrada do banco: cria as pastas, aplica uma restauração pendente, aplica migrações e limpa
/// sincronizações interrompidas.
/// </summary>
public static class AppDatabase
{
    public static async Task<(SqliteConnectionFactory Factory, MigrationResult Migration, RestoreOutcome? Restore)> InitializeAsync(
        AppPaths paths, CancellationToken ct = default)
    {
        paths.EnsureCreated();
        // Antes de qualquer conexão: o arquivo do banco só pode ser trocado enquanto ninguém o usa.
        var restore = await BackupService.ApplyPendingRestoreAsync(paths, DateTimeOffset.UtcNow, ct);
        var factory = new SqliteConnectionFactory(paths.Database);
        var result = await new MigrationRunner(factory, paths.Backups).MigrateAsync(ct);
        await new Repositories.SyncRunRepository(factory).RecoverInterruptedAsync(DateTimeOffset.UtcNow, ct);
        return (factory, result, restore);
    }
}
