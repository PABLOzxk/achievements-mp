using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.Services;

/// <summary>
/// Ponte entre a sincronização e a interface: botão Sincronizar/Cancelar, progresso, sincronização ao abrir e periódica.
/// A sincronização roda fora da thread da interface, então a janela nunca trava.
/// </summary>
public sealed partial class SyncCoordinator(SyncService service, ISettingsStore settings, ISecretStore secrets, SyncRunRepository runs, TimeProvider time)
    : ObservableObject, IDisposable
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(5);
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty] private bool _lastFailed;

    /// <summary>Dados no banco mudaram (durante e ao fim da sincronização). As telas recarregam.</summary>
    public event Action? DataChanged;

    public async Task InitializeAsync()
    {
        await RefreshIdleStatusAsync();
        var s = await settings.LoadAsync();
        if (s.SyncOnStartup && await IsDueAsync(s))
            await RunAsync(force: false);
        _ = AutoSyncLoopAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private Task SyncNowAsync() => RunAsync(force: false);

    private bool CanSync() => !IsRunning;

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelando…";
    }

    private async Task RunAsync(bool force)
    {
        if (IsRunning) return;
        if (string.IsNullOrEmpty(await secrets.GetSteamApiKeyAsync()))
        {
            StatusText = "Informe a chave da Steam em Configurações.";
            LastFailed = true;
            return;
        }

        IsRunning = true;
        LastFailed = false;
        IsIndeterminate = true;
        ProgressPercent = 0;
        StatusText = "Conectando à Steam…";
        _cts = new CancellationTokenSource();
        var progress = new Progress<SyncProgress>(OnProgress); // criado na thread da interface

        try
        {
            var token = _cts.Token;
            var result = await Task.Run(() => service.SyncAsync(progress, force, token));
            FileLog.Info($"Sincronização: {result.Outcome}. {result.Message}");
            LastFailed = result.Outcome == SyncOutcome.Failed;
            StatusText = result.Outcome switch
            {
                SyncOutcome.Succeeded => $"Atualizado às {time.GetLocalNow():HH:mm}. {result.Message}",
                SyncOutcome.PartiallySucceeded => $"Atualizado às {time.GetLocalNow():HH:mm}. {result.Message}",
                _ => result.Message,
            };
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha inesperada na sincronização", ex);
            LastFailed = true;
            StatusText = "Erro inesperado ao sincronizar. Os dados anteriores foram mantidos; detalhes no log.";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
            IsIndeterminate = false;
            DataChanged?.Invoke();
        }
    }

    private void OnProgress(SyncProgress p)
    {
        switch (p.Stage)
        {
            case SyncStage.Library:
                StatusText = "Lendo sua biblioteca…";
                break;
            case SyncStage.Games when p.Total > 0:
                IsIndeterminate = false;
                ProgressPercent = 100.0 * p.Done / p.Total;
                StatusText = $"Conquistas: {p.Done} de {p.Total}\n{p.CurrentGame}";
                if (p.Done % 25 == 0) DataChanged?.Invoke();
                break;
        }
    }

    private async Task AutoSyncLoopAsync()
    {
        _timer = new PeriodicTimer(CheckEvery, time);
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                if (!IsRunning && await IsDueAsync(await settings.LoadAsync()))
                    await RunAsync(force: false);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha no agendamento de sincronização", ex);
        }
    }

    private async Task<bool> IsDueAsync(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.SteamProfile) || string.IsNullOrEmpty(await secrets.GetSteamApiKeyAsync()))
            return false;
        var last = await runs.GetLastAsync(SyncRunRepository.LibraryScope, null, SyncStatus.Succeeded);
        return last?.FinishedUtc is not { } finished || time.GetUtcNow() - finished >= TimeSpan.FromMinutes(s.SyncIntervalMinutes);
    }

    private async Task RefreshIdleStatusAsync()
    {
        var last = await runs.GetLastAsync(SyncRunRepository.LibraryScope, null, SyncStatus.Succeeded);
        StatusText = last?.FinishedUtc is { } finished
            ? $"Última atualização: {finished.ToLocalTime():dd/MM HH:mm}"
            : "Ainda não sincronizado.";
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _timer?.Dispose();
    }
}
