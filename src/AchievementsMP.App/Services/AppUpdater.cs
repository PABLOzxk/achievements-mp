using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace AchievementsMP.App.Services;

/// <summary>
/// Atualização automática pelas Releases do GitHub (Velopack). Só funciona na versão instalada pelo Setup;
/// o zip portátil continua sendo atualizado à mão. Baixa em segundo plano e só troca o app quando o usuário
/// manda reiniciar, ou sozinho na próxima abertura.
/// </summary>
public sealed partial class AppUpdater : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/PABLOzxk/achievements-mp";

    // Criado só quando usado: fora do app instalado (testes, "dotnet run") o Velopack não está inicializado.
    private readonly Lazy<UpdateManager?> _manager = new(() =>
    {
        try
        {
            return new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch (InvalidOperationException ex)
        {
            FileLog.Info($"Atualizador indisponível: {ex.Message}");
            return null;
        }
    });
    private UpdateInfo? _ready;

    [ObservableProperty] private bool _isUpdateReady;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private string _readyVersion = "";
    [ObservableProperty] private string _statusText = "";

    public bool IsInstalled => _manager.Value?.IsInstalled == true;

    public async Task CheckAsync(bool userAsked)
    {
        if (IsChecking || IsUpdateReady) return;
        if (_manager.Value is not { IsInstalled: true } manager)
        {
            StatusText = "Esta cópia não foi instalada pelo instalador, então não se atualiza sozinha. Baixe a versão nova na página do GitHub.";
            return;
        }

        IsChecking = true;
        try
        {
            if (userAsked) StatusText = "Procurando atualizações…";
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                StatusText = $"Você está na versão mais recente ({manager.CurrentVersion}).";
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            StatusText = $"Baixando a versão {version}…";
            await manager.DownloadUpdatesAsync(update, p => StatusText = $"Baixando a versão {version}… {p}%");
            _ready = update;
            ReadyVersion = version;
            IsUpdateReady = true;
            StatusText = $"A versão {version} está pronta. Ela entra quando você reiniciar o app.";
            FileLog.Info($"Atualização {version} baixada.");
        }
        catch (Exception ex)
        {
            // Sem internet ou GitHub fora do ar: não é motivo para incomodar quem não pediu.
            FileLog.Error("Falha ao procurar atualizações", ex);
            StatusText = userAsked ? $"Não foi possível procurar atualizações agora: {ex.Message}" : "";
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Troca pela versão baixada e reabre. Chame só depois de gravar o que estiver pendente.</summary>
    public void ApplyAndRestart()
    {
        if (_ready is null || _manager.Value is not { } manager) return;
        FileLog.Info($"Aplicando atualização {ReadyVersion}.");
        manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }
}
