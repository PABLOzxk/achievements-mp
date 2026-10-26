using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Backups;

namespace AchievementsMP.App.Services;

/// <summary>
/// Backup automático diário, ao abrir e ao fechar o app. Roda fora da thread da interface (a pasta pode ser de rede
/// ou de um pendrive lento) e nunca derruba o app: a falha fica registrada para a tela de configurações mostrar.
/// </summary>
public sealed class AutoBackup(BackupService backups, ISettingsStore store, TimeProvider time)
{
    public string? LastError { get; private set; }

    public async Task<BackupFile?> RunIfDueAsync(CancellationToken ct = default)
    {
        try
        {
            var folder = (await store.LoadAsync(ct)).BackupFolder;
            var made = await Task.Run(() => backups.CreateIfDueAsync(folder, time.GetUtcNow(), ct), ct);
            LastError = null;
            if (made is not null)
                FileLog.Info($"Backup automático: {made.Path}");
            return made;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            FileLog.Error("Falha no backup automático", ex);
            return null;
        }
    }
}
