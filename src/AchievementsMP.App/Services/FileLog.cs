using System.IO;

namespace AchievementsMP.App.Services;

/// <summary>Log mínimo em arquivo, um por dia, guardado por <see cref="KeepDays"/> dias. Nunca registre a chave da API.</summary>
public static class FileLog
{
    public const int KeepDays = 30;
    private static readonly Lock Gate = new();
    private static string? _directory;

    public static void Initialize(string directory)
    {
        _directory = directory;
        try
        {
            var limit = DateTime.Now.AddDays(-KeepDays);
            foreach (var old in new DirectoryInfo(directory).EnumerateFiles("app-*.log").Where(f => f.LastWriteTime < limit))
                old.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Limpeza é opcional.
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        if (_directory is null) return;
        try
        {
            lock (Gate)
                File.AppendAllText(Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log"), $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Log não pode derrubar o app.
        }
    }
}
