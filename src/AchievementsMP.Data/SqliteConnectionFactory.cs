using Microsoft.Data.Sqlite;

namespace AchievementsMP.Data;

public sealed class SqliteConnectionFactory(string databasePath)
{
    public string DatabasePath { get; } = databasePath;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private,
        Pooling = false,
    }.ToString();

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var pragma = connection.CreateCommand();
            // WAL: leitura da UI não bloqueia enquanto a sincronização escreve.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await pragma.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            // Sem isto o arquivo ficaria preso até o coletor de lixo, e um banco danificado não poderia ser movido.
            await connection.DisposeAsync();
            throw;
        }
    }
}
