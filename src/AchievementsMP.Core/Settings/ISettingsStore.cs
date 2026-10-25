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

/// <summary>Criptografia local. A implementação real (DPAPI) só existe no Windows.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
