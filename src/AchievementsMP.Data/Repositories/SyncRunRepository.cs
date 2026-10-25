using Dapper;

namespace AchievementsMP.Data.Repositories;

public enum SyncStatus { Running = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

public sealed record SyncRun(long Id, string Scope, long? AppId, DateTimeOffset StartedUtc, DateTimeOffset? FinishedUtc, SyncStatus Status, string? ErrorKind, string? ErrorMessage);

/// <summary>Histórico de sincronização: base para "dados desatualizados desde X" e para novas tentativas.</summary>
public sealed class SyncRunRepository(SqliteConnectionFactory factory)
{
    public const string LibraryScope = "library";
    public const string GameScope = "game";

    public async Task<long> StartAsync(string scope, long? appId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO sync_runs(scope, app_id, started_utc, status) VALUES (@scope, @appId, @ts, 0) RETURNING id",
            new { scope, appId, ts = Time.ToUnix(now) }, cancellationToken: ct));
    }

    public Task FinishAsync(long id, SyncStatus status, DateTimeOffset now, string? errorKind = null, string? errorMessage = null, CancellationToken ct = default) =>
        ExecAsync("UPDATE sync_runs SET finished_utc = @ts, status = @status, error_kind = @errorKind, error_message = @errorMessage WHERE id = @id",
            new { id, ts = Time.ToUnix(now), status = (int)status, errorKind, errorMessage }, ct);

    public async Task<SyncRun?> GetLastAsync(string scope, long? appId, SyncStatus? status = null, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, scope AS Scope, app_id AS AppId, started_utc AS StartedUtc, finished_utc AS FinishedUtc,
                   status AS Status, error_kind AS ErrorKind, error_message AS ErrorMessage
            FROM sync_runs
            WHERE scope = @scope AND app_id IS @appId AND (@status IS NULL OR status = @status)
            ORDER BY started_utc DESC, id DESC LIMIT 1
            """, new { scope, appId, status = (int?)status }, cancellationToken: ct));
        return row is null ? null : new SyncRun(row.Id, row.Scope, row.AppId, Time.FromUnix(row.StartedUtc)!.Value,
            Time.FromUnix(row.FinishedUtc), (SyncStatus)row.Status, row.ErrorKind, row.ErrorMessage);
    }

    /// <summary>Sincronizações que ficaram "rodando" porque o app fechou no meio são marcadas como canceladas na abertura.</summary>
    public Task<int> RecoverInterruptedAsync(DateTimeOffset now, CancellationToken ct = default) =>
        ExecAsync("UPDATE sync_runs SET status = 3, finished_utc = @ts, error_kind = 'interrupted' WHERE status = 0",
            new { ts = Time.ToUnix(now) }, ct);

    private async Task<int> ExecAsync(string sql, object args, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition(sql, args, cancellationToken: ct));
    }

    private sealed class Row
    {
        public long Id { get; init; }
        public string Scope { get; init; } = "";
        public long? AppId { get; init; }
        public long StartedUtc { get; init; }
        public long? FinishedUtc { get; init; }
        public long Status { get; init; }
        public string? ErrorKind { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
