using AchievementsMP.Core.Domain;

namespace AchievementsMP.Core.Sync;

/// <summary>
/// Decide o mínimo de consultas por jogo. A primeira sincronização lê tudo; as seguintes só o que mudou
/// (tempo de jogo diferente, jogado desde a última leitura, falha anterior, idioma trocado ou dado velho).
/// </summary>
public static class SyncPlanner
{
    public static IReadOnlyList<GameSyncTask> Plan(IEnumerable<GameSyncState> games, string language, DateTimeOffset now, SyncPolicy policy, bool force = false)
    {
        var tasks = new List<(GameSyncTask Task, DateTimeOffset? LastPlayed)>();

        foreach (var g in games.Where(g => g.IsOwned))
        {
            var work = PlanGame(g, language, now, policy, force);
            if (work != GameSyncWork.None)
                tasks.Add((new GameSyncTask(g.AppId, g.Name, work, g.AchievementCount > 0), g.LastPlayedUtc));
        }

        // Jogados recentemente primeiro: é o que você quer ver atualizado antes.
        return tasks
            .OrderByDescending(t => t.LastPlayed ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.Task.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => t.Task)
            .ToList();
    }

    internal static GameSyncWork PlanGame(GameSyncState g, string language, DateTimeOffset now, SyncPolicy policy, bool force)
    {
        if (g.Availability == DataAvailability.NoAchievements)
        {
            var recheck = force || g.SchemaSyncedUtc is null || now - g.SchemaSyncedUtc.Value > policy.NoAchievementsRecheck;
            return recheck ? GameSyncWork.Schema : GameSyncWork.None;
        }

        var schemaStale = force
            || g.SchemaSyncedUtc is null
            || !string.Equals(g.SchemaLanguage, language, StringComparison.OrdinalIgnoreCase)
            || now - g.SchemaSyncedUtc.Value > policy.SchemaMaxAge;

        var playerStale = force
            || schemaStale
            || g.PlayerSyncedUtc is null
            || g.PlayerSyncedPlaytime != g.PlaytimeMinutes
            || (g.LastPlayedUtc is { } played && played > g.PlayerSyncedUtc.Value)
            || now - g.PlayerSyncedUtc.Value > policy.PlayerMaxAge
            || g.Availability is DataAvailability.SyncFailed or DataAvailability.PrivateProfile or DataAvailability.SchemaUnavailable;

        return (schemaStale ? GameSyncWork.Schema : GameSyncWork.None) | (playerStale ? GameSyncWork.Player : GameSyncWork.None);
    }
}
