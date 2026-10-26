using System.Text;
using AchievementsMP.Core.Profile;
using Dapper;

namespace AchievementsMP.Data.Repositories;

/// <summary>
/// Perfil da barra lateral, na tabela <c>settings</c>: como fica no banco, entra no backup e volta com a restauração.
/// Cada campo tem a sua chave, para a atualização vinda da Steam e a edição do usuário nunca apagarem uma à outra.
/// </summary>
public sealed class ProfileRepository(SqliteConnectionFactory factory)
{
    private const string SteamNameKey = "profile_steam_name";
    private const string SteamAvatarUrlKey = "profile_steam_avatar_url";
    private const string CustomNameKey = "profile_custom_name";
    private const string CustomAvatarKey = "profile_custom_avatar";

    /// <summary>
    /// Teto da foto guardada. A que o app grava tem bem menos (PNG de 256 px); maior que isso só vem de um banco
    /// adulterado, e é ignorada em vez de ser aberta.
    /// </summary>
    public const int MaxAvatarBytes = 2 * 1024 * 1024;

    public async Task<UserProfile> LoadAsync(CancellationToken ct = default)
    {
        await using var c = await factory.OpenAsync(ct);
        var rows = (await c.QueryAsync<(string Key, byte[]? Value)>(new CommandDefinition(
            "SELECT key, value FROM settings WHERE key IN (@SteamNameKey, @SteamAvatarUrlKey, @CustomNameKey, @CustomAvatarKey)",
            new { SteamNameKey, SteamAvatarUrlKey, CustomNameKey, CustomAvatarKey }, cancellationToken: ct)))
            .ToDictionary(r => r.Key, r => r.Value);

        string? Text(string key) => rows.GetValueOrDefault(key) is { Length: > 0 } bytes ? Encoding.UTF8.GetString(bytes) : null;
        var avatar = rows.GetValueOrDefault(CustomAvatarKey) is { Length: > 0 and <= MaxAvatarBytes } image ? image : null;
        return new UserProfile(Text(SteamNameKey), Text(SteamAvatarUrlKey), Text(CustomNameKey), avatar);
    }

    /// <summary>Nome e foto atuais na Steam. Nulos apagam (perfil não configurado).</summary>
    public Task SaveSteamAsync(string? name, string? avatarUrl, CancellationToken ct = default) =>
        WriteAsync([(SteamNameKey, Utf8(name)), (SteamAvatarUrlKey, Utf8(avatarUrl))], ct);

    /// <summary>Nome e foto escolhidos pelo usuário. Nulo = usar o da Steam.</summary>
    public Task SaveChoicesAsync(string? customName, byte[]? customAvatar, CancellationToken ct = default)
    {
        if (customAvatar is { Length: > MaxAvatarBytes })
            throw new ArgumentException("Foto grande demais para guardar.", nameof(customAvatar));
        return WriteAsync([(CustomNameKey, Utf8(customName)), (CustomAvatarKey, customAvatar is { Length: > 0 } ? customAvatar : null)], ct);
    }

    private static byte[]? Utf8(string? text) => string.IsNullOrEmpty(text) ? null : Encoding.UTF8.GetBytes(text);

    private async Task WriteAsync(IReadOnlyList<(string Key, byte[]? Value)> values, CancellationToken ct)
    {
        await using var c = await factory.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var (key, value) in values)
        {
            await c.ExecuteAsync(new CommandDefinition(value is null
                    ? "DELETE FROM settings WHERE key = @key"
                    : "INSERT INTO settings(key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                new { key, value }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }
}
