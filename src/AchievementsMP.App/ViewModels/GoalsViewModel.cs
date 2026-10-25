using System.Globalization;
using AchievementsMP.App.Services;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Data.Planning;
using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

/// <summary>Aba "Metas" do planejador: lista das metas e formulário de meta nova.</summary>
public sealed partial class GoalsViewModel(PlannerRepository planner, GoalTracker tracker, SteamCatalogRepository catalog, TimeProvider time)
    : ObservableObject
{
    public IReadOnlyList<Option<GoalKind>> KindOptions { get; } =
    [
        new(GoalKind.CompleteGame, "100% em um jogo"),
        new(GoalKind.UnlockAchievement, "Desbloquear uma conquista"),
        new(GoalKind.UnlockCount, "Desbloquear N conquistas"),
        new(GoalKind.Custom, "Meta livre"),
    ];

    public IReadOnlyList<Option<GoalPeriod>> PeriodOptions { get; } =
    [
        new(GoalPeriod.Weekly, "Por semana (segunda a domingo)"),
        new(GoalPeriod.Daily, "Por dia"),
        new(GoalPeriod.None, "No total, até o prazo"),
    ];

    public IReadOnlyList<Option<Priority>> PriorityOptions => AchievementEditor.PriorityOptions;

    /// <summary>Abre o plano do jogo da meta (definido pelo planejador).</summary>
    internal Action<long>? OpenGame { get; set; }

    private static TimeZoneInfo Zone => TimeZoneInfo.Local;

    [ObservableProperty] private IReadOnlyList<GoalItem> _active = [];
    [ObservableProperty] private IReadOnlyList<GoalItem> _finished = [];
    [ObservableProperty] private bool _hasActive;
    [ObservableProperty] private bool _hasFinished;
    [ObservableProperty] private bool _showFinished;
    [ObservableProperty] private string _finishedHeader = "";

    // Formulário
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowGame), nameof(ShowAchievement), nameof(ShowCount), nameof(ShowDeadline), nameof(SuggestedTitle))]
    private Option<GoalKind>? _kind;
    [ObservableProperty] private IReadOnlyList<GameChoice> _gameOptions = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SuggestedTitle))] private GameChoice? _game;
    [ObservableProperty] private IReadOnlyList<Option<string>> _achievementOptions = [];
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SuggestedTitle))] private Option<string>? _achievement;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SuggestedTitle))] private string _targetText = "3";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowDeadline), nameof(SuggestedTitle))] private Option<GoalPeriod>? _period;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(SuggestedTitle))] private string _deadlineText = "";
    [ObservableProperty] private Option<Priority>? _priority;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _formError;

    public bool ShowGame => Kind?.Value is not GoalKind.Custom;
    public bool ShowAchievement => Kind?.Value == GoalKind.UnlockAchievement;
    public bool ShowCount => Kind?.Value == GoalKind.UnlockCount;
    public bool ShowDeadline => !(ShowCount && Period?.Value is GoalPeriod.Daily or GoalPeriod.Weekly);

    /// <summary>Título usado se o campo ficar vazio (aparece como dica no campo).</summary>
    public string SuggestedTitle
    {
        get
        {
            if (Kind?.Value is not { } kind || kind == GoalKind.Custom) return "Ex.: zerar no modo difícil";
            var game = Game?.AppId is not null ? _gameNames.GetValueOrDefault(Game.AppId.Value) : null;
            var deadline = ShowDeadline && TryParseDeadline(DeadlineText, out var d) ? d : (DateTime?)null;
            return GoalRules.DefaultTitle(kind, game, Achievement?.Label, int.TryParse(TargetText, out var n) ? n : null,
                ShowCount ? Period?.Value ?? GoalPeriod.None : GoalPeriod.None, deadline is { } day ? new DateTimeOffset(day) : null);
        }
    }

    private Dictionary<long, string> _gameNames = [];
    private Dictionary<long, bool> _gameComplete = [];

    public async Task LoadAsync()
    {
        var views = await tracker.GetGoalsAsync(time.GetUtcNow(), Zone);
        var now = time.GetUtcNow();
        Active = GoalItem.OrderActive(views).Select(v => GoalItem.From(v, now, Zone)).ToList();
        Finished = views.Where(v => v.Goal.Status != GoalStatus.Active)
            .OrderByDescending(v => v.Goal.DoneUtc ?? v.Goal.CreatedUtc)
            .Select(v => GoalItem.From(v, now, Zone))
            .ToList();
        HasActive = Active.Count > 0;
        HasFinished = Finished.Count > 0;
        FinishedHeader = $"Concluídas e abandonadas ({Finished.Count})";

        var library = await catalog.GetLibraryAsync();
        var planned = await planner.GetPlannedGameIdsAsync();
        _gameNames = library.ToDictionary(g => g.AppId, g => g.Name);
        _gameComplete = library.ToDictionary(g => g.AppId, g => g.Progress.IsComplete);
        var previous = Game;
        GameOptions =
        [
            new GameChoice(null, "Qualquer jogo", false),
            .. library.Where(g => g.Progress.Total > 0)
                .OrderByDescending(g => planned.Contains(g.AppId))
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => GameChoice.From(g, planned.Contains(g.AppId))),
        ];
        // Primeira vez: o primeiro jogo da lista (planejados vêm antes). Depois: mantém a escolha.
        Game = previous is null
            ? GameOptions.Skip(1).FirstOrDefault() ?? GameOptions[0]
            : GameOptions.FirstOrDefault(o => o.AppId == previous.AppId) ?? GameOptions[0];
        Kind ??= KindOptions[0];
        Period ??= PeriodOptions[0];
        Priority ??= PriorityOptions[1];
    }

    /// <summary>Preenche o formulário com "100% neste jogo" (botão do plano do jogo).</summary>
    public async Task PrepareCompleteGameAsync(long appId)
    {
        await LoadAsync();
        Kind = KindOptions.First(o => o.Value == GoalKind.CompleteGame);
        Game = GameOptions.FirstOrDefault(o => o.AppId == appId);
        FormError = null;
    }

    partial void OnKindChanged(Option<GoalKind>? value)
    {
        FormError = null;
        _ = LoadAchievementOptionsAsync();
    }

    partial void OnGameChanged(GameChoice? value) => _ = LoadAchievementOptionsAsync();

    private async Task LoadAchievementOptionsAsync()
    {
        if (!ShowAchievement || Game?.AppId is not { } appId)
        {
            AchievementOptions = [];
            Achievement = null;
            return;
        }
        try
        {
            var achievements = await catalog.GetAchievementsAsync(appId);
            AchievementOptions = achievements
                .Where(a => a.State == UnlockState.Locked)
                .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(a => new Option<string>(a.ApiName, a.DisplayName))
                .ToList();
            Achievement = AchievementOptions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao carregar conquistas para a meta", ex);
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        FormError = Validate(out var draft);
        if (FormError is not null || draft is null) return;

        await planner.CreateGoalAsync(draft, time.GetUtcNow());
        Title = "";
        DeadlineText = "";
        await LoadAsync();
    }

    private string? Validate(out GoalDraft? draft)
    {
        draft = null;
        var kind = Kind?.Value ?? GoalKind.Custom;
        var appId = kind == GoalKind.Custom ? null : Game?.AppId;

        if (kind is GoalKind.CompleteGame or GoalKind.UnlockAchievement && appId is null)
            return "Escolha o jogo.";
        if (kind == GoalKind.CompleteGame && appId is { } id && _gameComplete.GetValueOrDefault(id))
            return "Esse jogo já está em 100%.";
        if (kind == GoalKind.UnlockAchievement && Achievement is null)
            return "Escolha a conquista. Só aparecem as que ainda faltam.";

        int? target = null;
        if (kind == GoalKind.UnlockCount)
        {
            if (!int.TryParse(TargetText.Trim(), out var n) || n is < 1 or > 1000)
                return "Quantidade: um número de 1 a 1000.";
            target = n;
        }

        var period = kind == GoalKind.UnlockCount ? Period?.Value ?? GoalPeriod.None : GoalPeriod.None;
        DateTimeOffset? deadline = null;
        if (ShowDeadline && !string.IsNullOrWhiteSpace(DeadlineText))
        {
            if (!TryParseDeadline(DeadlineText, out var day))
                return "Prazo: use o formato dd/mm/aaaa.";
            if (day < TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).Date)
                return "O prazo já passou.";
            deadline = EndOfDay(day);
        }

        var title = string.IsNullOrWhiteSpace(Title) ? (kind == GoalKind.Custom ? null : SuggestedTitle) : Title.Trim();
        if (title is null)
            return "Dê um nome para a meta livre.";

        draft = new GoalDraft(title, kind, appId, kind == GoalKind.UnlockAchievement ? Achievement?.Value : null, target, period, deadline,
            Priority?.Value ?? Core.Domain.Priority.Normal);
        return null;
    }

    private bool TryParseDeadline(string text, out DateTime day)
    {
        string[] withYear = ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy"];
        var t = text.Trim();
        if (DateTime.TryParseExact(t, withYear, CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            return true;
        if (DateTime.TryParseExact(t, ["d/M", "dd/MM"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var noYear))
        {
            // "31/12" = próxima ocorrência dessa data.
            var today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).Date;
            day = new DateTime(today.Year, noYear.Month, noYear.Day);
            if (day < today) day = day.AddYears(1);
            return true;
        }
        return false;
    }

    /// <summary>Meia-noite do dia seguinte, no fuso local: o prazo vale o dia inteiro.</summary>
    private static DateTimeOffset EndOfDay(DateTime day)
    {
        var next = DateTime.SpecifyKind(day.Date.AddDays(1), DateTimeKind.Unspecified);
        while (Zone.IsInvalidTime(next)) next = next.AddMinutes(30);
        return new DateTimeOffset(next, Zone.GetUtcOffset(next));
    }

    [RelayCommand]
    private async Task CompleteAsync(GoalItem? item)
    {
        if (item is null) return;
        await planner.SetGoalStatusAsync(item.Id, GoalStatus.Completed, time.GetUtcNow());
        await LoadAsync();
    }

    [RelayCommand]
    private async Task AbandonAsync(GoalItem? item)
    {
        if (item is null) return;
        await planner.SetGoalStatusAsync(item.Id, GoalStatus.Abandoned, null);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ReopenAsync(GoalItem? item)
    {
        if (item is null) return;
        await planner.SetGoalStatusAsync(item.Id, GoalStatus.Active, null);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(GoalItem? item)
    {
        if (item is null) return;
        var answer = System.Windows.MessageBox.Show($"Excluir a meta “{item.Title}”?", "Excluir meta",
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        await planner.DeleteGoalAsync(item.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenGoalGame(GoalItem? item)
    {
        if (item?.AppId is { } appId) OpenGame?.Invoke(appId);
    }

    [RelayCommand]
    private void ToggleFinished() => ShowFinished = !ShowFinished;
}
