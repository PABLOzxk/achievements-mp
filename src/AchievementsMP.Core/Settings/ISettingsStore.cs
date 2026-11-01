namespace AchievementsMP.Core.Settings;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

/// <summary>Armazena segredos (a chave da Steam Web API). No Windows, protegido com DPAPI por usuário.</summary>
public interface ISecretStore
{
    Task<string?> GetSteamApiKeyAsync(CancellationToken ct = default);
    Task SetSteamApiKeyAsync(string? apiKey, CancellationToken ct = default);
}

/// <summary>Login de outra plataforma (ex.: Microsoft, para o Xbox) guardado entre uma abertura do app e outra, cifrado.</summary>
public interface ITokenCacheStore
{
    Task<byte[]?> LoadAsync(CancellationToken ct = default);
    /// <summary>Null apaga.</summary>
    Task SaveAsync(byte[]? data, CancellationToken ct = default);
}

/// <summary>Criptografia local. A implementação real (DPAPI) só existe no Windows.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
