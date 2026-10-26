using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;

namespace AchievementsMP.Sync;

/// <summary>
/// Atualiza o nome e a foto da Steam guardados para o cartão de perfil. Usa só o resumo do perfil (GetPlayerSummaries),
/// que a Steam mostra mesmo com o perfil privado. Falha de rede ou da Steam mantém o que já estava guardado.
/// </summary>
public sealed class ProfileRefresher(ISteamWebApi steam, ISettingsStore settings, ProfileRepository profiles)
{
    /// <summary>Verdadeiro se o que está guardado mudou (atualizado ou apagado).</summary>
    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var s = await settings.LoadAsync(ct);
        ulong? steamId = SteamIdentity.Parse(s.SteamProfile) switch
        {
            SteamProfileInput.SteamId id => id.Value,
            // Nome personalizado ainda não convertido: a sincronização converte, e a atualização seguinte acerta.
            SteamProfileInput.Vanity => s.SteamId64,
            _ => null,
        };

        var current = await profiles.LoadAsync(ct);
        if (steamId is null)
        {
            // Sem perfil configurado, o nome de uma conta antiga não pode continuar aparecendo como se fosse o atual.
            if (string.IsNullOrWhiteSpace(s.SteamProfile) && (current.SteamName is not null || current.SteamAvatarUrl is not null))
            {
                await profiles.SaveSteamAsync(null, null, ct);
                return true;
            }
            return false;
        }

        var summary = await steam.GetPlayerSummaryAsync(steamId.Value, ct);
        if (!summary.IsSuccess)
            return false;
        var who = summary.Value!;
        if (who.PersonaName == current.SteamName && who.AvatarUrl == current.SteamAvatarUrl)
            return false;
        await profiles.SaveSteamAsync(who.PersonaName, who.AvatarUrl, ct);
        return true;
    }
}
