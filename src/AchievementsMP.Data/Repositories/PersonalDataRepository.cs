using AchievementsMP.Core.Domain;
using Dapper;

namespace AchievementsMP.Data.Repositories;

/// <summary>Listas e favoritos. Só a UI escreve aqui; a sincronização nunca. O planejador fica em <see cref="PlannerRepository"/>.</summary>
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

    public async Task RemoveGameFromListAsync(long listId, long appId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM user_list_games WHERE list_id = @listId AND app_id = @appId", new { listId, appId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UserList>> GetListsAsync(CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = await c.QueryAsync<(long Id, string Name, string? Description, long IsFavorites, long CreatedUtc)>(new CommandDefinition(
            "SELECT id, name, description, is_favorites, created_utc FROM user_lists ORDER BY is_favorites DESC, name COLLATE NOCASE",
            cancellationToken: ct));
        return rows.Select(r => new UserList(r.Id, r.Name, r.Description, r.IsFavorites != 0, Time.FromUnix(r.CreatedUtc)!.Value)).ToList();
    }

    /// <summary>A lista "Favoritos" é criada na primeira vez que for usada.</summary>
    public async Task<long> GetOrCreateFavoritesAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using (var c = await factory.OpenAsync(ct))
        {
            var id = await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
                "SELECT id FROM user_lists WHERE is_favorites = 1 ORDER BY id LIMIT 1", cancellationToken: ct));
            if (id is { } existing) return existing;
        }
        return await CreateListAsync("Favoritos", null, now, isFavorites: true, ct);
    }

    public async Task RenameListAsync(long listId, string name, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE user_lists SET name = @name WHERE id = @listId", new { listId, name }, cancellationToken: ct));
    }

    /// <summary>Apaga a lista e suas associações. Os jogos e tudo mais continuam intactos.</summary>
    public async Task DeleteListAsync(long listId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM user_lists WHERE id = @listId", new { listId }, cancellationToken: ct));
    }

    /// <summary>Listas que contêm cada jogo (app_id → ids das listas).</summary>
    public async Task<ILookup<long, long>> GetMembershipAsync(CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = await c.QueryAsync<(long ListId, long AppId)>(new CommandDefinition(
            "SELECT list_id, app_id FROM user_list_games", cancellationToken: ct));
        return rows.ToLookup(r => r.AppId, r => r.ListId);
    }

    public async Task<IReadOnlyList<long>> GetListGamesAsync(long listId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return (await c.QueryAsync<long>(new CommandDefinition(
            "SELECT app_id FROM user_list_games WHERE list_id = @listId ORDER BY added_utc, app_id", new { listId }, cancellationToken: ct))).ToList();
    }
}
