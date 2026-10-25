using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Progress;
using Dapper;

namespace AchievementsMP.Data.Repositories;

public sealed record LibraryGame(
    long AppId,
    string Name,
    string? IconHash,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayedUtc,
    bool IsOwned,
    DataAvailability Availability,
    DateTimeOffset? PlayerSyncedUtc,
    GameProgress Progress);

/// <summary>
/// Escreve os dados oficiais da Steam. Todas as escritas são upserts idempotentes e nunca apagam linhas:
/// rodar a mesma sincronização duas vezes dá o mesmo resultado, e nada pessoal é tocado.
/// </summary>
public sealed class SteamCatalogRepository(SqliteConnectionFactory factory)
{
    /// <summary>
    /// Grava a biblioteca. Se <paramref name="isCompleteLibrary"/>, jogos ausentes são marcados como não possuídos
    /// (não apagados). Uma lista vazia nunca marca nada: quase sempre significa perfil privado, não biblioteca vazia.
    /// </summary>
    public async Task UpsertLibraryAsync(IReadOnlyCollection<Game> games, DateTimeOffset now, bool isCompleteLibrary, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        var ts = Time.ToUnix(now);
        foreach (var g in games.DistinctBy(g => g.AppId))
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO games(app_id, name, icon_hash, playtime_minutes, last_played_utc, has_community_stats, is_owned, first_seen_utc, library_synced_utc)
                VALUES (@AppId, @Name, @IconHash, @PlaytimeMinutes, @LastPlayed, @HasStats, 1, @ts, @ts)
                ON CONFLICT(app_id) DO UPDATE SET
                    name = excluded.name,
                    icon_hash = COALESCE(excluded.icon_hash, games.icon_hash),
                    playtime_minutes = excluded.playtime_minutes,
                    last_played_utc = COALESCE(excluded.last_played_utc, games.last_played_utc),
                    has_community_stats = COALESCE(excluded.has_community_stats, games.has_community_stats),
                    is_owned = 1,
                    library_synced_utc = excluded.library_synced_utc
                """,
                new { g.AppId, g.Name, g.IconHash, g.PlaytimeMinutes, LastPlayed = Time.ToUnix(g.LastPlayedUtc), HasStats = g.HasCommunityStats, ts },
                tx, cancellationToken: ct));
        }

        if (isCompleteLibrary && games.Count > 0)
        {
            await c.ExecuteAsync(new CommandDefinition(
                "UPDATE games SET is_owned = 0 WHERE library_synced_utc < @ts AND is_owned = 1",
                new { ts }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>Grava o esquema de conquistas. Conquistas que sumiram do esquema são marcadas como removidas, não apagadas.</summary>
    public async Task UpsertSchemaAsync(long appId, IReadOnlyCollection<AchievementDefinition> schema, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);

        foreach (var a in schema.Where(a => a.AppId == appId).DistinctBy(a => a.ApiName, StringComparer.Ordinal))
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO achievements(app_id, api_name, display_name, description, hidden, icon_url, icon_gray_url, is_removed)
                VALUES (@AppId, @ApiName, @DisplayName, @Description, @Hidden, @IconUrl, @IconGrayUrl, 0)
                ON CONFLICT(app_id, api_name) DO UPDATE SET
                    display_name = excluded.display_name,
                    description = COALESCE(excluded.description, achievements.description),
                    hidden = excluded.hidden,
                    icon_url = excluded.icon_url,
                    icon_gray_url = excluded.icon_gray_url,
                    is_removed = 0
                """, a, tx, cancellationToken: ct));
        }

        var names = schema.Select(a => a.ApiName).ToArray();
        await c.ExecuteAsync(new CommandDefinition(
            names.Length == 0
                ? "UPDATE achievements SET is_removed = 1 WHERE app_id = @appId"
                : "UPDATE achievements SET is_removed = 1 WHERE app_id = @appId AND api_name NOT IN @names",
            new { appId, names }, tx, cancellationToken: ct));

        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE games SET schema_synced_utc = @ts, availability = CASE WHEN @empty THEN @none ELSE availability END WHERE app_id = @appId",
            new { appId, ts = Time.ToUnix(now), empty = names.Length == 0, none = (int)DataAvailability.NoAchievements }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Grava o estado do jogador. Só chame com uma resposta bem-sucedida da Steam: conquistas que a resposta
    /// não menciona continuam com o estado anterior (ou desconhecido), nunca viram "pendente".
    /// </summary>
    public async Task ApplyPlayerStatesAsync(long appId, IReadOnlyCollection<PlayerAchievementState> states, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var ts = Time.ToUnix(now);

        foreach (var s in states.Where(s => s.AppId == appId && s.State != UnlockState.Unknown))
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO player_achievements(app_id, api_name, state, unlocked_utc, synced_utc)
                VALUES (@AppId, @ApiName, @State, @Unlocked, @ts)
                ON CONFLICT(app_id, api_name) DO UPDATE SET
                    state = excluded.state, unlocked_utc = excluded.unlocked_utc, synced_utc = excluded.synced_utc
                """,
                new { s.AppId, s.ApiName, State = (int)s.State, Unlocked = Time.ToUnix(s.UnlockedAtUtc), ts }, tx, cancellationToken: ct));
        }

        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE games SET player_synced_utc = @ts, availability = @available WHERE app_id = @appId",
            new { appId, ts, available = (int)DataAvailability.Available }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task UpsertGlobalPercentagesAsync(long appId, IReadOnlyDictionary<string, double> percentByApiName, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var ts = Time.ToUnix(now);
        foreach (var (apiName, percent) in percentByApiName)
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO achievement_global_stats(app_id, api_name, percent, updated_utc) VALUES (@appId, @apiName, @percent, @ts)
                ON CONFLICT(app_id, api_name) DO UPDATE SET percent = excluded.percent, updated_utc = excluded.updated_utc
                """, new { appId, apiName, percent, ts }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Registra por que os dados de um jogo estão incompletos, sem alterar o progresso já salvo.</summary>
    public async Task SetAvailabilityAsync(long appId, DataAvailability availability, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE games SET availability = @availability WHERE app_id = @appId",
            new { appId, availability = (int)availability }, cancellationToken: ct));
    }

    public async Task<GameProgress> GetProgressAsync(long appId, CancellationToken ct = default)
    {
        var games = await QueryLibraryAsync("WHERE g.app_id = @appId", new { appId }, ct);
        return games.SingleOrDefault()?.Progress ?? GameProgress.Empty;
    }

    public Task<IReadOnlyList<LibraryGame>> GetLibraryAsync(bool includeNotOwned = false, CancellationToken ct = default) =>
        QueryLibraryAsync(includeNotOwned ? "" : "WHERE g.is_owned = 1", null, ct);

    private async Task<IReadOnlyList<LibraryGame>> QueryLibraryAsync(string where, object? args, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        // Conquista sem linha em player_achievements = desconhecida (não pendente).
        var rows = await c.QueryAsync<LibraryRow>(new CommandDefinition($"""
            SELECT g.app_id AS AppId, g.name AS Name, g.icon_hash AS IconHash, g.playtime_minutes AS PlaytimeMinutes,
                   g.last_played_utc AS LastPlayedUtc, g.is_owned AS IsOwned, g.availability AS Availability,
                   g.player_synced_utc AS PlayerSyncedUtc,
                   COUNT(a.api_name) AS Total,
                   COALESCE(SUM(CASE WHEN p.state = 2 THEN 1 ELSE 0 END), 0) AS Unlocked,
                   COALESCE(SUM(CASE WHEN p.state = 1 THEN 1 ELSE 0 END), 0) AS Locked
            FROM games g
            LEFT JOIN achievements a ON a.app_id = g.app_id AND a.is_removed = 0
            LEFT JOIN player_achievements p ON p.app_id = a.app_id AND p.api_name = a.api_name
            {where}
            GROUP BY g.app_id
            ORDER BY g.name COLLATE NOCASE
            """, args, cancellationToken: ct));

        return rows.Select(r => new LibraryGame(
            r.AppId, r.Name, r.IconHash, (int)r.PlaytimeMinutes, Time.FromUnix(r.LastPlayedUtc), r.IsOwned != 0,
            (DataAvailability)r.Availability, Time.FromUnix(r.PlayerSyncedUtc),
            new GameProgress((int)r.Total, (int)r.Unlocked, (int)r.Locked, (int)(r.Total - r.Unlocked - r.Locked)))).ToList();
    }

    private sealed class LibraryRow
    {
        public long AppId { get; init; }
        public string Name { get; init; } = "";
        public string? IconHash { get; init; }
        public long PlaytimeMinutes { get; init; }
        public long? LastPlayedUtc { get; init; }
        public long IsOwned { get; init; }
        public long Availability { get; init; }
        public long? PlayerSyncedUtc { get; init; }
        public long Total { get; init; }
        public long Unlocked { get; init; }
        public long Locked { get; init; }
    }
}
