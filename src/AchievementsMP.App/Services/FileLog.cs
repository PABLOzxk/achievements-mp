using System.IO;

namespace AchievementsMP.App.Services;

/// <summary>Log mínimo em arquivo, um por dia. Nunca registre a chave da API.</summary>
public static class FileLog
{
    private static readonly Lock Gate = new();
    private static string? _directory;

    public static void Initialize(string directory) => _directory = directory;

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
        catch (IOException)
        {
            // Log não pode derrubar o app.
        }
    }
}
