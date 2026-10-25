using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AchievementsMP.App.Services;
using AchievementsMP.App.ViewModels;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data;
using AchievementsMP.Data.Migrations;
using AchievementsMP.Data.Planning;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;
using AchievementsMP.Steam.Http;
using AchievementsMP.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementsMP.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => FileLog.Error("Exceção não tratada", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => { FileLog.Error("Tarefa com exceção não observada", args.Exception); args.SetObserved(); };

        var paths = AppPaths.ForCurrentUser();
        try
        {
            paths.EnsureCreated();
            FileLog.Initialize(paths.Logs);
            FileLog.Info("Iniciando.");

            var (factory, migration) = await AppDatabase.InitializeAsync(paths);
            if (migration.BackupPath is not null)
                FileLog.Info($"Banco migrado de v{migration.FromVersion} para v{migration.ToVersion}. Backup: {migration.BackupPath}");

            _services = ConfigureServices(paths, factory, migration.ToVersion);
            var window = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            MainWindow = window;
            window.Show();
            await ((MainViewModel)window.DataContext).InitializeAsync();
        }
        catch (DatabaseTooNewException ex)
        {
            Fail(ex.Message, ex);
        }
        catch (Exception ex)
        {
            Fail($"Não foi possível abrir o banco de dados local.\n\n{ex.Message}\n\nPasta: {paths.Root}", ex);
        }
    }

    internal static ServiceProvider ConfigureServices(AppPaths paths, SqliteConnectionFactory factory, int schemaVersion)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton(factory);
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsRepository>());
        services.AddSingleton<ISecretStore>(sp => sp.GetRequiredService<SettingsRepository>());
        services.AddSingleton<SteamCatalogRepository>();
        services.AddSingleton<PersonalDataRepository>();
        services.AddSingleton<PlannerRepository>();
        services.AddSingleton<GoalTracker>();
        services.AddSingleton<SyncRunRepository>();

        services.AddSingleton<ISteamWebApi>(sp =>
        {
            var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"AchievementsMP/{AppVersion}");
            var secrets = sp.GetRequiredService<ISecretStore>();
            return new SteamWebApiClient(http, secrets.GetSteamApiKeyAsync);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new SyncService(
            sp.GetRequiredService<ISteamWebApi>(), sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<SteamCatalogRepository>(),
            sp.GetRequiredService<SyncRunRepository>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<SyncCoordinator>();

        services.AddSingleton(new StatusInfo(paths.Root, schemaVersion, AppVersion));
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<GameDetailsViewModel>();
        services.AddSingleton<GoalsViewModel>();
        services.AddSingleton<PlannerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Error("Erro na interface", e.Exception);
        MessageBox.Show($"Ocorreu um erro inesperado. Detalhes foram gravados no log.\n\n{e.Exception.Message}",
            "Achievements MP", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void Fail(string message, Exception ex)
    {
        FileLog.Error("Falha na inicialização", ex);
        MessageBox.Show(message, "Achievements MP", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    // Linhas de jogo (estilo GameListItem em App.xaml): clique ou Enter abrem os detalhes.
    private void OnGameRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenGameFromRow(sender);

    private void OnGameRowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            OpenGameFromRow(sender);
            e.Handled = true;
        }
    }

    private static void OpenGameFromRow(object sender)
    {
        if (sender is System.Windows.Controls.ListBoxItem { DataContext: LibraryItem item } row
            && System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(row)?.DataContext is IGameOpener opener)
            opener.OpenGame(item.AppId);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
