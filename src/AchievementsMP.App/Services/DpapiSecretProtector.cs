using System.Security.Cryptography;
using System.Text;
using AchievementsMP.Core.Settings;

namespace AchievementsMP.App.Services;

/// <summary>
/// DPAPI escopo usuário: só o mesmo usuário do Windows, na mesma máquina, consegue decifrar.
/// Não protege contra malware rodando como você; protege contra alguém copiar o arquivo do banco.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AchievementsMP/steam-api-key/v1");

    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
