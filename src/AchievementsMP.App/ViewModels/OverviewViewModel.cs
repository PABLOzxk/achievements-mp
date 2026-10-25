using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public sealed partial class OverviewViewModel(SteamCatalogRepository catalog, AchievementsMP.Core.Settings.ISecretStore secrets) : PageViewModel
{
    public override string Title => "Visão geral";
    public override string Glyph => "";

    internal Action? NavigateToSettings { get; set; }

    [ObservableProperty] private int _gameCount;
    [ObservableProperty] private int _unlockedCount;
    [ObservableProperty] private int _completedGames;
    [ObservableProperty] private int _nearlyComplete;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _needsSetup;

    public override async Task OnNavigatedToAsync()
    {
        var library = await catalog.GetLibraryAsync();
        GameCount = library.Count;
        UnlockedCount = library.Sum(g => g.Progress.Unlocked);
        CompletedGames = library.Count(g => g.Progress.IsComplete);
        NearlyComplete = library.Count(g => g.Progress.Percent is >= 80 && !g.Progress.IsComplete);
        HasData = library.Count > 0;
        NeedsSetup = string.IsNullOrEmpty(await secrets.GetSteamApiKeyAsync());
    }

    [RelayCommand]
    private void OpenSettings() => NavigateToSettings?.Invoke();
}
