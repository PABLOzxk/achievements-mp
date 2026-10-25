using System.Text;
using System.Text.Json;
using AchievementsMP.Core.Settings;
using Dapper;

namespace AchievementsMP.Data.Repositories;

/// <summary>Configurações em JSON na tabela <c>settings</c>; a chave da API vai cifrada pelo <see cref="ISecretProtector"/>.</summary>
public sealed class SettingsRepository(SqliteConnectionFactory factory, ISecretProtector protector) : ISettingsStore, ISecretStore
{
    private const string SettingsKey = "app_settings";
    private const string ApiKeyKey = "steam_api_key";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var raw = await ReadAsync(SettingsKey, ct);
        if (raw is null)
            return AppSettings.Default;
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(raw, Json) ?? AppSettings.Default;
        }
        catch (JsonException)
        {
            // Configuração corrompida não pode impedir o app de abrir.
            return AppSettings.Default;
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default) =>
        WriteAsync(SettingsKey, JsonSerializer.SerializeToUtf8Bytes(settings, Json), ct);

    public async Task<string?> GetSteamApiKeyAsync(CancellationToken ct = default)
    {
        var cipher = await ReadAsync(ApiKeyKey, ct);
        if (cipher is null)
            return null;
        try
        {
            return Encoding.UTF8.GetString(protector.Unprotect(cipher));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            // Ex.: banco copiado de outro usuário do Windows. Trata como "sem chave" e o usuário informa de novo.
            return null;
        }
    }

    public async Task SetSteamApiKeyAsync(string? apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await DeleteAsync(ApiKeyKey, ct);
            return;
        }
        await WriteAsync(ApiKeyKey, protector.Protect(Encoding.UTF8.GetBytes(apiKey.Trim())), ct);
    }

    private async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<byte[]?>(
            new CommandDefinition("SELECT value FROM settings WHERE key = @key", new { key }, cancellationToken: ct));
    }

    private async Task WriteAsync(string key, byte[] value, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT INTO settings(key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { key, value }, cancellationToken: ct));
    }

    private async Task DeleteAsync(string key, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM settings WHERE key = @key", new { key }, cancellationToken: ct));
    }
}
