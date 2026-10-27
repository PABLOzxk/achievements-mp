using System.Collections.ObjectModel;
using AchievementsMP.App.Services;
using AchievementsMP.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(OverviewViewModel overview, LibraryViewModel library, GameDetailsViewModel details, PlannerViewModel planner, SettingsViewModel settings,
        SetupViewModel setup, SyncCoordinator sync, StatusInfo status, AutoBackup backup, ISettingsStore settingsStore, ISecretStore secrets, AppUpdater updater,
        ProfileViewModel profile)
    {
        Updater = updater;
        _backup = backup;
        _setup = setup;
        _settingsStore = settingsStore;
        _secrets = secrets;
        Pages = [overview, library, details, planner, settings];
        Profile = profile;
        Sync = sync;
        Status = status;
        sync.DataChanged += () =>
        {
            if (CurrentPage is OverviewViewModel or LibraryViewModel or GameDetailsViewModel)
                _ = LoadAsync(CurrentPage);
            // Fim de uma sincronização (o perfil pode ter sido trocado nas configurações): nome e foto da Steam de novo.
            if (!sync.IsRunning)
                _ = Profile.RefreshFromSteamAsync();
        };
        overview.NavigateToSettings = settings.NavigateToSetup = () => Show(setup);
        setup.NavigateToOverview = () => Show(overview);
        overview.NavigateToGame = library.NavigateToGame = planner.NavigateToGame = appId =>
        {
            details.Show(appId);
            Show(details);
        };
        overview.NavigateToPlanner = (appId, apiName) =>
        {
            planner.ShowGame(appId, apiName);
            Show(planner);
        };
        library.NavigateToPlanner = details.NavigateToPlanner = appId =>
        {
            planner.ShowGame(appId);
            Show(planner);
        };
        overview.NavigateToGoals = () =>
        {
            planner.ShowGoals();
            Show(planner);
        };
        _planner = planner;
        _currentPage = overview;
        overview.IsActive = true;
    }

    private readonly PlannerViewModel _planner;
    private readonly AutoBackup _backup;
    private readonly SetupViewModel _setup;
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secrets;

    public ObservableCollection<PageViewModel> Pages { get; }
    public ProfileViewModel Profile { get; }
    public SyncCoordinator Sync { get; }
    public StatusInfo Status { get; }
    public AppUpdater Updater { get; }

    /// <summary>Grava o que estiver pendente, faz o backup do dia e reabre o app já na versão nova.</summary>
    [RelayCommand]
    private async Task RestartToUpdateAsync()
    {
        await BeforeExitAsync();
        Updater.ApplyAndRestart();
    }

    [ObservableProperty]
    private PageViewModel _currentPage;

    partial void OnCurrentPageChanged(PageViewModel? oldValue, PageViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        newValue.IsActive = true;
        _ = LoadAsync(newValue);
    }

    [RelayCommand]
    private void Navigate(PageViewModel page) => CurrentPage = page;

    /// <summary>Vai para a página, ou recarrega se ela já estiver aberta.</summary>
    private void Show(PageViewModel page)
    {
        if (CurrentPage == page)
            _ = LoadAsync(page);
        else
            Navigate(page);
    }

    /// <summary>Grava edições ainda pendentes (o planejador grava ao trocar de conquista ou sair da tela).</summary>
    public Task FlushAsync() => _planner.FlushAsync();

    /// <summary>Antes de fechar: grava o que estiver em edição e faz o backup do dia, se ainda não foi feito.</summary>
    public async Task BeforeExitAsync()
    {
        try
        {
            await FlushAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao gravar edições pendentes ao fechar", ex);
        }
        // Uma pasta lenta (rede, pendrive) não pode prender o fechamento.
        await Task.WhenAny(_backup.RunIfDueAsync(), Task.Delay(TimeSpan.FromSeconds(10)));
    }

    public async Task InitializeAsync()
    {
        try
        {
            await Profile.LoadAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao carregar o perfil", ex);
        }
        // Primeira vez (sem perfil e sem chave): abre direto o guia em vez da visão geral vazia.
        if (string.IsNullOrWhiteSpace((await _settingsStore.LoadAsync()).SteamProfile) && string.IsNullOrEmpty(await _secrets.GetSteamApiKeyAsync()))
            CurrentPage = _setup;
        else
            await LoadAsync(CurrentPage);
        _ = _backup.RunIfDueAsync();
        _ = Updater.CheckAsync(userAsked: false);
        _ = Profile.RefreshFromSteamAsync();
        try
        {
            await Sync.InitializeAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao iniciar a sincronização automática", ex);
        }
    }

    private static async Task LoadAsync(PageViewModel page)
    {
        try
        {
            await page.OnNavigatedToAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error($"Falha ao abrir a página {page.Title}", ex);
        }
    }
}

/// <summary>Onde estão os dados e em que versão estão o banco e o app. A janela mostra só a versão do app, no canto.</summary>
public sealed record StatusInfo(string DataFolder, int SchemaVersion, string AppVersion)
{
    public string VersionText => $"v{AppVersion}";
}
