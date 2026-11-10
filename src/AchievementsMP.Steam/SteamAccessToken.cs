using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AchievementsMP.Steam;

/// <summary>
/// Token de sessão da loja Steam ("webapi_token"), colado pelo usuário para ler a biblioteca da Família Steam.
/// O token dá acesso à conta enquanto vale (cerca de um dia), então é tratado como senha: nunca é salvo, nunca vai
/// para log, e <see cref="ToString"/> não o mostra.
/// </summary>
public sealed partial record SteamAccessToken(string Value, ulong? SteamId, DateTimeOffset? ExpiresUtc)
{
    /// <summary>Página da loja que mostra o token de quem está logado no navegador.</summary>
    public const string SourcePage = "https://store.steampowered.com/pointssummary/ajaxgetasyncconfig";

    public override string ToString() => $"SteamAccessToken(conta {SteamId?.ToString() ?? "?"}, expira {ExpiresUtc:u})";

    /// <summary>
    /// Aceita a página inteira copiada (JSON com "webapi_token") ou só o token. Retorna null se não reconhecer.
    /// Lê a conta ("sub") e a validade ("exp") do próprio token, sem conferir assinatura: serve só para avisar cedo
    /// de token de outra conta ou vencido. Quem decide se vale é a Steam.
    /// </summary>
    public static SteamAccessToken? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();

        var token = text.Contains("webapi_token", StringComparison.Ordinal) ? FromPage(text) : text;
        if (token is null || !JwtShape().IsMatch(token)) return null;

        var (steamId, expires) = ReadClaims(token);
        return new SteamAccessToken(token, steamId, expires);
    }

    private static string? FromPage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return Find(doc.RootElement);
        }
        catch (JsonException)
        {
            // Página copiada com algo a mais em volta: procura o campo no texto.
            var m = PageField().Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    private static string? Find(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "webapi_token" && p.Value.ValueKind == JsonValueKind.String)
                        return p.Value.GetString();
                    if (Find(p.Value) is { } inner) return inner;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (Find(item) is { } inner) return inner;
                return null;
            default:
                return null;
        }
    }

    private static (ulong? SteamId, DateTimeOffset? Expires) ReadClaims(string token)
    {
        try
        {
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var root = doc.RootElement;

            ulong? steamId = null;
            if (root.TryGetProperty("sub", out var sub))
            {
                if (sub.ValueKind == JsonValueKind.String && ulong.TryParse(sub.GetString(), out var s)) steamId = s;
                else if (sub.ValueKind == JsonValueKind.Number && sub.TryGetUInt64(out var n)) steamId = n;
            }

            DateTimeOffset? expires = null;
            if (root.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var seconds) && seconds > 0)
                expires = DateTimeOffset.FromUnixTimeSeconds(seconds);

            return (steamId, expires);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or IndexOutOfRangeException)
        {
            return (null, null);
        }
    }

    // Formato JWT: três partes base64url separadas por ponto.
    [GeneratedRegex(@"^[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+$")]
    private static partial Regex JwtShape();

    [GeneratedRegex("\"webapi_token\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex PageField();
}
