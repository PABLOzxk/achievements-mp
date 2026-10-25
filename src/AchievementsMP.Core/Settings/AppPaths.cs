namespace AchievementsMP.Core.Settings;

/// <summary>Onde o app guarda seus arquivos. Tudo fica em %LocalAppData%\AchievementsMP no Windows.</summary>
public sealed record AppPaths(string Root)
{
    public string Database => Path.Combine(Root, "achievements.db");
    public string Backups => Path.Combine(Root, "backups");
    public string Logs => Path.Combine(Root, "logs");

    public static AppPaths ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AchievementsMP"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
    }
}
