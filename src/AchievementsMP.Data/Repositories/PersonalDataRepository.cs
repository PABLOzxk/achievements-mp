using AchievementsMP.Core.Domain;
using Dapper;

namespace AchievementsMP.Data.Repositories;

/// <summary>Listas, anotações e afins. Só a UI escreve aqui; a sincronização nunca.</summary>
public sealed class PersonalDataRepository(SqliteConnectionFactory factory)
{
    public async Task<long> CreateListAsync(string name, string? description, DateTimeOffset now, bool isFavorites = false, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO user_lists(name, description, is_favorites, created_utc) VALUES (@name, @description, @isFavorites, @ts) RETURNING id",
            new { name, description, isFavorites, ts = Time.ToUnix(now) }, cancellationToken: ct));
    }

    public async Task AddGameToListAsync(long listId, long appId, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT OR IGNORE INTO user_list_games(list_id, app_id, added_utc) VALUES (@listId, @appId, @ts)",
            new { listId, appId, ts = Time.ToUnix(now) }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<long>> GetListGamesAsync(long listId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return (await c.QueryAsync<long>(new CommandDefinition(
            "SELECT app_id FROM user_list_games WHERE list_id = @listId ORDER BY added_utc, app_id", new { listId }, cancellationToken: ct))).ToList();
    }

    public async Task SetAchievementNoteAsync(long appId, string apiName, string note, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO achievement_notes(app_id, api_name, note, updated_utc) VALUES (@appId, @apiName, @note, @ts)
            ON CONFLICT(app_id, api_name) DO UPDATE SET note = excluded.note, updated_utc = excluded.updated_utc
            """, new { appId, apiName, note, ts = Time.ToUnix(now) }, cancellationToken: ct));
    }

    public async Task<string?> GetAchievementNoteAsync(long appId, string apiName, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT note FROM achievement_notes WHERE app_id = @appId AND api_name = @apiName", new { appId, apiName }, cancellationToken: ct));
    }

    public async Task<long> AddPlanTaskAsync(long appId, string? apiName, string title, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO plan_tasks(app_id, api_name, title, sort_order)
            VALUES (@appId, @apiName, @title, COALESCE((SELECT MAX(sort_order) + 1 FROM plan_tasks WHERE app_id = @appId), 0))
            RETURNING id
            """, new { appId, apiName, title }, cancellationToken: ct));
    }

    public async Task<int> CountPlanTasksAsync(long appId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM plan_tasks WHERE app_id = @appId", new { appId }, cancellationToken: ct));
    }
}
