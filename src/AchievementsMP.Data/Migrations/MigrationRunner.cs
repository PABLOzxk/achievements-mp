using Microsoft.Data.Sqlite;

namespace AchievementsMP.Data.Migrations;

public sealed class DatabaseTooNewException(int found, int supported)
    : Exception($"O banco está na versão {found}, mas este app só conhece até a {supported}. Atualize o app; o banco não foi alterado.")
{
    public int FoundVersion { get; } = found;
    public int SupportedVersion { get; } = supported;
}

public sealed record MigrationResult(int FromVersion, int ToVersion, string? BackupPath);

/// <summary>
/// Aplica migrações pendentes. Antes de alterar um banco existente, faz uma cópia em <c>backups/</c>.
/// Cada migração roda numa transação: se falhar, o banco fica exatamente como estava.
/// A versão é guardada em <c>PRAGMA user_version</c>.
/// </summary>
public sealed class MigrationRunner(SqliteConnectionFactory factory, string backupDirectory, IReadOnlyList<Migration>? migrations = null)
{
    private readonly IReadOnlyList<Migration> _migrations = Validate(migrations ?? Schema.All);

    public async Task<MigrationResult> MigrateAsync(CancellationToken ct = default)
    {
        await using var connection = await factory.OpenAsync(ct);
        var current = await GetVersionAsync(connection, ct);
        var latest = _migrations.Count == 0 ? 0 : _migrations[^1].Version;

        if (current > latest)
            throw new DatabaseTooNewException(current, latest);
        if (current == latest)
            return new MigrationResult(current, current, null);

        string? backup = current > 0 ? await BackupAsync(connection, current, ct) : null;

        foreach (var migration in _migrations.Where(m => m.Version > current))
        {
            await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                // user_version não aceita parâmetro; o valor vem de código, não de entrada do usuário.
                cmd.CommandText = migration.Sql + $"\nPRAGMA user_version = {migration.Version};";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }

        return new MigrationResult(current, latest, backup);
    }

    public static async Task<int> GetVersionAsync(SqliteConnection connection, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task<string> BackupAsync(SqliteConnection source, int version, CancellationToken ct)
    {
        Directory.CreateDirectory(backupDirectory);
        var path = Path.Combine(backupDirectory, $"achievements-v{version}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await target.OpenAsync(ct);
        source.BackupDatabase(target);
        return path;
    }

    private static IReadOnlyList<Migration> Validate(IReadOnlyList<Migration> migrations)
    {
        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
                throw new InvalidOperationException($"Migrações devem ser sequenciais a partir de 1; encontrada versão {migrations[i].Version} na posição {i}.");
        }
        return migrations;
    }
}
