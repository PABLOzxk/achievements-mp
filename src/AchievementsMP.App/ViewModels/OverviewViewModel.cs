using AchievementsMP.Core.Library;
using AchievementsMP.Core.Planning;
using AchievementsMP.Core.Progress;
using AchievementsMP.Data.Planning;
using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public sealed partial class OverviewViewModel(
    SteamCatalogRepository catalog, PlannerRepository planner, GoalTracker goals, AchievementsMP.Core.Settings.ISecretStore secrets, TimeProvider time)
    : PageViewModel, IGameOpener
{
    public override string Title => "Visão geral";
    public override string Glyph => "\uE80F";

    internal Action? NavigateToSettings { get; set; }
    internal Action<long>? NavigateToGame { get; set; }
    internal Action<long, string?>? NavigateToPlanner { get; set; }
    internal Action? NavigateToGoals { get; set; }

    public void OpenGame(long appId) => NavigateToGame?.Invoke(appId);

    [ObservableProperty] private int _gameCount;
    [ObservableProperty] private int _unlockedCount;
    [ObservableProperty] private int _completedGames;
    [ObservableProperty] private int _nearlyComplete;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _needsSetup;
    [ObservableProperty] private IReadOnlyList<LibraryItem> _closest = [];
    [ObservableProperty] private IReadOnlyList<LibraryItem> _mostViable = [];
    [ObservableProperty] private bool _hasViable;
    [ObservableProperty] private IReadOnlyList<GoalItem> _activeGoals = [];
    [ObservableProperty] private bool _hasGoals;
    [ObservableProperty] private string _goalsHeader = "METAS EM ANDAMENTO";
    [ObservableProperty] private IReadOnlyList<FlaggedItem> _flagged = [];
    [ObservableProperty] private bool _hasFlagged;

    public override async Task OnNavigatedToAsync()
    {
        var library = await catalog.GetLibraryAsync();
        GameCount = library.Count;
        UnlockedCount = library.Sum(g => g.Progress.Unlocked);
        CompletedGames = library.Count(g => g.Progress.IsComplete);
        NearlyComplete = library.Count(g => g.Progress.Percent is >= 80 && !g.Progress.IsComplete);
        HasData = library.Any(g => g.Progress.Total > 0);
        NeedsSetup = string.IsNullOrEmpty(await secrets.GetSteamApiKeyAsync());

        // Mais perto de 100% = menos conquistas faltando (8 de 21 está mais perto que 31 de 80).
        // Desempate pelo percentual. Só jogos já começados e com dados completos.
        Closest = library
            .Where(g => g.Progress.Total > 0 && !g.Progress.IsComplete && g.Progress.IsExact && g.Progress.Unlocked > 0)
            .OrderBy(g => g.Progress.KnownRemaining)
            .ThenByDescending(g => g.Progress.Percent)
            .Take(6)
            .Select(g => LibraryItem.From(g))
            .ToList();

        // Mais viáveis: começados, e as que faltam são as mais comuns (raridade da mais rara que falta).
        MostViable = LibraryQuery.Apply(library, new LibraryFilter
            {
                Status = LibraryStatus.InProgress,
                MinViability = Viability.Low,
                Sort = LibrarySort.MostViable,
            })
            .Take(6)
            .Select(g => LibraryItem.From(g))
            .ToList();
        HasViable = MostViable.Count > 0;

        var now = time.GetUtcNow();
        var zone = TimeZoneInfo.Local;
        var active = GoalItem.OrderActive(await goals.GetGoalsAsync(now, zone)).ToList();
        ActiveGoals = active.Take(5).Select(v => GoalItem.From(v, now, zone)).ToList();
        HasGoals = ActiveGoals.Count > 0;
        GoalsHeader = active.Count > ActiveGoals.Count ? $"METAS EM ANDAMENTO · {ActiveGoals.Count} DE {active.Count}" : "METAS EM ANDAMENTO";

        Flagged = (await planner.GetFlaggedPendingAsync(8)).Select(FlaggedItem.From).ToList();
        HasFlagged = Flagged.Count > 0;
    }

    [RelayCommand]
    private void OpenGoals() => NavigateToGoals?.Invoke();

    [RelayCommand]
    private void OpenFlagged(FlaggedItem? item)
    {
        if (item is not null) NavigateToPlanner?.Invoke(item.AppId, item.ApiName);
    }

    [RelayCommand]
    private void OpenSettings() => NavigateToSettings?.Invoke();
}

/// <summary>Conquista perdível ou prioritária, para a visão geral.</summary>
public sealed record FlaggedItem(long AppId, string ApiName, string Name, string GameName, string? IconUrl, bool IsMissable, string RarityText, Rarity Rarity)
{
    public static FlaggedItem From(FlaggedAchievement f)
    {
        var (rarity, text) = AchievementItem.DescribeRarity(f.Achievement.GlobalPercent);
        return new FlaggedItem(f.AppId, f.Achievement.ApiName, f.Achievement.DisplayName, f.GameName,
            f.Achievement.IconGrayUrl ?? f.Achievement.IconUrl, f.Plan.Missable == Missable.Yes, text, rarity);
    }
}
