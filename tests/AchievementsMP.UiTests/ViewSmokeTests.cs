using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AchievementsMP.App;
using AchievementsMP.App.ViewModels;
using AchievementsMP.App.Views;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementsMP.UiTests;

/// <summary>
/// Abre cada tela com dados reais num banco temporário e força o layout. Pega o que o compilador não pega:
/// XAML que quebra ao carregar, recurso inexistente, template inválido e binding com nome de propriedade errado.
/// </summary>
public class ViewSmokeTests
{
    private const long Hades = 1145360, Portal = 620;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddDays(-3);

    [Fact]
    public void Every_page_renders_with_data_without_xaml_or_binding_errors() => RunOnUiThread(async () =>
    {
        var bindingErrors = BindingErrorListener.Attach();

        // Prova de que o detector funciona: um binding com nome errado precisa ser pego.
        var probe = new TextBlock();
        probe.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("PropriedadeQueNaoExiste"));
        Render(probe, new object());
        Assert.Single(bindingErrors.Errors);
        bindingErrors.Errors.Clear();

        var app = new AchievementsMP.App.App();
        app.InitializeComponent();

        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "amp-ui-tests", Guid.NewGuid().ToString("N")));
        var (factory, migration, _) = await AppDatabase.InitializeAsync(paths);
        await SeedAsync(factory);
        using var services = AchievementsMP.App.App.ConfigureServices(paths, factory, migration.ToVersion);

        var main = services.GetRequiredService<MainViewModel>();
        var window = new MainWindow { DataContext = main };

        var overview = services.GetRequiredService<OverviewViewModel>();
        await overview.OnNavigatedToAsync();
        Render(new OverviewView(), overview);
        Assert.NotEmpty(overview.ActiveGoals);
        Assert.NotEmpty(overview.Flagged);

        var library = services.GetRequiredService<LibraryViewModel>();
        await library.OnNavigatedToAsync();
        Render(new LibraryView(), library);
        Assert.Equal(2, library.Items.Count);

        var details = services.GetRequiredService<GameDetailsViewModel>();
        details.Show(Hades);
        await details.OnNavigatedToAsync();
        Render(new GameDetailsView(), details);
        Assert.True(details.HasGame);
        // O filtro padrão mostra só as pendentes; com "Todas" as desbloqueadas aparecem com o fundo verde ou roxo.
        details.Filter = details.FilterOptions.First(o => o.Value == AchievementFilter.All);
        Render(new GameDetailsView(), details);
        Assert.Equal(["Conquista rara"], details.Achievements.Where(a => a.IsRareUnlock).Select(a => a.Name));
        Assert.Contains(details.Achievements, a => a.IsUnlocked && !a.IsRareUnlock);

        // Planejador: abre já com uma conquista selecionada, edita, salva e confere que mudou de grupo.
        var planner = services.GetRequiredService<PlannerViewModel>();
        planner.ShowGame(Hades, "rapida");
        await planner.OnNavigatedToAsync();
        var plannerView = new PlannerView();
        Render(plannerView, planner);
        Assert.True(planner.HasGame);
        Assert.NotNull(planner.Editor);
        Assert.Equal("rapida", planner.Editor!.ApiName);
        Assert.Contains(planner.Rows, r => r is PlanHeaderRow { Group: PlanGroup.Missable });
        Assert.NotEmpty(planner.Tasks);

        planner.Editor.Missable = AchievementEditor.MissableOptions.First(o => o.Value == Missable.Yes);
        Assert.True(planner.Editor.IsDirty);
        await planner.SaveEditorCommand.ExecuteAsync(null);
        Assert.False(planner.Editor.IsDirty);
        Render(plannerView, planner);
        var missable = planner.Rows.SkipWhile(r => r is not PlanHeaderRow { Group: PlanGroup.Missable }).Skip(1)
            .TakeWhile(r => r is PlanAchievementRow).Cast<PlanAchievementRow>().Select(r => r.ApiName).ToList();
        Assert.Contains("rapida", missable);

        planner.NewTaskTitle = "Pegar todos os martelos";
        await planner.AddTaskCommand.ExecuteAsync(null);
        planner.Tasks[0].IsDone = true;
        Render(plannerView, planner);

        // Aba de metas, com o formulário em cada tipo.
        planner.IsGoalsTab = true;
        await planner.Goals.LoadAsync();
        Render(plannerView, planner);
        Assert.NotEmpty(planner.Goals.Active);
        foreach (var kind in planner.Goals.KindOptions)
        {
            planner.Goals.Kind = kind;
            Render(plannerView, planner);
        }
        planner.Goals.Kind = planner.Goals.KindOptions.First(o => o.Value == GoalKind.UnlockCount);
        planner.Goals.TargetText = "2";
        await planner.Goals.CreateCommand.ExecuteAsync(null);
        Assert.Null(planner.Goals.FormError);
        planner.Goals.ShowFinished = true;
        Render(plannerView, planner);

        var backupFolder = Path.Combine(paths.Root, "OneDrive");
        var store = services.GetRequiredService<ISettingsStore>();
        await store.SaveAsync((await store.LoadAsync()) with { BackupFolder = backupFolder });

        var settings = services.GetRequiredService<SettingsViewModel>();
        await settings.OnNavigatedToAsync();
        Render(new SettingsView(), settings);
        Assert.True(settings.IsBackupOn);
        await settings.BackupNowCommand.ExecuteAsync(null);
        Assert.False(settings.IsBackupError, settings.BackupStatus);
        Render(new SettingsView(), settings);
        Assert.Single(BackupService.List(backupFolder));
        Assert.StartsWith("Último backup", settings.LastBackupText);

        // Guia de primeira configuração, com os três estados de passo (sem chamar a Steam de verdade).
        var setup = services.GetRequiredService<SetupViewModel>();
        await setup.OnNavigatedToAsync();
        var setupView = new SetupView();
        Render(setupView, setup);
        setup.ProfileStep.Set(AchievementsMP.Sync.CheckState.Ok, "Encontrei o perfil de Fulano.");
        setup.KeyStep.Set(AchievementsMP.Sync.CheckState.Problem, "A Steam recusou a chave.");
        setup.NewApiKey = "curta-demais";
        Render(setupView, setup);
        Assert.NotEmpty(setup.KeyShapeWarning);
        Assert.False(setup.FinishCommand.CanExecute(null));

        // Fechar o app: com backup do dia já feito, não cria outro.
        await main.BeforeExitAsync();
        Assert.Single(BackupService.List(backupFolder));

        GC.KeepAlive(window); // construída só para validar o XAML; fechar encerraria o app de teste
        Assert.True(bindingErrors.Errors.Count == 0, "Erros de binding:\n" + string.Join("\n", bindingErrors.Errors));
    });

    private static async Task SeedAsync(SqliteConnectionFactory factory)
    {
        var steam = new SteamCatalogRepository(factory);
        var planner = new PlannerRepository(factory);
        var personal = new PersonalDataRepository(factory);

        await steam.UpsertLibraryAsync(
            [new Game(Hades, "Hades", null, 336, T0, true), new Game(Portal, "Portal 2", null, 600, T0.AddDays(-30), true)], T0, isCompleteLibrary: true);

        string[] hades = ["feita", "rara", "perdivel", "rapida", "longa", "semclasse", "alta"];
        string[] unlocked = ["feita", "rara"];
        await steam.UpsertSchemaAsync(Hades, hades.Select(a => new AchievementDefinition(Hades, a, $"Conquista {a}", $"Descrição de {a}", a == "semclasse", null, null)).ToList(), T0);
        await steam.ApplyPlayerStatesAsync(Hades,
            hades.Select(a => unlocked.Contains(a)
                ? new PlayerAchievementState(Hades, a, UnlockState.Unlocked, T0)
                : new PlayerAchievementState(Hades, a, UnlockState.Locked, null)).ToList(), T0);
        await steam.UpsertGlobalPercentagesAsync(Hades,
            new Dictionary<string, double> { ["feita"] = 81.8, ["rara"] = 2.1, ["perdivel"] = 12.5, ["rapida"] = 55.0, ["longa"] = 5.9, ["semclasse"] = 0.4 }, T0);

        await steam.UpsertSchemaAsync(Portal, [new AchievementDefinition(Portal, "bolo", "O bolo", null, false, null, null)], T0);
        await steam.ApplyPlayerStatesAsync(Portal, [new PlayerAchievementState(Portal, "bolo", UnlockState.Unlocked, T0)], T0);

        await planner.SavePlanAsync(new AchievementPlan(Hades, "perdivel", Effort.Long, Missable.Yes, Priority.Normal, "Dificuldade máxima", "guia X", "Antes do chefe final"), T0);
        await planner.SavePlanAsync(new AchievementPlan(Hades, "rapida", Effort.Quick), T0);
        await planner.SavePlanAsync(new AchievementPlan(Hades, "longa", Effort.Long, Priority: Priority.Low), T0);
        await planner.SavePlanAsync(new AchievementPlan(Hades, "alta", Priority: Priority.High), T0);
        await planner.AddGuideAsync(Hades, null, new Uri("https://example.com/hades-100"), "Guia 100%", T0);
        await planner.AddGuideAsync(Hades, "perdivel", new Uri("https://example.com/perdivel"), null, T0);
        await planner.AddTaskAsync(Hades, null, "Comprar todos os itens do contrato", T0);
        var done = await planner.AddTaskAsync(Hades, null, "Zerar uma vez", T0);
        await planner.SetTaskDoneAsync(done, T0.AddDays(1));

        await planner.CreateGoalAsync(new GoalDraft("100% em Hades", GoalKind.CompleteGame, Hades, DeadlineUtc: DateTimeOffset.UtcNow.AddDays(2), Priority: Priority.High), T0);
        await planner.CreateGoalAsync(new GoalDraft("Pegar a perdível", GoalKind.UnlockAchievement, Hades, "perdivel"), T0);
        await planner.CreateGoalAsync(new GoalDraft("3 por semana", GoalKind.UnlockCount, null, null, 3, GoalPeriod.Weekly), T0);
        await planner.CreateGoalAsync(new GoalDraft("Zerar no modo difícil", GoalKind.Custom, DeadlineUtc: DateTimeOffset.UtcNow.AddDays(-1)), T0);
        var finished = await planner.CreateGoalAsync(new GoalDraft("Antiga", GoalKind.Custom), T0);
        await planner.SetGoalStatusAsync(finished, GoalStatus.Completed, T0);

        var fav = await personal.GetOrCreateFavoritesAsync(T0);
        await personal.AddGameToListAsync(fav, Hades, T0);
    }

    private static void Render(FrameworkElement view, object viewModel)
    {
        view.DataContext = viewModel;
        view.Measure(new Size(1400, 900));
        view.Arrange(new Rect(0, 0, 1400, 900));
        view.UpdateLayout();
        // Deixa rodar o que a interface agendou (bindings adiados, carregamentos assíncronos).
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>WPF exige uma thread STA com Dispatcher; os awaits continuam nela.</summary>
    private static void RunOnUiThread(Func<Task> scenario)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try { await scenario(); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new TimeoutException("A interface não terminou em 2 minutos.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Coleta os erros de binding que o WPF normalmente só escreve na janela de saída do depurador.</summary>
    private sealed class BindingErrorListener : TraceListener
    {
        private readonly StringBuilder _pending = new();
        public List<string> Errors { get; } = [];

        public static BindingErrorListener Attach()
        {
            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            return listener;
        }

        public override void Write(string? message) => _pending.Append(message);

        public override void WriteLine(string? message)
        {
            var full = _pending.Append(message).ToString();
            _pending.Clear();
            // 40 = propriedade não encontrada no objeto do binding (nome errado ou tipo errado).
            if (full.Contains("Error: 40", StringComparison.Ordinal) || full.Contains("BindingExpression path error", StringComparison.Ordinal))
                Errors.Add(full);
        }
    }
}
