using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Data.Repositories;
using Dapper;

namespace AchievementsMP.Tests.Data;

public class PlannerRepositoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private const long Hades = 1145360, Portal = 620;

    private static Game G(long id, string name) => new(id, name, null, 0, null, true);
    private static AchievementDefinition A(long app, string name) => new(app, name, name, null, false, null, null);

    [Fact]
    public async Task Plan_round_trips_and_clearing_everything_removes_it()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);

        await repo.SavePlanAsync(new AchievementPlan(Hades, "X", Effort.Quick, Missable.Yes, Priority.High, " Multijogador ", "guia X", "  fazer no 1º chefe "), T0);
        var saved = (await repo.GetPlansAsync(Hades))["X"];

        Assert.Equal(new AchievementPlan(Hades, "X", Effort.Quick, Missable.Yes, Priority.High, "Multijogador", "guia X", "fazer no 1º chefe"), saved);

        await repo.SavePlanAsync(AchievementPlan.Empty(Hades, "X") with { Requirement = "  " }, T0);

        Assert.Empty(await repo.GetPlansAsync(Hades));
        await using var c = await db.Factory.OpenAsync();
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM achievement_plans"));
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM achievement_notes"));
    }

    [Fact]
    public async Task Note_alone_is_kept_without_a_classification_row()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);

        await repo.SavePlanAsync(AchievementPlan.Empty(Hades, "X") with { Note = "lembrar" }, T0);

        var plan = (await repo.GetPlansAsync(Hades))["X"];
        Assert.Equal("lembrar", plan.Note);
        Assert.False(plan.IsClassified);
    }

    [Fact]
    public async Task Unknown_enum_values_in_the_database_fall_back_to_defaults()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO achievement_plans(app_id, api_name, effort, missable, priority, updated_utc) VALUES (1, 'X', 9, 9, 9, 0)");

        var plan = (await new PlannerRepository(db.Factory).GetPlansAsync(1))["X"];

        Assert.Equal((Effort.Unknown, Missable.Unknown, Priority.Normal), (plan.Effort, plan.Missable, plan.Priority));
    }

    [Fact]
    public async Task Guides_accept_only_web_links()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);

        await repo.AddGuideAsync(Hades, null, new Uri("https://example.com/hades"), " Guia geral ", T0);
        var perAchievement = await repo.AddGuideAsync(Hades, "X", new Uri("https://example.com/x"), null, T0);
        await Assert.ThrowsAsync<ArgumentException>(() => repo.AddGuideAsync(Hades, null, new Uri("file:///C:/calc.exe"), null, T0));
        // Linha adulterada no banco não aparece.
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO guides(app_id, url, created_utc) VALUES (@Hades, 'file:///C:/calc.exe', 0)", new { Hades });

        var guides = await repo.GetGuidesAsync(Hades);
        Assert.Equal(["Guia geral", null], guides.Select(g => g.Title));
        Assert.Equal([null, "X"], guides.Select(g => g.ApiName));

        await repo.DeleteGuideAsync(perAchievement);
        Assert.Single(await repo.GetGuidesAsync(Hades));
    }

    [Fact]
    public async Task Tasks_keep_order_can_move_and_record_completion()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);
        var a = await repo.AddTaskAsync(Hades, null, "A", T0);
        var b = await repo.AddTaskAsync(Hades, null, "B", T0);
        var c = await repo.AddTaskAsync(Hades, null, "C", T0);
        await repo.AddTaskAsync(Portal, null, "outro jogo", T0);

        await repo.MoveTaskAsync(c, -1);          // A C B
        await repo.MoveTaskAsync(a, -1);          // já é a primeira: nada muda
        await repo.MoveTaskAsync(a, +1);          // C A B
        await repo.SetTaskDoneAsync(b, T0.AddDays(1));
        await repo.RenameTaskAsync(c, " C2 ");

        var tasks = await repo.GetTasksAsync(Hades);
        Assert.Equal(["C2", "A", "B"], tasks.Select(t => t.Title));
        Assert.Equal(T0.AddDays(1), tasks[2].DoneUtc);
        Assert.True(tasks[2].IsDone);

        await repo.SetTaskDoneAsync(b, null);
        await repo.DeleteTaskAsync(a);
        tasks = await repo.GetTasksAsync(Hades);
        Assert.Equal(["C2", "B"], tasks.Select(t => t.Title));
        Assert.False(tasks[1].IsDone);
        Assert.Equal(1, await repo.CountTasksAsync(Portal));
    }

    [Fact]
    public async Task Achievement_tasks_have_their_own_order_and_can_change_owner()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);
        var game1 = await repo.AddTaskAsync(Hades, null, "jogo 1", T0);
        var x1 = await repo.AddTaskAsync(Hades, "x", "x 1", T0);
        var game2 = await repo.AddTaskAsync(Hades, null, "jogo 2", T0);
        var x2 = await repo.AddTaskAsync(Hades, "x", "x 2", T0);
        await repo.AddTaskAsync(Hades, "y", "y 1", T0);

        async Task<string[]> Titles(string? apiName) =>
            (await repo.GetTasksAsync(Hades)).Where(t => t.ApiName == apiName).Select(t => t.Title).ToArray();

        // Subir "jogo 2" troca com "jogo 1", não com "x 1", que está entre os dois no banco.
        await repo.MoveTaskAsync(game2, -1);
        Assert.Equal(["jogo 2", "jogo 1"], await Titles(null));
        await repo.MoveTaskAsync(x1, -1);         // já é a primeira da conquista: nada muda
        await repo.MoveTaskAsync(x1, +1);
        Assert.Equal(["x 2", "x 1"], await Titles("x"));
        Assert.Equal(["y 1"], await Titles("y"));

        // Uma tarefa antiga do jogo passa para a conquista e vai para o fim da lista dela; e volta.
        await repo.MoveTaskToAsync(game1, "x");
        Assert.Equal(["jogo 2"], await Titles(null));
        Assert.Equal(["x 2", "x 1", "jogo 1"], await Titles("x"));
        await repo.MoveTaskToAsync(x2, null);
        Assert.Equal(["jogo 2", "x 2"], await Titles(null));
        Assert.Equal(["x 1", "jogo 1"], await Titles("x"));
        Assert.Equal(5, await repo.CountTasksAsync(Hades));
    }

    [Fact]
    public async Task Goals_round_trip_and_change_status()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);
        var deadline = new DateTimeOffset(2026, 12, 31, 3, 0, 0, TimeSpan.Zero);

        var id = await repo.CreateGoalAsync(new GoalDraft(" 5 por semana ", GoalKind.UnlockCount, Hades, null, 5, GoalPeriod.Weekly, deadline, Priority.High), T0);
        await repo.CreateGoalAsync(new GoalDraft("Livre", GoalKind.Custom), T0);

        var goal = (await repo.GetGoalsAsync())[0];
        Assert.Equal(new Goal(id, "5 por semana", GoalKind.UnlockCount, Hades, null, 5, GoalPeriod.Weekly, deadline, Priority.High, GoalStatus.Active, T0, null), goal);

        await repo.SetGoalStatusAsync(id, GoalStatus.Completed, T0.AddDays(2));
        Assert.Equal((GoalStatus.Completed, T0.AddDays(2)), ((await repo.GetGoalsAsync())[0].Status, (await repo.GetGoalsAsync())[0].DoneUtc));

        await repo.DeleteGoalAsync(id);
        Assert.Equal(["Livre"], (await repo.GetGoalsAsync()).Select(g => g.Title));
    }

    [Fact]
    public async Task Counts_unlocks_inside_the_window_only()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var steam = new SteamCatalogRepository(db.Factory);
        await steam.UpsertLibraryAsync([G(Hades, "Hades"), G(Portal, "Portal 2")], T0, isCompleteLibrary: true);
        await steam.UpsertSchemaAsync(Hades, [A(Hades, "a"), A(Hades, "b"), A(Hades, "c"), A(Hades, "d")], T0);
        await steam.UpsertSchemaAsync(Portal, [A(Portal, "p")], T0);
        await steam.ApplyPlayerStatesAsync(Hades,
        [
            new(Hades, "a", UnlockState.Unlocked, T0.AddDays(-1)), // antes da janela
            new(Hades, "b", UnlockState.Unlocked, T0),             // início conta
            new(Hades, "c", UnlockState.Unlocked, T0.AddDays(7)),  // fim não conta
            new(Hades, "d", UnlockState.Locked, null),
        ], T0);
        await steam.ApplyPlayerStatesAsync(Portal, [new(Portal, "p", UnlockState.Unlocked, T0.AddDays(1))], T0);
        var repo = new PlannerRepository(db.Factory);

        Assert.Equal(1, await repo.CountUnlocksAsync(Hades, T0, T0.AddDays(7)));
        Assert.Equal(2, await repo.CountUnlocksAsync(null, T0, T0.AddDays(7)));
        Assert.Equal(3, await repo.CountUnlocksAsync(null, T0, null));
    }

    [Fact]
    public async Task Flagged_pending_lists_only_locked_missable_or_high_priority_missable_first()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var steam = new SteamCatalogRepository(db.Factory);
        await steam.UpsertLibraryAsync([G(Hades, "Hades")], T0, isCompleteLibrary: true);
        await steam.UpsertSchemaAsync(Hades, [A(Hades, "perdivel"), A(Hades, "alta"), A(Hades, "feita"), A(Hades, "normal"), A(Hades, "semdados")], T0);
        await steam.ApplyPlayerStatesAsync(Hades,
        [
            new(Hades, "perdivel", UnlockState.Locked, null), new(Hades, "alta", UnlockState.Locked, null),
            new(Hades, "feita", UnlockState.Unlocked, T0), new(Hades, "normal", UnlockState.Locked, null),
        ], T0);
        var repo = new PlannerRepository(db.Factory);
        await repo.SavePlanAsync(new AchievementPlan(Hades, "alta", Priority: Priority.High), T0);
        await repo.SavePlanAsync(new AchievementPlan(Hades, "perdivel", Missable: Missable.Yes, Note: "antes do fim"), T0);
        await repo.SavePlanAsync(new AchievementPlan(Hades, "feita", Missable: Missable.Yes), T0);
        await repo.SavePlanAsync(new AchievementPlan(Hades, "normal", Effort.Quick), T0);
        await repo.SavePlanAsync(new AchievementPlan(Hades, "semdados", Missable: Missable.Yes), T0);

        var flagged = await repo.GetFlaggedPendingAsync(10);

        Assert.Equal(["perdivel", "alta"], flagged.Select(f => f.Achievement.ApiName));
        Assert.Equal("Hades", flagged[0].GameName);
        Assert.Equal("antes do fim", flagged[0].Plan.Note);
    }

    [Fact]
    public async Task Planned_games_include_any_personal_planning()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PlannerRepository(db.Factory);
        await repo.SavePlanAsync(new AchievementPlan(1, "x", Effort.Quick), T0);
        await repo.AddTaskAsync(2, null, "t", T0);
        await repo.AddGuideAsync(3, null, new Uri("https://example.com"), null, T0);
        await repo.CreateGoalAsync(new GoalDraft("100%", GoalKind.CompleteGame, 4), T0);
        var done = await repo.CreateGoalAsync(new GoalDraft("100%", GoalKind.CompleteGame, 5), T0);
        await repo.SetGoalStatusAsync(done, GoalStatus.Completed, T0);

        Assert.Equal([1L, 2, 3, 4], (await repo.GetPlannedGameIdsAsync()).Order());
    }
}
