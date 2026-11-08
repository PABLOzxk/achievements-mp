using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;

namespace AchievementsMP.Sync;

/// <param name="Found">Jogos emprestados que você já jogou, segundo a Steam.</param>
/// <param name="Added">Quantos deles ainda não estavam na biblioteca do app.</param>
public sealed record FamilyImportResult(bool Ok, string Message, int Found = 0, int Added = 0);

/// <summary>
/// Importação única da Família Steam: com o token colado pelo usuário, lê a biblioteca compartilhada e traz para o app
/// os jogos emprestados que ele já jogou. O token é usado nas duas chamadas e esquecido; nada aqui o guarda ou registra.
/// Jogo da família que ele nunca abriu fica de fora: entraria com 0% e só poluiria a biblioteca e as listas de viáveis.
/// </summary>
public sealed class FamilyImporter(ISteamFamilyApi family, ISettingsStore settings, SteamCatalogRepository catalog, TimeProvider time)
{
    public async Task<FamilyImportResult> ImportAsync(string? pasted, CancellationToken ct = default)
    {
        var token = SteamAccessToken.Parse(pasted);
        if (token is null)
            return Fail("Não reconheci o que foi colado. Abra a página do token, copie tudo (Ctrl+A, Ctrl+C) e cole de novo.");

        var s = await settings.LoadAsync(ct);
        var steamId = s.SteamId64 ?? (SteamIdentity.Parse(s.SteamProfile) as SteamProfileInput.SteamId)?.Value;
        if (steamId is not { } me || await catalog.GetOwnerSteamIdAsync(ct) != me)
            return Fail("Sincronize a sua biblioteca pelo menos uma vez antes de importar a família.");

        if (token.SteamId is { } sub && sub != me)
            return Fail("Esse token é de outra conta Steam. Entre na loja com a mesma conta configurada aqui e copie de novo.");
        if (token.ExpiresUtc is { } exp && exp <= time.GetUtcNow())
            return Fail("Esse token já venceu. Abra a página do token de novo e copie outra vez.");

        var group = await family.GetFamilyGroupIdAsync(token.Value, me, ct);
        if (!group.IsSuccess)
            return Fail(Describe(group.Error!));
        if (group.Value is not { } groupId)
            return Fail("A Steam diz que esta conta não está em nenhuma Família Steam.");

        var apps = await family.GetSharedLibraryAppsAsync(token.Value, groupId, me, s.Language, ct);
        if (!apps.IsSuccess)
            return Fail(Describe(apps.Error!));

        var played = apps.Value!
            .Where(a => !a.OwnerSteamIds.Contains(me) && a.PlaytimeMinutes > 0)
            .Select(a => new Game(a.AppId, a.Name, a.IconHash, a.PlaytimeMinutes, a.LastPlayedUtc, null))
            .ToList();
        var shared = apps.Value!.Count(a => !a.OwnerSteamIds.Contains(me));

        if (played.Count == 0)
            return new FamilyImportResult(true,
                $"A família compartilha {shared} jogos com você, mas a Steam não registra tempo de jogo seu em nenhum deles. Nada foi adicionado.");

        var added = await catalog.UpsertFamilyGamesAsync(played, time.GetUtcNow(), ct);
        var message = added == played.Count
            ? $"{Plural(added, "jogo da família adicionado", "jogos da família adicionados")}."
            : $"{Plural(played.Count, "jogo da família encontrado", "jogos da família encontrados")}, {added} novos na biblioteca.";
        return new FamilyImportResult(true, message + " O token foi descartado. As conquistas chegam na próxima sincronização.", played.Count, added);
    }

    private static FamilyImportResult Fail(string message) => new(false, message);

    private static string Plural(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    private static string Describe(SteamError e) => e.Kind switch
    {
        SteamErrorKind.InvalidAccessToken => "A Steam recusou o token. Ele pode ter vencido ou sido copiado pela metade. Abra a página de novo e copie tudo.",
        SteamErrorKind.Network or SteamErrorKind.Transient => "Sem conexão com a Steam agora. Tente de novo daqui a pouco.",
        SteamErrorKind.RateLimited => "A Steam pediu para esperar antes de novas consultas. Tente de novo mais tarde.",
        // A Valve não documenta essa rota: se mudar o formato, dizemos isso em vez de fingir que a família está vazia.
        SteamErrorKind.InvalidResponse or SteamErrorKind.NotFound => "A Steam respondeu num formato que o app não conhece. Ela pode ter mudado essa função.",
        _ => $"Não foi possível ler a família: {e.Message}",
    };
}
