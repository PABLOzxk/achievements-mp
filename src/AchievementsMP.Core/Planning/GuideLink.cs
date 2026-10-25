namespace AchievementsMP.Core.Planning;

/// <summary>
/// Links de guia digitados pelo usuário. Só http e https são aceitos: o link é aberto pelo sistema,
/// e um "file://" ou caminho local poderia executar um programa em vez de abrir uma página.
/// </summary>
public static class GuideLink
{
    public static bool TryParse(string? input, out Uri url)
    {
        url = null!;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace)) return false;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)) return false;
        if (!IsSafe(parsed) || !parsed.Host.Contains('.')) return false;
        url = parsed;
        return true;
    }

    /// <summary>Conferido de novo na hora de abrir, porque o valor vem do banco.</summary>
    public static bool IsSafe(Uri url) =>
        url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(url.Host);

    /// <summary>Texto curto para a lista: o título dado pelo usuário ou o site.</summary>
    public static string DisplayName(Uri url, string? title) =>
        !string.IsNullOrWhiteSpace(title) ? title.Trim() : url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? url.Host[4..] : url.Host;
}
