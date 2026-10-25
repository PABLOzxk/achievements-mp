using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public sealed partial class OverviewViewModel(SteamCatalogRepository catalog, AchievementsMP.Core.Settings.ISecretStore secrets) : PageViewModel
{
    public override string Title => "Visão geral";
    public override string Glyph => "\uE80F";

    internal Action? NavigateToSettings { get; set; }

    [ObservableProperty] private int _gameCount;
    [ObservableProperty] private int _unlockedCount;
    [ObservableProperty] private int _completedGames;
    [ObservableProperty] private int _nearlyComplete;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _needsSetup;
    [ObservableProperty] private IReadOnlyList<LibraryItem> _closest = [];

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
            .Select(LibraryItem.From)
            .ToList();
    }

    [RelayCommand]
    private void OpenSettings() => NavigateToSettings?.Invoke();
}
