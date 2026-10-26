using AchievementsMP.Core.Domain;
using AchievementsMP.Steam;

namespace AchievementsMP.Sync;

public enum CheckState { NotChecked, Ok, Problem }

/// <summary>Resultado do guia de configuração: cada passo com seu estado e o que fazer quando há problema.</summary>
public sealed record SetupCheck(
    CheckState Profile, string ProfileMessage,
    CheckState Key, string KeyMessage,
    CheckState Privacy, string PrivacyMessage,
    ulong? SteamId = null, string? PersonaName = null, int? GameCount = null,
    string? Notice = null)
{
    public bool AllOk => Profile == CheckState.Ok && Key == CheckState.Ok && Privacy == CheckState.Ok;
}

/// <summary>
/// Confere, na ordem em que a Steam deixa conferir, se perfil, chave e privacidade estão prontos para sincronizar.
/// Um passo só é marcado como problema quando a Steam diz que é; falha de rede deixa o passo "não conferido".
/// </summary>
public sealed class SetupChecker(ISteamWebApi steam)
{
    private const string NotChecked = "";

    public async Task<SetupCheck> CheckAsync(string? profile, bool hasKey, CancellationToken ct = default)
    {
        var input = SteamIdentity.Parse(profile);
        if (input is null)
        {
            return new SetupCheck(
                CheckState.Problem, string.IsNullOrWhiteSpace(profile)
                    ? "Cole o link do seu perfil Steam."
                    : "Não reconheci esse formato. Cole o endereço do perfil, como steamcommunity.com/id/seunome.",
                hasKey ? CheckState.NotChecked : CheckState.Problem, hasKey ? NotChecked : "Cole a sua chave.",
                CheckState.NotChecked, NotChecked);
        }
        if (!hasKey)
            return new SetupCheck(CheckState.NotChecked, "Será conferido junto com a chave.", CheckState.Problem, "Cole a sua chave.", CheckState.NotChecked, NotChecked);

        ulong steamId;
        if (input is SteamProfileInput.Vanity vanity)
        {
            var resolved = await steam.ResolveVanityUrlAsync(vanity.Name, ct);
            if (!resolved.IsSuccess)
                return FromError(resolved.Error!, profileStep: true);
            steamId = resolved.Value;
        }
        else
        {
            steamId = ((SteamProfileInput.SteamId)input).Value;
        }

        var summary = await steam.GetPlayerSummaryAsync(steamId, ct);
        if (!summary.IsSuccess)
            return FromError(summary.Error!, profileStep: true);
        var who = summary.Value!;
        var found = $"Encontrei o perfil de {who.PersonaName}.";
        if (!who.IsProfilePublic)
        {
            return new SetupCheck(CheckState.Ok, found, CheckState.Ok, "Chave aceita pela Steam.",
                CheckState.Problem, "Seu perfil está privado. Deixe “Meu perfil” como Público nas configurações de privacidade da Steam.",
                steamId, who.PersonaName);
        }

        var owned = await steam.GetOwnedGamesAsync(steamId, ct);
        if (!owned.IsSuccess)
        {
            if (owned.Error!.Kind == SteamErrorKind.PrivateProfile)
            {
                return new SetupCheck(CheckState.Ok, found, CheckState.Ok, "Chave aceita pela Steam.",
                    CheckState.Problem, "Os “Detalhes do jogo” estão privados, então a Steam não mostra sua biblioteca. Deixe essa opção como Pública.",
                    steamId, who.PersonaName);
            }
            return FromError(owned.Error, profileStep: false) with
            {
                Profile = CheckState.Ok, ProfileMessage = found, Key = CheckState.Ok, KeyMessage = "Chave aceita pela Steam.",
                SteamId = steamId, PersonaName = who.PersonaName,
            };
        }

        return new SetupCheck(CheckState.Ok, found, CheckState.Ok, "Chave aceita pela Steam.",
            CheckState.Ok, $"Perfil e biblioteca públicos: {owned.Value!.GameCount} jogos encontrados.",
            steamId, who.PersonaName, owned.Value.GameCount);
    }

    private static SetupCheck FromError(SteamError error, bool profileStep) => error.Kind switch
    {
        SteamErrorKind.InvalidApiKey => new SetupCheck(CheckState.NotChecked, "Será conferido quando a chave funcionar.",
            CheckState.Problem, "A Steam recusou a chave. Confira se copiou a chave inteira (32 letras e números) da página da chave.",
            CheckState.NotChecked, NotChecked),
        SteamErrorKind.NotFound when profileStep => new SetupCheck(CheckState.Problem,
            "A Steam não encontrou esse perfil. Abra seu perfil no navegador e copie o endereço da barra.",
            CheckState.NotChecked, NotChecked, CheckState.NotChecked, NotChecked),
        SteamErrorKind.PrivateProfile => new SetupCheck(CheckState.NotChecked, NotChecked, CheckState.NotChecked, NotChecked,
            CheckState.Problem, "A Steam diz que o perfil é privado. Deixe “Meu perfil” e “Detalhes do jogo” como Públicos."),
        SteamErrorKind.RateLimited => Unreachable("A Steam pediu para esperar um pouco. Tente de novo em alguns minutos."),
        SteamErrorKind.Network => Unreachable("Sem conexão com a Steam. Verifique sua internet e tente de novo."),
        _ => Unreachable($"A Steam não respondeu direito agora ({error.Message}). Tente de novo em alguns minutos."),
    };

    // Problema do lado da Steam ou da rede: nada foi provado errado, então nenhum passo vira "problema".
    private static SetupCheck Unreachable(string message) =>
        new(CheckState.NotChecked, NotChecked, CheckState.NotChecked, NotChecked, CheckState.NotChecked, NotChecked, Notice: message);
}
