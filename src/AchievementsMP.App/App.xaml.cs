using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AchievementsMP.App.Services;
using AchievementsMP.App.ViewModels;
using AchievementsMP.App.Views;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Migrations;
using AchievementsMP.Data.Planning;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;
using AchievementsMP.Steam.Http;
using AchievementsMP.Sync;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace AchievementsMP.App;

public partial class App : Application
{
    /// <summary>Passado pela instância que reinicia o app: a nova espera a antiga fechar antes de abrir o banco.</summary>
    internal const string WaitForPidArgument = "--wait-pid";

    private ServiceProvider? _services;

    [STAThread]
    public static void Main()
    {
        // Instalar, atualizar e desinstalar chamam o exe com argumentos próprios: o Velopack trata e encerra antes do WPF.
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

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

            await WaitForPreviousInstanceAsync(e.Args);
            if (!TryBecomeTheOnlyInstance(waitForPrevious: e.Args.Contains(WaitForPidArgument)))
            {
                FileLog.Info("Já existe uma janela do app aberta; esta instância foi encerrada.");
                MessageBox.Show("O Achievements MP já está aberto. Procure a janela dele na barra de tarefas.",
                    "Achievements MP", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            var database = await OpenDatabaseAsync(paths);
            if (database is null)
            {
                Shutdown(1);
                return;
            }
            var (factory, migration, restore) = database.Value;
            if (migration.BackupPath is not null)
                FileLog.Info($"Banco migrado de v{migration.FromVersion} para v{migration.ToVersion}. Backup: {migration.BackupPath}");
            ReportRestore(restore);

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
        services.AddSingleton<BackupService>();
        services.AddSingleton<AutoBackup>();
        services.AddSingleton<IShell, WpfShell>();
        services.AddSingleton<BackupActions>();
        services.AddSingleton<AppUpdater>();

        services.AddSingleton<ISteamWebApi>(sp =>
        {
            var secrets = sp.GetRequiredService<ISecretStore>();
            return new SteamWebApiClient(SteamWebApiClient.CreateHttpClient($"AchievementsMP/{AppVersion}"), secrets.GetSteamApiKeyAsync);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new SyncService(
            sp.GetRequiredService<ISteamWebApi>(), sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<SteamCatalogRepository>(),
            sp.GetRequiredService<SyncRunRepository>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton(sp => new SetupChecker(sp.GetRequiredService<ISteamWebApi>()));

        services.AddSingleton(new StatusInfo(paths.Root, schemaVersion, AppVersion));
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<GameDetailsViewModel>();
        services.AddSingleton<GoalsViewModel>();
        services.AddSingleton<PlannerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider();
    }

    // Duas janelas abertas brigariam pelo mesmo banco (sincronizações em dobro, restauração que não consegue trocar o arquivo).
    private static Mutex? _instanceLock;

    private static bool TryBecomeTheOnlyInstance(bool waitForPrevious)
    {
        _instanceLock = new Mutex(initiallyOwned: true, @"Local\AchievementsMP.App", out var createdNew);
        if (createdNew)
            return true;
        try
        {
            // Num reinício (restauração), a instância anterior ainda pode estar terminando de fechar.
            return _instanceLock.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(30) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            return true; // a anterior caiu sem liberar; agora é desta
        }
    }

    /// <summary>
    /// Abre o banco. Se o arquivo estiver danificado, pergunta o que fazer (restaurar um backup ou começar do zero)
    /// e tenta de novo; nada é apagado. Nulo = o usuário preferiu fechar.
    /// </summary>
    private async Task<(SqliteConnectionFactory, MigrationResult, RestoreOutcome?)?> OpenDatabaseAsync(AppPaths paths)
    {
        while (true)
        {
            try
            {
                return await AppDatabase.InitializeAsync(paths);
            }
            catch (Exception ex) when (AppDatabase.IsDamaged(ex))
            {
                FileLog.Error("Banco local danificado", ex);
                // A janela de recuperação não é a principal: fechá-la não pode encerrar o app.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var dialog = new DatabaseRecoveryWindow(ex.Message, paths.Backups);
                dialog.ShowDialog();
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow = null;

                switch (dialog.Choice)
                {
                    case RecoveryChoice.Restore:
                        await StageBackupForDamagedDatabaseAsync(paths);
                        break;
                    case RecoveryChoice.StartOver:
                        var kept = AppDatabase.SetAsideDamaged(paths, DateTimeOffset.UtcNow);
                        FileLog.Info($"Começando do zero. Banco danificado guardado em {kept}");
                        break;
                    default:
                        return null;
                }
            }
        }
    }

    private static async Task StageBackupForDamagedDatabaseAsync(AppPaths paths)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o backup para restaurar",
            Filter = "Backup do Achievements MP (*.db)|*.db|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            await BackupService.StageRestoreAsync(paths, dialog.FileName);
        }
        catch (Exception ex) when (ex is InvalidBackupException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Este arquivo não pode ser usado.\n\n{ex.Message}", "Achievements MP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Task WaitForPreviousInstanceAsync(string[] args)
    {
        var i = Array.IndexOf(args, WaitForPidArgument);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid))
            return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                using var previous = Process.GetProcessById(pid);
                previous.WaitForExit(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                // Já fechou.
            }
        });
    }

    private static void ReportRestore(RestoreOutcome? restore)
    {
        if (restore is null) return;
        if (restore.Restored)
        {
            FileLog.Info($"Backup restaurado. Cópia dos dados anteriores: {restore.SafetyCopyPath}");
            MessageBox.Show("Backup restaurado." + (restore.SafetyCopyPath is { } copy
                    ? $"\n\nOs dados que estavam no app antes ficaram guardados em:\n{copy}" : ""),
                "Achievements MP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            FileLog.Error($"Restauração cancelada: {restore.Error}");
            MessageBox.Show($"Não foi possível restaurar o backup. Seus dados atuais não foram alterados.\n\n{restore.Error}",
                "Achievements MP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}
