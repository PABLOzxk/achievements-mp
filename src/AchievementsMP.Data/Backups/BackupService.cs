using System.Globalization;
using System.Text.RegularExpressions;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace AchievementsMP.Data.Backups;

/// <summary>Arquivo de backup encontrado numa pasta. A data vem do nome, que o próprio app gerou.</summary>
public sealed record BackupFile(string Path, DateTimeOffset CreatedUtc, long Bytes);

/// <summary>O que um backup contém, para o usuário conferir antes de restaurar.</summary>
public sealed record BackupSummary(int SchemaVersion, int Games, int Plans, int Notes, int Goals, int Tasks, int Guides, int Lists);

public sealed record RestoreOutcome(bool Restored, string? SafetyCopyPath, string? Error);

public sealed class InvalidBackupException(string message) : Exception(message);

/// <summary>
/// Cópias do banco inteiro numa pasta escolhida pelo usuário (de preferência fora do PC, como OneDrive).
/// É a única proteção dos dados pessoais: classificações, notas, metas e listas não existem na Steam.
/// Restaurar é feito na próxima abertura do app, antes de qualquer conexão, para nunca trocar um arquivo em uso.
/// </summary>
public sealed partial class BackupService(SqliteConnectionFactory factory, AppPaths paths)
{
    public const string FilePrefix = "AchievementsMP-backup-";
    public const int KeepCount = 10;
    public static readonly TimeSpan AutoInterval = TimeSpan.FromHours(24);
    private const string DateFormat = "yyyy-MM-dd_HHmmss";

    // Só arquivos com este formato exato são considerados (e apagados na limpeza); o resto da pasta nunca é tocado.
    [GeneratedRegex(@"^AchievementsMP-backup-(\d{4}-\d{2}-\d{2}_\d{6})(-\d+)?\.db$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();

    private static readonly string[] RequiredTables = ["games", "achievements", "player_achievements", "goals", "settings"];

    /// <summary>Faz um backup agora e mantém só os <see cref="KeepCount"/> mais recentes da pasta.</summary>
    public async Task<BackupFile> CreateAsync(string folder, DateTimeOffset now, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        foreach (var leftover in Directory.EnumerateFiles(folder, FilePrefix + "*.partial"))
            TryDelete(leftover);

        var stamp = now.ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"{FilePrefix}{stamp}.db");
        for (var i = 2; File.Exists(target); i++)
            target = Path.Combine(folder, $"{FilePrefix}{stamp}-{i}.db");

        // Grava num .partial e renomeia: um backup interrompido nunca parece um backup válido.
        var partial = target + ".partial";
        try
        {
            await using (var source = await factory.OpenAsync(ct))
                await CopyDatabaseAsync(source, partial, ct);
            File.Move(partial, target);
        }
        finally
        {
            TryDelete(partial);
        }

        Prune(folder);
        return new BackupFile(target, now.ToUniversalTime(), new FileInfo(target).Length);
    }

    /// <summary>Backup automático: só quando há pasta configurada e o último tem mais de <see cref="AutoInterval"/>.</summary>
    public async Task<BackupFile?> CreateIfDueAsync(string? folder, DateTimeOffset now, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        var latest = List(folder).FirstOrDefault();
        if (latest is not null && now - latest.CreatedUtc < AutoInterval)
            return null;
        return await CreateAsync(folder, now, ct);
    }

    /// <summary>Backups do app na pasta, do mais novo para o mais antigo.</summary>
    public static IReadOnlyList<BackupFile> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, FilePrefix + "*")
            .Select(path => (path, match: BackupName().Match(Path.GetFileName(path))))
            .Where(x => x.match.Success)
            .Select(x => new BackupFile(x.path, ParseStamp(x.match.Groups[1].Value), new FileInfo(x.path).Length))
            .OrderByDescending(b => b.CreatedUtc)
            .ThenByDescending(b => b.Path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Confere se o arquivo é um banco do app íntegro e de uma versão que este app sabe abrir.</summary>
    public static async Task<BackupSummary> InspectAsync(string file, CancellationToken ct = default)
    {
        if (!File.Exists(file))
            throw new InvalidBackupException("Arquivo não encontrado.");
        if (!await HasSqliteHeaderAsync(file, ct))
            throw new InvalidBackupException("Este arquivo não é um backup do Achievements MP.");

        var latest = Schema.All[^1].Version;
        try
        {
            await using var c = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await c.OpenAsync(ct);

            var check = await ScalarAsync<string>(c, "PRAGMA quick_check;", ct);
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidBackupException("O backup está corrompido e não pode ser usado.");

            var version = await MigrationRunner.GetVersionAsync(c, ct);
            if (version > latest)
                throw new InvalidBackupException($"O backup é de uma versão mais nova do app (banco v{version}). Atualize o app antes de restaurar.");

            var tables = new HashSet<string>(StringComparer.Ordinal);
            await using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    tables.Add(reader.GetString(0));
            }
            if (version < 1 || RequiredTables.Any(t => !tables.Contains(t)))
                throw new InvalidBackupException("Este arquivo não é um backup do Achievements MP.");

            async Task<int> Count(string table) =>
                tables.Contains(table) ? await ScalarAsync<int>(c, $"SELECT COUNT(*) FROM {table};", ct) : 0;

            return new BackupSummary(version, await Count("games"), await Count("achievement_plans"), await Count("achievement_notes"),
                await Count("goals"), await Count("plan_tasks"), await Count("guides"), await Count("user_lists"));
        }
        catch (SqliteException ex)
        {
            throw new InvalidBackupException($"Não foi possível ler o backup: {ex.Message}");
        }
    }

    /// <summary>Valida e deixa o backup pronto para ser aplicado na próxima abertura do app.</summary>
    public async Task<BackupSummary> StageRestoreAsync(string file, CancellationToken ct = default)
    {
        var summary = await InspectAsync(file, ct);
        var partial = paths.PendingRestore + ".partial";
        try
        {
            File.Copy(file, partial, overwrite: true);
            File.Move(partial, paths.PendingRestore, overwrite: true);
        }
        finally
        {
            TryDelete(partial);
        }
        return summary;
    }

    public bool HasPendingRestore => File.Exists(paths.PendingRestore);

    /// <summary>
    /// Roda na abertura, antes de qualquer conexão com o banco. Guarda uma cópia dos dados atuais em <c>backups/</c>
    /// e só então troca o arquivo. Qualquer falha deixa os dados atuais como estavam.
    /// </summary>
    public static async Task<RestoreOutcome?> ApplyPendingRestoreAsync(AppPaths paths, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!File.Exists(paths.PendingRestore))
            return null;

        try
        {
            await InspectAsync(paths.PendingRestore, ct);

            string? safety = null;
            if (File.Exists(paths.Database))
            {
                Directory.CreateDirectory(paths.Backups);
                safety = Path.Combine(paths.Backups, $"antes-de-restaurar-{now.ToLocalTime().ToString(DateFormat, CultureInfo.InvariantCulture)}.db");
                // Pela API de backup do SQLite: inclui o que ainda estiver no arquivo -wal.
                await using (var current = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = paths.Database,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false,
                }.ToString()))
                {
                    await current.OpenAsync(ct);
                    await CopyDatabaseAsync(current, safety, ct);
                }
            }

            // Um -wal antigo seria aplicado por cima do banco restaurado e o corromperia.
            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                TryDelete(paths.Database + suffix);
            File.Move(paths.PendingRestore, paths.Database, overwrite: true);
            return new RestoreOutcome(true, safety, null);
        }
        catch (Exception ex) when (ex is InvalidBackupException or IOException or UnauthorizedAccessException or SqliteException)
        {
            TryDelete(paths.PendingRestore);
            return new RestoreOutcome(false, null, ex.Message);
        }
    }

    private static async Task CopyDatabaseAsync(SqliteConnection source, string targetPath, CancellationToken ct)
    {
        await using var target = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = targetPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        await target.OpenAsync(ct);
        source.BackupDatabase(target);
        // Arquivo único: sem -wal ao lado, para poder ser copiado ou enviado para a nuvem sozinho.
        await using var cmd = target.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=DELETE;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void Prune(string folder)
    {
        foreach (var old in List(folder).Skip(KeepCount))
            TryDelete(old.Path);
    }

    private static DateTimeOffset ParseStamp(string stamp)
    {
        var local = DateTime.ParseExact(stamp, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    private static async Task<bool> HasSqliteHeaderAsync(string file, CancellationToken ct)
    {
        var header = "SQLite format 3\0"u8.ToArray();
        var buffer = new byte[header.Length];
        await using var stream = File.OpenRead(file);
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
        return read == header.Length && buffer.AsSpan().SequenceEqual(header);
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync(ct);
        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
