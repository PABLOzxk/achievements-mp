using System.Globalization;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Migrations;
using Microsoft.Data.Sqlite;

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

    /// <summary>O arquivo do banco não é um banco SQLite ou está corrompido (disco com defeito, cópia cortada…).</summary>
    public static bool IsDamaged(Exception ex) => ex is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADatabase };

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    /// <summary>
    /// Para começar do zero quando o banco está danificado: move os arquivos para <c>backups/</c> em vez de apagar,
    /// e a próxima abertura cria um banco novo. Devolve onde o arquivo ficou.
    /// </summary>
    public static string SetAsideDamaged(AppPaths paths, DateTimeOffset now)
    {
        Directory.CreateDirectory(paths.Backups);
        var stamp = now.ToLocalTime().ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(paths.Backups, $"danificado-{stamp}.db");
        for (var i = 2; File.Exists(target); i++)
            target = Path.Combine(paths.Backups, $"danificado-{stamp}-{i}.db");
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(paths.Database + suffix))
                File.Move(paths.Database + suffix, target + suffix);
        File.Move(paths.Database, target);
        return target;
    }
}
