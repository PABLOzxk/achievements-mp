using System.IO;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Backups;

namespace AchievementsMP.App.Services;

public sealed record BackupActionResult(bool Ok, string Message);

/// <summary>Escolher a pasta do backup, fazer um backup agora e descrever o último. Usado no guia e nas configurações.</summary>
public sealed class BackupActions(BackupService backups, ISettingsStore store, IShell shell, AppPaths paths, TimeProvider time)
{
    /// <summary>Pede a pasta e faz o primeiro backup nela; só grava a escolha se ele der certo. Nulo se o usuário cancelou.</summary>
    public async Task<BackupActionResult?> ChooseFolderAsync(string? current)
    {
        var folder = shell.PickFolder("Pasta para o backup automático (ex.: dentro do OneDrive ou Google Drive)", current);
        if (folder is null) return null;
        if (IsInsideDataFolder(folder))
            return new(false, "Escolha uma pasta fora da pasta de dados do app: um backup no mesmo lugar não protege contra perder o PC.");

        var result = await BackupNowAsync(folder);
        if (result.Ok)
        {
            var previous = await store.LoadAsync();
            await store.SaveAsync(previous with { BackupFolder = folder });
        }
        return result;
    }

    public async Task<BackupActionResult> BackupNowAsync(string folder)
    {
        try
        {
            var made = await Task.Run(() => backups.CreateAsync(folder, time.GetUtcNow()));
            FileLog.Info($"Backup manual: {made.Path}");
            return new(true, $"Backup feito: {Path.GetFileName(made.Path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            FileLog.Error("Falha no backup manual", ex);
            return new(false, $"Não foi possível gravar o backup nessa pasta: {ex.Message}");
        }
    }

    public async Task DisableAsync()
    {
        var previous = await store.LoadAsync();
        await store.SaveAsync(previous with { BackupFolder = null });
    }

    public static string DescribeLast(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "Sem backup automático, seus dados pessoais existem só neste computador.";
        var all = BackupService.List(folder);
        return all.Count == 0
            ? "Nenhum backup nesta pasta ainda."
            : $"Último backup: {all[0].CreatedUtc.ToLocalTime():dd/MM/yyyy 'às' HH:mm} · {all.Count} guardado(s), ficam os {BackupService.KeepCount} mais recentes.";
    }

    private bool IsInsideDataFolder(string folder)
    {
        var root = Path.GetFullPath(paths.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var chosen = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return chosen.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
