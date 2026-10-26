namespace AchievementsMP.Core.Profile;

/// <summary>
/// Perfil mostrado no topo da barra lateral. Começa com o nome e a foto da Steam; o usuário pode trocar os dois.
/// A escolha dele vale sobre a da Steam, e "voltar para a da Steam" é só apagar a escolha.
/// </summary>
/// <param name="CustomAvatar">Foto escolhida, já reduzida pelo app (PNG quadrado). Fica no banco, então entra no backup.</param>
public sealed record UserProfile(string? SteamName, string? SteamAvatarUrl, string? CustomName, byte[]? CustomAvatar)
{
    /// <summary>Mesmo limite de nome da Steam.</summary>
    public const int MaxNameLength = 32;

    public static readonly UserProfile Empty = new(null, null, null, null);

    /// <summary>Nulo enquanto não há nome nenhum (antes da primeira consulta à Steam e sem nome escolhido).</summary>
    public string? Name => CustomName ?? SteamName;

    /// <summary>
    /// Nome digitado, pronto para gravar: sem espaços nas pontas nem caracteres de controle, no máximo <see cref="MaxNameLength"/>.
    /// Vazio ou igual ao da Steam vira nulo, para continuar acompanhando o nome da Steam se ele mudar lá.
    /// </summary>
    public static string? NormalizeName(string? typed, string? steamName)
    {
        if (typed is null) return null;
        var clean = new string(typed.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxNameLength)
        {
            // Não corta um emoji (par de surrogates) ao meio.
            var cut = char.IsHighSurrogate(clean[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;
            clean = clean[..cut].TrimEnd();
        }
        return clean.Length == 0 || clean == steamName ? null : clean;
    }
}
