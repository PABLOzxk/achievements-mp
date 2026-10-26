using System.Collections.ObjectModel;
using AchievementsMP.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(OverviewViewModel overview, LibraryViewModel library, GameDetailsViewModel details, PlannerViewModel planner, SettingsViewModel settings,
        SyncCoordinator sync, StatusInfo status, AutoBackup backup)
    {
        _backup = backup;
        Pages = [overview, library, details, planner];
        Settings = settings;
        Sync = sync;
        Status = status;
        sync.DataChanged += () =>
        {
            if (CurrentPage is OverviewViewModel or LibraryViewModel or GameDetailsViewModel)
                _ = LoadAsync(CurrentPage);
        };
        overview.NavigateToSettings = () => Navigate(settings);
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

    public ObservableCollection<PageViewModel> Pages { get; }
    public SettingsViewModel Settings { get; }
    public SyncCoordinator Sync { get; }
    public StatusInfo Status { get; }

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
        await LoadAsync(CurrentPage);
        _ = _backup.RunIfDueAsync();
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

/// <summary>Informações do rodapé: onde estão os dados e em que versão o banco está.</summary>
public sealed record StatusInfo(string DataFolder, int SchemaVersion, string AppVersion)
{
    public string Text => $"Dados em {DataFolder}  ·  banco v{SchemaVersion}  ·  app v{AppVersion}";
}
