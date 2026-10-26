using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Planning;
using Dapper;

namespace AchievementsMP.Data.Repositories;

/// <summary>
/// Planejador: classificação das conquistas, anotações, guias, tarefas e metas. Só a UI escreve aqui; a sincronização nunca.
/// Nenhuma tabela daqui tem chave estrangeira para as tabelas da Steam, então nada que a Steam mude apaga estes dados.
/// </summary>
public sealed class PlannerRepository(SqliteConnectionFactory factory)
{
    // ── Classificação e anotações ─────────────────────────────────────────────

    /// <summary>Classificação e anotação de cada conquista do jogo que tem alguma (chave: api_name).</summary>
    public async Task<IReadOnlyDictionary<string, AchievementPlan>> GetPlansAsync(long appId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var plans = await c.QueryAsync<PlanRow>(new CommandDefinition("""
            SELECT app_id AS AppId, api_name AS ApiName, effort AS EffortCode, missable AS MissableCode, priority AS PriorityCode,
                   requirement AS Requirement, source AS Source
            FROM achievement_plans WHERE app_id = @appId
            """, new { appId }, cancellationToken: ct));
        var notes = await c.QueryAsync<(string ApiName, string Note)>(new CommandDefinition(
            "SELECT api_name, note FROM achievement_notes WHERE app_id = @appId", new { appId }, cancellationToken: ct));

        var result = new Dictionary<string, AchievementPlan>(StringComparer.Ordinal);
        foreach (var p in plans)
            result[p.ApiName] = p.ToPlan(null);
        foreach (var (apiName, note) in notes)
            result[apiName] = (result.TryGetValue(apiName, out var existing) ? existing : AchievementPlan.Empty(appId, apiName)) with { Note = note };
        return result;
    }

    /// <summary>Grava classificação e anotação juntas. Tudo vazio apaga a linha (equivale a "não sei").</summary>
    public async Task SavePlanAsync(AchievementPlan plan, DateTimeOffset now, CancellationToken ct = default)
    {
        plan = plan.Normalized();
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var key = new { plan.AppId, plan.ApiName };

        if (plan with { Note = null } is { IsEmpty: true })
        {
            await c.ExecuteAsync(new CommandDefinition(
                "DELETE FROM achievement_plans WHERE app_id = @AppId AND api_name = @ApiName", key, tx, cancellationToken: ct));
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO achievement_plans(app_id, api_name, effort, missable, priority, requirement, source, updated_utc)
                VALUES (@AppId, @ApiName, @Effort, @Missable, @Priority, @Requirement, @Source, @ts)
                ON CONFLICT(app_id, api_name) DO UPDATE SET
                    effort = excluded.effort, missable = excluded.missable, priority = excluded.priority,
                    requirement = excluded.requirement, source = excluded.source, updated_utc = excluded.updated_utc
                """,
                new
                {
                    plan.AppId, plan.ApiName, Effort = (int)plan.Effort, Missable = (int)plan.Missable, Priority = (int)plan.Priority,
                    plan.Requirement, plan.Source, ts = Time.ToUnix(now),
                }, tx, cancellationToken: ct));
        }

        if (plan.Note is null)
            await c.ExecuteAsync(new CommandDefinition(
                "DELETE FROM achievement_notes WHERE app_id = @AppId AND api_name = @ApiName", key, tx, cancellationToken: ct));
        else
            await UpsertNoteAsync(c, plan.AppId, plan.ApiName, plan.Note, now, tx, ct);

        await tx.CommitAsync(ct);
    }

    public async Task SetAchievementNoteAsync(long appId, string apiName, string note, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await UpsertNoteAsync(c, appId, apiName, note, now, null, ct);
    }

    public async Task<string?> GetAchievementNoteAsync(long appId, string apiName, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT note FROM achievement_notes WHERE app_id = @appId AND api_name = @apiName", new { appId, apiName }, cancellationToken: ct));
    }

    private static Task UpsertNoteAsync(Microsoft.Data.Sqlite.SqliteConnection c, long appId, string apiName, string note, DateTimeOffset now,
        System.Data.Common.DbTransaction? tx, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO achievement_notes(app_id, api_name, note, updated_utc) VALUES (@appId, @apiName, @note, @ts)
            ON CONFLICT(app_id, api_name) DO UPDATE SET note = excluded.note, updated_utc = excluded.updated_utc
            """, new { appId, apiName, note, ts = Time.ToUnix(now) }, tx, cancellationToken: ct));

    /// <summary>Pendentes marcadas como perdíveis ou de alta prioridade, em jogos da biblioteca. Perdíveis primeiro.</summary>
    public async Task<IReadOnlyList<FlaggedAchievement>> GetFlaggedPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = await c.QueryAsync<FlaggedRow>(new CommandDefinition("""
            SELECT g.app_id AS AppId, g.name AS GameName,
                   a.api_name AS ApiName, a.display_name AS DisplayName, a.description AS Description, a.hidden AS Hidden,
                   a.icon_url AS IconUrl, a.icon_gray_url AS IconGrayUrl, p.unlocked_utc AS UnlockedUtc, gs.percent AS GlobalPercent,
                   ap.effort AS EffortCode, ap.missable AS MissableCode, ap.priority AS PriorityCode, ap.requirement AS Requirement, ap.source AS Source,
                   n.note AS Note
            FROM achievement_plans ap
            JOIN achievements a ON a.app_id = ap.app_id AND a.api_name = ap.api_name AND a.is_removed = 0
            JOIN games g ON g.app_id = ap.app_id AND g.is_owned = 1
            JOIN player_achievements p ON p.app_id = ap.app_id AND p.api_name = ap.api_name AND p.state = 1
            LEFT JOIN achievement_global_stats gs ON gs.app_id = ap.app_id AND gs.api_name = ap.api_name
            LEFT JOIN achievement_notes n ON n.app_id = ap.app_id AND n.api_name = ap.api_name
            WHERE ap.missable = 1 OR ap.priority = 2
            ORDER BY (ap.missable = 1) DESC, ap.priority DESC, gs.percent DESC, g.name COLLATE NOCASE, a.display_name COLLATE NOCASE
            LIMIT @limit
            """, new { limit }, cancellationToken: ct));
        return rows.Select(r => new FlaggedAchievement(
            r.AppId, r.GameName,
            new AchievementDetail(r.AppId, r.ApiName, r.DisplayName, r.Description, r.Hidden != 0, r.IconUrl, r.IconGrayUrl,
                UnlockState.Locked, Time.FromUnix(r.UnlockedUtc), r.GlobalPercent),
            r.ToPlan(r.Note))).ToList();
    }

    /// <summary>Jogos com algo planejado (classificação, anotação, guia, tarefa ou meta ativa).</summary>
    public async Task<IReadOnlySet<long>> GetPlannedGameIdsAsync(CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var ids = await c.QueryAsync<long>(new CommandDefinition("""
            SELECT app_id FROM achievement_plans
            UNION SELECT app_id FROM achievement_notes
            UNION SELECT app_id FROM guides
            UNION SELECT app_id FROM plan_tasks
            UNION SELECT app_id FROM goals WHERE app_id IS NOT NULL AND status = 0
            """, cancellationToken: ct));
        return ids.ToHashSet();
    }

    // ── Guias ────────────────────────────────────────────────────────────────

    /// <summary>Guias do jogo: os do jogo inteiro (api_name nulo) e os de cada conquista.</summary>
    public async Task<IReadOnlyList<Guide>> GetGuidesAsync(long appId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = await c.QueryAsync<(long Id, long AppId, string? ApiName, string Url, string? Title, string? Language, string? Source)>(
            new CommandDefinition("""
                SELECT id, app_id, api_name, url, title, language, source FROM guides
                WHERE app_id = @appId ORDER BY created_utc, id
                """, new { appId }, cancellationToken: ct));
        // Um valor inválido no banco (editado à mão) é ignorado em vez de quebrar a tela.
        return rows
            .Select(r => Uri.TryCreate(r.Url, UriKind.Absolute, out var url) && GuideLink.IsSafe(url)
                ? new Guide(r.Id, r.AppId, r.ApiName, url, r.Title, r.Language, r.Source)
                : null)
            .OfType<Guide>()
            .ToList();
    }

    public async Task<long> AddGuideAsync(long appId, string? apiName, Uri url, string? title, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!GuideLink.IsSafe(url)) throw new ArgumentException("Só links http ou https.", nameof(url));
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO guides(app_id, api_name, url, title, created_utc) VALUES (@appId, @apiName, @url, @title, @ts) RETURNING id
            """, new { appId, apiName, url = url.AbsoluteUri, title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), ts = Time.ToUnix(now) },
            cancellationToken: ct));
    }

    public async Task DeleteGuideAsync(long id, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM guides WHERE id = @id", new { id }, cancellationToken: ct));
    }

    // ── Tarefas (checklist do jogo ou de uma conquista) ─────────────────────

    /// <summary>Tarefas do jogo: as do jogo inteiro (api_name nulo) e as de cada conquista.</summary>
    public async Task<IReadOnlyList<PlanTask>> GetTasksAsync(long appId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = await c.QueryAsync<(long Id, long AppId, string? ApiName, string Title, string? Notes, long SortOrder, long? DoneUtc)>(
            new CommandDefinition("""
                SELECT id, app_id, api_name, title, notes, sort_order, done_utc FROM plan_tasks
                WHERE app_id = @appId ORDER BY sort_order, id
                """, new { appId }, cancellationToken: ct));
        return rows.Select(r => new PlanTask(r.Id, r.AppId, r.ApiName, r.Title, r.Notes, (int)r.SortOrder, Time.FromUnix(r.DoneUtc))).ToList();
    }

    public async Task<long> AddTaskAsync(long appId, string? apiName, string title, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO plan_tasks(app_id, api_name, title, sort_order, created_utc)
            VALUES (@appId, @apiName, @title, COALESCE((SELECT MAX(sort_order) + 1 FROM plan_tasks WHERE app_id = @appId), 0), @ts)
            RETURNING id
            """, new { appId, apiName, title = title.Trim(), ts = Time.ToUnix(now) }, cancellationToken: ct));
    }

    /// <summary>Marca (com a data) ou desmarca uma tarefa.</summary>
    public async Task SetTaskDoneAsync(long id, DateTimeOffset? doneUtc, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE plan_tasks SET done_utc = @done WHERE id = @id", new { id, done = Time.ToUnix(doneUtc) }, cancellationToken: ct));
    }

    /// <summary>Passa a tarefa para uma conquista (ou, com nulo, para o jogo inteiro). Ela vai para o fim da lista nova.</summary>
    public async Task MoveTaskToAsync(long id, string? apiName, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE plan_tasks
            SET api_name = @apiName, sort_order = (SELECT MAX(t.sort_order) + 1 FROM plan_tasks t WHERE t.app_id = plan_tasks.app_id)
            WHERE id = @id
            """, new { id, apiName }, cancellationToken: ct));
    }

    public async Task RenameTaskAsync(long id, string title, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE plan_tasks SET title = @title WHERE id = @id", new { id, title = title.Trim() }, cancellationToken: ct));
    }

    public async Task DeleteTaskAsync(long id, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM plan_tasks WHERE id = @id", new { id }, cancellationToken: ct));
    }

    /// <summary>
    /// Troca a tarefa de lugar com a vizinha de cima (-1) ou de baixo (+1) da mesma lista (a do jogo ou a da mesma conquista).
    /// Nas pontas, não faz nada.
    /// </summary>
    public async Task MoveTaskAsync(long id, int direction, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var task = await c.QueryFirstOrDefaultAsync<TaskPosition>(new CommandDefinition(
            "SELECT id AS Id, app_id AS AppId, api_name AS ApiName, sort_order AS SortOrder FROM plan_tasks WHERE id = @id", new { id }, tx, cancellationToken: ct));
        if (task is not { } t) return;

        // "IS" também compara nulo com nulo: as tarefas do jogo inteiro formam uma lista própria.
        var n = await c.QueryFirstOrDefaultAsync<TaskPosition>(new CommandDefinition(direction < 0
                ? "SELECT id AS Id, app_id AS AppId, api_name AS ApiName, sort_order AS SortOrder FROM plan_tasks WHERE app_id = @AppId AND api_name IS @ApiName AND (sort_order < @SortOrder OR (sort_order = @SortOrder AND id < @Id)) ORDER BY sort_order DESC, id DESC LIMIT 1"
                : "SELECT id AS Id, app_id AS AppId, api_name AS ApiName, sort_order AS SortOrder FROM plan_tasks WHERE app_id = @AppId AND api_name IS @ApiName AND (sort_order > @SortOrder OR (sort_order = @SortOrder AND id > @Id)) ORDER BY sort_order, id LIMIT 1",
            new { t.AppId, t.ApiName, t.SortOrder, t.Id }, tx, cancellationToken: ct));
        if (n is null) return;

        // Ordem empatada (dados antigos): separa antes de trocar.
        var (mine, theirs) = n.SortOrder == t.SortOrder
            ? direction < 0 ? (t.SortOrder, t.SortOrder + 1) : (t.SortOrder + 1, t.SortOrder)
            : (n.SortOrder, t.SortOrder);
        await c.ExecuteAsync(new CommandDefinition("UPDATE plan_tasks SET sort_order = @mine WHERE id = @id", new { mine, id }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("UPDATE plan_tasks SET sort_order = @theirs WHERE id = @nid", new { theirs, nid = n.Id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<int> CountTasksAsync(long appId, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM plan_tasks WHERE app_id = @appId", new { appId }, cancellationToken: ct));
    }

    // ── Metas ────────────────────────────────────────────────────────────────

    public async Task<long> CreateGoalAsync(GoalDraft draft, DateTimeOffset now, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO goals(title, kind, app_id, api_name, target, period, deadline_utc, priority, status, created_utc)
            VALUES (@Title, @Kind, @AppId, @ApiName, @Target, @Period, @Deadline, @Priority, 0, @ts)
            RETURNING id
            """,
            new
            {
                Title = draft.Title.Trim(), Kind = (int)draft.Kind, draft.AppId, draft.ApiName, draft.Target, Period = (int)draft.Period,
                Deadline = Time.ToUnix(draft.DeadlineUtc), Priority = (int)draft.Priority, ts = Time.ToUnix(now),
            }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Goal>> GetGoalsAsync(CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = await c.QueryAsync<GoalRow>(new CommandDefinition("""
            SELECT id AS Id, title AS Title, kind AS Kind, app_id AS AppId, api_name AS ApiName, target AS Target, period AS Period,
                   deadline_utc AS DeadlineUtc, priority AS Priority, status AS Status, created_utc AS CreatedUtc, done_utc AS DoneUtc
            FROM goals ORDER BY id
            """, cancellationToken: ct));
        return rows.Select(r => new Goal(
            r.Id, r.Title, (GoalKind)r.Kind, r.AppId, r.ApiName, (int?)r.Target, (GoalPeriod)r.Period, Time.FromUnix(r.DeadlineUtc),
            (Priority)r.Priority, (GoalStatus)r.Status, Time.FromUnix(r.CreatedUtc)!.Value, Time.FromUnix(r.DoneUtc))).ToList();
    }

    public async Task SetGoalStatusAsync(long id, GoalStatus status, DateTimeOffset? doneUtc, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE goals SET status = @status, done_utc = @done WHERE id = @id",
            new { id, status = (int)status, done = Time.ToUnix(doneUtc) }, cancellationToken: ct));
    }

    public async Task DeleteGoalAsync(long id, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM goals WHERE id = @id", new { id }, cancellationToken: ct));
    }

    /// <summary>Conquistas desbloqueadas em [início, fim), num jogo ou em todos. Usa a data de desbloqueio informada pela Steam.</summary>
    public async Task<int> CountUnlocksAsync(long? appId, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM player_achievements p
            JOIN achievements a ON a.app_id = p.app_id AND a.api_name = p.api_name AND a.is_removed = 0
            WHERE p.state = 2 AND p.unlocked_utc >= @start AND (@end IS NULL OR p.unlocked_utc < @end)
              AND (@appId IS NULL OR p.app_id = @appId)
            """, new { appId, start = Time.ToUnix(start), end = Time.ToUnix(end) }, cancellationToken: ct));
    }

    private class PlanRow
    {
        public long AppId { get; init; }
        public string ApiName { get; init; } = "";
        public long EffortCode { get; init; }
        public long MissableCode { get; init; }
        public long PriorityCode { get; init; }
        public string? Requirement { get; init; }
        public string? Source { get; init; }

        // Valor desconhecido no banco (versão futura, edição manual) cai no padrão em vez de virar um enum inválido.
        public AchievementPlan ToPlan(string? note) => new(
            AppId, ApiName, OrDefault((Effort)EffortCode, Effort.Unknown), OrDefault((Missable)MissableCode, Missable.Unknown),
            OrDefault((Priority)PriorityCode, Priority.Normal), Requirement, Source, note);

        private static T OrDefault<T>(T value, T fallback) where T : struct, Enum => Enum.IsDefined(value) ? value : fallback;
    }

    private sealed class FlaggedRow : PlanRow
    {
        public string GameName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string? Description { get; init; }
        public long Hidden { get; init; }
        public string? IconUrl { get; init; }
        public string? IconGrayUrl { get; init; }
        public long? UnlockedUtc { get; init; }
        public double? GlobalPercent { get; init; }
        public string? Note { get; init; }
    }

    private sealed class TaskPosition
    {
        public long Id { get; init; }
        public long AppId { get; init; }
        public string? ApiName { get; init; }
        public long SortOrder { get; init; }
    }

    private sealed class GoalRow
    {
        public long Id { get; init; }
        public string Title { get; init; } = "";
        public long Kind { get; init; }
        public long? AppId { get; init; }
        public string? ApiName { get; init; }
        public long? Target { get; init; }
        public long Period { get; init; }
        public long? DeadlineUtc { get; init; }
        public long Priority { get; init; }
        public long Status { get; init; }
        public long CreatedUtc { get; init; }
        public long? DoneUtc { get; init; }
    }
}
