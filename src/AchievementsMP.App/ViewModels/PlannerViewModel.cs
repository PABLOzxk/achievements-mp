using System.ComponentModel;
using System.Diagnostics;
using AchievementsMP.App.Services;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Planning;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

/// <summary>
/// Planejador de 100%: o plano de um jogo (o que falta, na ordem escolhida, com classificação, anotações, guias e
/// checklist) e a aba de metas. A classificação só vai para o banco ao salvar, ao trocar de conquista ou ao sair da tela.
/// </summary>
public sealed partial class PlannerViewModel : PageViewModel
{
    private readonly SteamCatalogRepository _catalog;
    private readonly PlannerRepository _planner;
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _time;

    public PlannerViewModel(SteamCatalogRepository catalog, PlannerRepository planner, ISettingsStore settings, TimeProvider time, GoalsViewModel goals)
    {
        _catalog = catalog;
        _planner = planner;
        _settings = settings;
        _time = time;
        Goals = goals;
        goals.OpenGame = appId => _ = OpenGameFromGoalsAsync(appId);
    }

    public override string Title => "Planejador de 100%";
    public override string Glyph => "\uE9D5";

    public GoalsViewModel Goals { get; }

    internal Action<long>? NavigateToGame { get; set; }

    public IReadOnlyList<Option<PlanStrategy>> StrategyOptions { get; } =
    [
        new(PlanStrategy.Recommended, "Perdíveis → rápidas → demais → demoradas"),
        new(PlanStrategy.QuickFirst, "Rápidas → perdíveis → demais → demoradas"),
        new(PlanStrategy.RarityOnly, "Só pela raridade (mais comuns primeiro)"),
    ];

    private long? _pendingAppId;
    private string? _pendingApiName;
    private bool _loading;
    private IReadOnlyList<LibraryGame> _library = [];
    private IReadOnlyList<AchievementDetail> _achievements = [];
    private Dictionary<string, AchievementPlan> _plans = new(StringComparer.Ordinal);
    private IReadOnlyList<GuideRow> _guides = [];

    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsGameTab))] private bool _isGoalsTab;

    public bool IsGameTab
    {
        get => !IsGoalsTab;
        set => IsGoalsTab = !value;
    }

    [ObservableProperty] private IReadOnlyList<GameChoice> _gameOptions = [];
    [ObservableProperty] private GameChoice? _selectedGame;
    [ObservableProperty] private Option<PlanStrategy>? _strategy;

    [ObservableProperty] private bool _hasGame;
    [ObservableProperty] private LibraryItem? _game;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _emptyText = "Escolha um jogo acima, ou use “Planejar 100%” nos detalhes do jogo ou no menu da biblioteca (botão direito).";
    [ObservableProperty] private bool _hasRows;
    [ObservableProperty] private IReadOnlyList<PlanRow> _rows = [];
    [ObservableProperty] private object? _selectedRowItem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyCanExecuteChangedFor(nameof(SaveEditorCommand))]
    private AchievementEditor? _editor;
    [ObservableProperty] private string _saveStatus = "";

    [ObservableProperty] private IReadOnlyList<TaskRow> _tasks = [];
    [ObservableProperty] private string _taskSummary = "";
    [ObservableProperty] private string _newTaskTitle = "";

    [ObservableProperty] private IReadOnlyList<GuideRow> _gameGuides = [];
    [ObservableProperty] private string _newGameGuideUrl = "";
    [ObservableProperty] private string? _gameGuideError;

    public bool HasEditor => Editor is not null;

    /// <summary>Abre o plano deste jogo (e, opcionalmente, já seleciona a conquista) na próxima carga da página.</summary>
    public void ShowGame(long appId, string? apiName = null)
    {
        _pendingAppId = appId;
        _pendingApiName = apiName;
        IsGoalsTab = false;
    }

    public void ShowGoals() => IsGoalsTab = true;

    public override async Task OnNavigatedToAsync()
    {
        await SaveIfDirtyAsync(Editor);
        _loading = true;
        try
        {
            var settings = await _settings.LoadAsync();
            Strategy ??= StrategyOptions.FirstOrDefault(o => o.Value == settings.PlanStrategy) ?? StrategyOptions[0];

            _library = await _catalog.GetLibraryAsync();
            var planned = await _planner.GetPlannedGameIdsAsync();
            var targetId = _pendingAppId ?? SelectedGame?.AppId;
            _pendingAppId = null;

            GameOptions = _library
                .Where(g => g.Progress.Total > 0 || g.AppId == targetId)
                .OrderByDescending(g => planned.Contains(g.AppId))
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => GameChoice.From(g, planned.Contains(g.AppId)))
                .ToList();
            // Sem jogo pedido: volta ao que estava aberto ou, na primeira vez, ao primeiro jogo já planejado.
            SelectedGame = targetId is not null
                ? GameOptions.FirstOrDefault(o => o.AppId == targetId)
                : GameOptions.FirstOrDefault(o => o.IsPlanned);
        }
        finally
        {
            _loading = false;
        }

        await LoadGameAsync(SelectedGame?.AppId);
        await Goals.LoadAsync();
    }

    partial void OnSelectedGameChanged(GameChoice? value)
    {
        if (!_loading) _ = LoadGameAsync(value?.AppId);
    }

    partial void OnStrategyChanged(Option<PlanStrategy>? value)
    {
        if (_loading || value is null) return;
        RebuildRows();
        _ = SaveStrategyAsync(value.Value);
    }

    private async Task SaveStrategyAsync(PlanStrategy strategy)
    {
        try
        {
            var s = await _settings.LoadAsync();
            await _settings.SaveAsync(s with { PlanStrategy = strategy });
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao salvar a ordem do planejador", ex);
        }
    }

    private async Task LoadGameAsync(long? appId)
    {
        await SaveIfDirtyAsync(Editor);
        Editor = null;
        SaveStatus = "";
        var game = appId is { } id ? _library.FirstOrDefault(g => g.AppId == id) ?? await _catalog.GetGameAsync(id) : null;
        if (game is null)
        {
            Game = null;
            HasGame = false;
            Rows = [];
            HasRows = false;
            Tasks = [];
            GameGuides = [];
            return;
        }

        Game = LibraryItem.From(game);
        _achievements = await _catalog.GetAchievementsAsync(game.AppId);
        _plans = new Dictionary<string, AchievementPlan>(await _planner.GetPlansAsync(game.AppId), StringComparer.Ordinal);
        await ReloadGuidesAsync(game.AppId);
        await ReloadTasksAsync(game.AppId);
        HasGame = true;

        var select = _pendingApiName;
        _pendingApiName = null;
        RebuildRows(select);
    }

    /// <summary>Recalcula grupos e ordem. Mantém selecionada a conquista em edição (ou <paramref name="select"/>).</summary>
    private void RebuildRows(string? select = null)
    {
        if (Game is null) return;
        var plan = PlanBuilder.Build(_achievements, _plans, Strategy?.Value ?? PlanStrategy.Recommended);
        var guideCounts = _guides.Where(g => g.ApiName is not null).GroupBy(g => g.ApiName!).ToDictionary(g => g.Key, g => g.Count());

        var rows = new List<PlanRow>();
        foreach (var group in PlanBuilder.GroupOrder(Strategy?.Value ?? PlanStrategy.Recommended))
        {
            var entries = plan.Entries.Where(e => e.Group == group).ToList();
            if (entries.Count == 0) continue;
            rows.Add(PlanHeaderRow.For(group, entries.Count));
            rows.AddRange(entries.Select(e => PlanAchievementRow.From(e, guideCounts.GetValueOrDefault(e.Achievement.ApiName))));
        }
        Rows = rows;
        HasRows = rows.Count > 0;

        var total = _achievements.Count;
        Summary = total switch
        {
            0 => "Este jogo não tem conquistas na Steam.",
            _ when plan.Pending == 0 && plan.Unknown == 0 => $"Completo: {plan.Unlocked} de {total}. Nada a planejar.",
            _ when plan.Pending == 0 => $"{plan.Unknown} conquistas sem dados da Steam. Sincronize para planejar.",
            _ => $"Faltam {plan.Pending} de {total}"
                 + (plan.MissablePending > 0 ? $" · {plan.MissablePending} perdíveis" : "")
                 + (plan.Unclassified > 0 ? $" · {plan.Unclassified} sem classificação" : " · todas classificadas")
                 + (plan.Unknown > 0 ? $" · {plan.Unknown} sem dados" : ""),
        };

        var keep = select ?? Editor?.ApiName;
        var match = rows.OfType<PlanAchievementRow>().FirstOrDefault(r => r.ApiName == keep);
        if (match is null && Editor is { IsDirty: false })
            Editor = null;
        SelectedRowItem = match;
    }

    partial void OnSelectedRowItemChanged(object? oldValue, object? newValue)
    {
        if (newValue is PlanHeaderRow)
        {
            SelectedRowItem = oldValue as PlanAchievementRow;
            return;
        }
        var row = newValue as PlanAchievementRow;
        if (row is null || row.ApiName == Editor?.ApiName) return;
        _ = SelectAsync(row);
    }

    private async Task SelectAsync(PlanAchievementRow row)
    {
        var previous = Editor;
        var plan = _plans.GetValueOrDefault(row.ApiName) ?? AchievementPlan.Empty(Game!.AppId, row.ApiName);
        Editor = new AchievementEditor(row, plan, _guides.Where(g => g.ApiName == row.ApiName).ToList());
        SaveStatus = "";
        if (await SaveIfDirtyAsync(previous))
            RebuildRows();
    }

    partial void OnEditorChanged(AchievementEditor? oldValue, AchievementEditor? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnEditorPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AchievementEditor.IsDirty)) return;
        SaveEditorCommand.NotifyCanExecuteChanged();
        if (Editor?.IsDirty == true) SaveStatus = "Alterações não salvas";
    }

    private bool CanSaveEditor() => Editor?.IsDirty == true;

    [RelayCommand(CanExecute = nameof(CanSaveEditor))]
    private async Task SaveEditorAsync()
    {
        if (await SaveIfDirtyAsync(Editor))
            RebuildRows();
    }

    /// <summary>Grava a classificação se houver mudanças. Devolve true se gravou.</summary>
    private async Task<bool> SaveIfDirtyAsync(AchievementEditor? editor)
    {
        if (editor is not { IsDirty: true }) return false;
        var plan = editor.ToPlan();
        try
        {
            await _planner.SavePlanAsync(plan, _time.GetUtcNow());
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao salvar a classificação da conquista", ex);
            SaveStatus = "Não foi possível salvar. Detalhes no log.";
            return false;
        }

        // Só atualiza o cache se ainda for o mesmo jogo (a gravação pode terminar depois de trocar de jogo).
        if (Game?.AppId == plan.AppId)
        {
            if (plan.IsEmpty) _plans.Remove(plan.ApiName);
            else _plans[plan.ApiName] = plan;
        }
        editor.MarkSaved(plan);
        if (editor == Editor) SaveStatus = "Salvo";
        return true;
    }

    /// <summary>Chamado ao fechar a janela: nada digitado se perde.</summary>
    public Task FlushAsync() => SaveIfDirtyAsync(Editor);

    public bool HasPendingEdits => Editor?.IsDirty == true;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Saiu da tela: grava o que estiver pendente.
        if (e.PropertyName == nameof(IsActive) && !IsActive)
            _ = SaveIfDirtyAsync(Editor);
    }

    // ── Guias ────────────────────────────────────────────────────────────────

    private async Task ReloadGuidesAsync(long appId)
    {
        _guides = (await _planner.GetGuidesAsync(appId)).Select(GuideRow.From).ToList();
        GameGuides = _guides.Where(g => g.ApiName is null).ToList();
        if (Editor is { } editor)
            editor.Guides = _guides.Where(g => g.ApiName == editor.ApiName).ToList();
    }

    [RelayCommand]
    private async Task AddGameGuideAsync()
    {
        if (Game is null) return;
        if (!GuideLink.TryParse(NewGameGuideUrl, out var url))
        {
            GameGuideError = "Cole um link de página da web (http ou https).";
            return;
        }
        await _planner.AddGuideAsync(Game.AppId, null, url, null, _time.GetUtcNow());
        NewGameGuideUrl = "";
        GameGuideError = null;
        await ReloadGuidesAsync(Game.AppId);
    }

    [RelayCommand]
    private async Task AddAchievementGuideAsync()
    {
        if (Game is null || Editor is not { } editor) return;
        if (!GuideLink.TryParse(editor.NewGuideUrl, out var url))
        {
            editor.GuideError = "Cole um link de página da web (http ou https).";
            return;
        }
        await _planner.AddGuideAsync(Game.AppId, editor.ApiName, url, null, _time.GetUtcNow());
        editor.NewGuideUrl = "";
        editor.GuideError = null;
        await ReloadGuidesAsync(Game.AppId);
        RebuildRows();
    }

    [RelayCommand]
    private async Task RemoveGuideAsync(GuideRow? guide)
    {
        if (guide is null || Game is null) return;
        await _planner.DeleteGuideAsync(guide.Id);
        await ReloadGuidesAsync(Game.AppId);
        if (guide.ApiName is not null) RebuildRows();
    }

    [RelayCommand]
    private static void OpenGuide(GuideRow? guide)
    {
        // Conferido de novo aqui: só páginas web são abertas pelo sistema.
        if (guide is null || !GuideLink.IsSafe(guide.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(guide.Url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao abrir o link do guia", ex);
        }
    }

    // ── Checklist ────────────────────────────────────────────────────────────

    private async Task ReloadTasksAsync(long appId)
    {
        var tasks = await _planner.GetTasksAsync(appId);
        Tasks = tasks.Select(t => new TaskRow(t, ToggleTaskAsync)).ToList();
        TaskSummary = tasks.Count == 0 ? "" : $"{tasks.Count(t => t.IsDone)} de {tasks.Count} feitas";
    }

    private async Task ToggleTaskAsync(TaskRow task, bool done)
    {
        if (Game is null) return;
        try
        {
            await _planner.SetTaskDoneAsync(task.Id, done ? _time.GetUtcNow() : null);
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao marcar tarefa", ex);
        }
        await ReloadTasksAsync(Game.AppId);
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        var title = NewTaskTitle.Trim();
        if (Game is null || title.Length == 0) return;
        await _planner.AddTaskAsync(Game.AppId, null, title, _time.GetUtcNow());
        NewTaskTitle = "";
        await ReloadTasksAsync(Game.AppId);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(TaskRow? task)
    {
        if (task is null || Game is null) return;
        await _planner.DeleteTaskAsync(task.Id);
        await ReloadTasksAsync(Game.AppId);
    }

    [RelayCommand]
    private async Task MoveTaskUpAsync(TaskRow? task)
    {
        if (task is null || Game is null) return;
        await _planner.MoveTaskAsync(task.Id, -1);
        await ReloadTasksAsync(Game.AppId);
    }

    [RelayCommand]
    private async Task MoveTaskDownAsync(TaskRow? task)
    {
        if (task is null || Game is null) return;
        await _planner.MoveTaskAsync(task.Id, +1);
        await ReloadTasksAsync(Game.AppId);
    }

    // ── Navegação ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenDetails()
    {
        if (Game is not null) NavigateToGame?.Invoke(Game.AppId);
    }

    [RelayCommand]
    private async Task CreateGameGoalAsync()
    {
        if (Game is null) return;
        await Goals.PrepareCompleteGameAsync(Game.AppId);
        IsGoalsTab = true;
    }

    private async Task OpenGameFromGoalsAsync(long appId)
    {
        _pendingAppId = appId;
        IsGoalsTab = false;
        await OnNavigatedToAsync();
    }
}
