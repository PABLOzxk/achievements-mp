using AchievementsMP.App.Services;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Sync;
using AchievementsMP.Xbox;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

/// <summary>Cartão "Xbox" em Configurações: conectar, ver com qual gamertag e desconectar.</summary>
public sealed partial class XboxAccountViewModel(IXboxSignIn signIn, XboxSyncService xboxSync, ISettingsStore store, CatalogRepository catalog,
    SyncCoordinator sync) : ObservableObject
{
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    public bool IsAvailable => signIn.IsAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _errorText = "";

    public async Task RefreshAsync()
    {
        ErrorText = "";
        if (!IsAvailable)
        {
            StatusText = "Esta versão ainda não tem a conexão com o Xbox.";
            IsConnected = false;
            return;
        }
        IsConnected = await signIn.HasAccountAsync();
        var gamertag = (await store.LoadAsync()).XboxGamertag;
        StatusText = IsConnected
            ? string.IsNullOrWhiteSpace(gamertag) ? "Conectado." : $"Conectado como {gamertag}."
            : "Não conectado.";
    }

    private bool CanConnect() => IsAvailable && !IsBusy && !IsConnected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsBusy = true;
        ErrorText = "";
        StatusText = "Abrindo o login da Microsoft no navegador…";
        try
        {
            using var timeout = new CancellationTokenSource(SignInTimeout);
            var token = await signIn.SignInAsync(timeout.Token);
            StatusText = "Conferindo sua conta Xbox…";
            var identity = await xboxSync.IdentifyAsync(token);
            if (!identity.IsSuccess)
            {
                // Login sem Xbox utilizável não fica guardado.
                await signIn.SignOutAsync();
                ErrorText = identity.Error!.Message;
                await RefreshAsync();
                ErrorText = identity.Error.Message;
                return;
            }
            FileLog.Info("Xbox conectado.");
            await RefreshAsync();
            if (sync.SyncNowCommand.CanExecute(null))
                sync.SyncNowCommand.Execute(null);
        }
        catch (XboxSignInException ex)
        {
            FileLog.Info($"Login do Xbox não concluído: {ex.Message}");
            await RefreshAsync();
            ErrorText = ex.Message;
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao conectar o Xbox", ex);
            await RefreshAsync();
            ErrorText = "Erro inesperado ao conectar o Xbox. Detalhes no log.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanDisconnect() => !IsBusy && IsConnected;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        var answer = System.Windows.MessageBox.Show(
            "Desconectar o Xbox? Os jogos do Xbox saem da biblioteca. Metas, listas e anotações deles ficam guardadas e voltam se você conectar de novo.",
            "Desconectar Xbox", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await signIn.SignOutAsync();
            await catalog.SetPlatformNotOwnedAsync(Platform.Xbox);
            var s = await store.LoadAsync();
            await store.SaveAsync(s with { XboxGamertag = null });
            FileLog.Info("Xbox desconectado.");
            sync.NotifyDataChanged();
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }
}
