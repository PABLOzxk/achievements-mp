using System.Text;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data;
using AchievementsMP.Data.Migrations;

namespace AchievementsMP.Tests;

internal static class Fixture
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

/// <summary>Banco SQLite num diretório temporário próprio, apagado ao final do teste.</summary>
internal sealed class TempDatabase : IAsyncDisposable
{
    public AppPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "amp-tests", Guid.NewGuid().ToString("N")));
    public SqliteConnectionFactory Factory { get; }

    private TempDatabase()
    {
        Paths.EnsureCreated();
        Factory = new SqliteConnectionFactory(Paths.Database);
    }

    public static async Task<TempDatabase> CreateMigratedAsync()
    {
        var db = new TempDatabase();
        await new MigrationRunner(db.Factory, db.Paths.Backups).MigrateAsync();
        return db;
    }

    public static TempDatabase CreateEmpty() => new();

    public ValueTask DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Paths.Root, recursive: true); } catch (IOException) { }
        return ValueTask.CompletedTask;
    }
}

/// <summary>Substituto do DPAPI nos testes: inverte e marca os bytes, só para provar que nada fica em texto puro.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FAKE");
    public byte[] Protect(byte[] plaintext) => [.. Magic, .. plaintext.Reverse()];
    public byte[] Unprotect(byte[] ciphertext)
    {
        if (!ciphertext.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new System.Security.Cryptography.CryptographicException("not ours");
        return ciphertext[Magic.Length..].Reverse().ToArray();
    }
}
