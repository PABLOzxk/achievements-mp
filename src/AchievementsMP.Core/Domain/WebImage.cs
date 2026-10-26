namespace AchievementsMP.Core.Domain;

/// <summary>
/// Endereços de ícones guardados no banco (vindos da Steam ou de um backup restaurado). Só https é exibido:
/// um caminho local ou de rede (\\servidor\...) faria o Windows abrir arquivos ou mandar suas credenciais para outra máquina.
/// </summary>
public static class WebImage
{
    public static Uri? TryParse(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)
            ? uri
            : null;
}
