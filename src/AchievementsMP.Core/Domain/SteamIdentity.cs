using System.Text.RegularExpressions;

namespace AchievementsMP.Core.Domain;

/// <summary>O que o usuário digitou para identificar o perfil: um SteamID64 ou um nome personalizado (vanity URL).</summary>
public abstract record SteamProfileInput
{
    public sealed record SteamId(ulong Value) : SteamProfileInput;
    public sealed record Vanity(string Name) : SteamProfileInput;
}

public static partial class SteamIdentity
{
    // SteamID64 de contas individuais: universo público (1), tipo individual (1), instância 1.
    private const ulong IndividualBase = 76561197960265728UL;

    public static bool IsValidSteamId64(ulong value) =>
        value > IndividualBase && (value >> 32) == 0x01100001UL;

    /// <summary>
    /// Aceita: "7656119...", "https://steamcommunity.com/profiles/7656119...", "https://steamcommunity.com/id/nome", ou "nome".
    /// Retorna null para entradas claramente inválidas.
    /// </summary>
    public static SteamProfileInput? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var text = input.Trim().TrimEnd('/');

        var profile = ProfileUrl().Match(text);
        if (profile.Success)
            return ParseId(profile.Groups["id"].Value);

        var vanity = VanityUrl().Match(text);
        if (vanity.Success)
            return new SteamProfileInput.Vanity(vanity.Groups["name"].Value);

        if (text.All(char.IsAsciiDigit))
            return ParseId(text);

        return VanityName().IsMatch(text) ? new SteamProfileInput.Vanity(text) : null;
    }

    private static SteamProfileInput? ParseId(string digits) =>
        ulong.TryParse(digits, out var id) && IsValidSteamId64(id) ? new SteamProfileInput.SteamId(id) : null;

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?steamcommunity\.com/profiles/(?<id>\d{17})$", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileUrl();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?steamcommunity\.com/id/(?<name>[A-Za-z0-9_-]{2,32})$", RegexOptions.IgnoreCase)]
    private static partial Regex VanityUrl();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,32}$")]
    private static partial Regex VanityName();
}
