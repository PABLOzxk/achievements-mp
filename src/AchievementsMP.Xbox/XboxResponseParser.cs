using System.Globalization;
using System.Text.Json;

namespace AchievementsMP.Xbox;

/// <summary>Lê as respostas JSON do Xbox. Campos que faltam viram null; formato que não dá para entender vira erro.</summary>
internal static class XboxResponseParser
{
    // Conquista bloqueada vem com "0001-01-01" (ou datas assim) em timeUnlocked. O Xbox 360 é de 2005.
    private static readonly DateTimeOffset EarliestRealUnlock = new(2005, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Códigos XErr do XSTS que têm explicação para o usuário.
    private const long NoXboxAccount = 2148916233;
    private const long CountryNotAllowed = 2148916235;
    private const long AdultVerificationA = 2148916236;
    private const long AdultVerificationB = 2148916237;
    private const long ChildAccount = 2148916238;

    public static XboxResult<XboxUserToken> ParseUserToken(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = Str(root, "Token");
            var uhs = FirstClaim(root, "uhs");
            return token is null || uhs is null
                ? Invalid<XboxUserToken>("O Xbox não devolveu o token do usuário.")
                : XboxResult<XboxUserToken>.Ok(new XboxUserToken(token, uhs));
        }
        catch (JsonException)
        {
            return Invalid<XboxUserToken>("Resposta inesperada do login do Xbox.");
        }
    }

    public static XboxResult<XboxIdentity> ParseXsts(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = Str(root, "Token");
            var uhs = FirstClaim(root, "uhs");
            var xuid = FirstClaim(root, "xid");
            var gamertag = FirstClaim(root, "gtg") ?? "";
            if (token is null || uhs is null || xuid is null || !IsDigits(xuid))
                return Invalid<XboxIdentity>("O Xbox não devolveu a identificação da conta.");
            return XboxResult<XboxIdentity>.Ok(new XboxIdentity(xuid, gamertag, uhs, token, Date(root, "NotAfter")));
        }
        catch (JsonException)
        {
            return Invalid<XboxIdentity>("Resposta inesperada do login do Xbox.");
        }
    }

    /// <summary>Explica uma recusa do XSTS (401 com XErr no corpo).</summary>
    public static XboxError ParseXstsError(string body)
    {
        long? code = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("XErr", out var x))
                code = x.ValueKind switch
                {
                    JsonValueKind.Number when x.TryGetInt64(out var n) => n,
                    JsonValueKind.String when long.TryParse(x.GetString(), out var s) => s,
                    _ => null,
                };
        }
        catch (JsonException)
        {
        }

        return code switch
        {
            NoXboxAccount => new(XboxErrorKind.NoXboxAccount, "Esta conta Microsoft não tem perfil Xbox. Entre uma vez em xbox.com para criar o perfil e tente de novo."),
            CountryNotAllowed => new(XboxErrorKind.AccountRestricted, "O Xbox Live não está disponível no país desta conta."),
            AdultVerificationA or AdultVerificationB => new(XboxErrorKind.AccountRestricted, "O Xbox pede verificação de idade nesta conta. Faça a verificação em xbox.com e tente de novo."),
            ChildAccount => new(XboxErrorKind.AccountRestricted, "Conta de criança: um adulto da família precisa liberar o acesso em account.microsoft.com/family."),
            { } other => new(XboxErrorKind.Unauthorized, $"O Xbox recusou o login (código {other})."),
            null => new(XboxErrorKind.Unauthorized, "O Xbox recusou o login."),
        };
    }

    public static XboxResult<IReadOnlyList<XboxTitle>> ParseTitleHistory(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("titles", out var titles) || titles.ValueKind != JsonValueKind.Array)
                return Invalid<IReadOnlyList<XboxTitle>>("Resposta inesperada do Xbox ao ler seus jogos.");

            var list = new List<XboxTitle>();
            foreach (var t in titles.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                var titleId = Str(t, "titleId");
                var name = Str(t, "name");
                if (titleId is null || !IsDigits(titleId) || string.IsNullOrWhiteSpace(name)) continue;
                // O histórico também lista apps (Netflix, YouTube...). Só jogos interessam.
                if (Str(t, "type") is { } type && !type.Equals("Game", StringComparison.OrdinalIgnoreCase)) continue;

                var hasAchievement = t.TryGetProperty("achievement", out var a) && a.ValueKind == JsonValueKind.Object;
                var devices = t.TryGetProperty("devices", out var d) && d.ValueKind == JsonValueKind.Array
                    ? d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                    : [];
                // sourceVersion 1 = conquistas no modelo do Xbox 360 (inclusive jogos de 360 jogados no One/Series).
                var isXbox360 = hasAchievement && Int(a, "sourceVersion") == 1
                    || devices.Count > 0 && devices.All(x => x.Equals("Xbox360", StringComparison.OrdinalIgnoreCase));

                DateTimeOffset? lastPlayed = t.TryGetProperty("titleHistory", out var h) && h.ValueKind == JsonValueKind.Object ? RealDate(h, "lastTimePlayed") : null;

                list.Add(new XboxTitle(
                    titleId, name.Trim(), Https(Str(t, "displayImage")), lastPlayed,
                    hasAchievement ? Int(a, "currentAchievements") ?? 0 : 0,
                    hasAchievement ? Int(a, "totalAchievements") ?? 0 : 0,
                    hasAchievement ? Int(a, "currentGamerscore") ?? 0 : 0,
                    hasAchievement ? Int(a, "totalGamerscore") ?? 0 : 0,
                    isXbox360));
            }
            return XboxResult<IReadOnlyList<XboxTitle>>.Ok(list);
        }
        catch (JsonException)
        {
            return Invalid<IReadOnlyList<XboxTitle>>("Resposta inesperada do Xbox ao ler seus jogos.");
        }
    }

    public static XboxResult<XboxAchievementPage> ParseAchievements(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("achievements", out var items) || items.ValueKind != JsonValueKind.Array)
                return Invalid<XboxAchievementPage>("Resposta inesperada do Xbox ao ler as conquistas.");

            var list = new List<XboxAchievement>();
            foreach (var a in items.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var id = Str(a, "id");
                var name = Str(a, "name");
                if (string.IsNullOrWhiteSpace(id) || name is null) continue;

                var unlocked = IsAchieved(a);
                DateTimeOffset? when = unlocked && a.TryGetProperty("progression", out var p) && p.ValueKind == JsonValueKind.Object
                    ? RealDate(p, "timeUnlocked")
                    : null;
                double? rarity = a.TryGetProperty("rarity", out var r) && r.ValueKind == JsonValueKind.Object
                    && r.TryGetProperty("currentPercentage", out var pct) && pct.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(pct.GetDouble(), 0, 100)
                    : null;

                var description = NonEmpty(Str(a, "description")) ?? NonEmpty(Str(a, "lockedDescription"));
                list.Add(new XboxAchievement(id, name, description, Bool(a, "isSecret"), Icon(a), unlocked, when, rarity, Gamerscore(a)));
            }

            string? next = root.TryGetProperty("pagingInfo", out var paging) && paging.ValueKind == JsonValueKind.Object ? NonEmpty(Str(paging, "continuationToken")) : null;
            return XboxResult<XboxAchievementPage>.Ok(new XboxAchievementPage(list, next));
        }
        catch (JsonException)
        {
            return Invalid<XboxAchievementPage>("Resposta inesperada do Xbox ao ler as conquistas.");
        }
    }

    private static bool IsAchieved(JsonElement a)
    {
        if (!a.TryGetProperty("progressState", out var s)) return false;
        return s.ValueKind switch
        {
            JsonValueKind.String => string.Equals(s.GetString(), "Achieved", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => s.TryGetInt32(out var n) && n == 1,
            _ => false,
        };
    }

    private static string? Icon(JsonElement a)
    {
        if (!a.TryGetProperty("mediaAssets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string? any = null;
        foreach (var m in assets.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object || Https(Str(m, "url")) is not { } url) continue;
            if (string.Equals(Str(m, "type"), "Icon", StringComparison.OrdinalIgnoreCase)) return url;
            any ??= url;
        }
        return any;
    }

    private static int Gamerscore(JsonElement a)
    {
        if (!a.TryGetProperty("rewards", out var rewards) || rewards.ValueKind != JsonValueKind.Array) return 0;
        foreach (var r in rewards.EnumerateArray())
        {
            if (r.ValueKind == JsonValueKind.Object && string.Equals(Str(r, "type"), "Gamerscore", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(Str(r, "value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return 0;
    }

    private static string? FirstClaim(JsonElement root, string claim) =>
        root.TryGetProperty("DisplayClaims", out var dc) && dc.ValueKind == JsonValueKind.Object
        && dc.TryGetProperty("xui", out var xui) && xui.ValueKind == JsonValueKind.Array && xui.GetArrayLength() > 0
        && xui[0].ValueKind == JsonValueKind.Object
            ? Str(xui[0], claim)
            : null;

    /// <summary>Imagens do Xbox às vezes vêm em http; o servidor também atende em https, e o app só exibe https.</summary>
    internal static string? Https(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeHttps) return uri.AbsoluteUri;
        if (uri.Scheme != Uri.UriSchemeHttp) return null;
        return new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.AbsoluteUri;
    }

    internal static bool IsDigits(string s) => s.Length is > 0 and <= 20 && s.All(char.IsAsciiDigit);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    private static DateTimeOffset? RealDate(JsonElement e, string name) => Date(e, name) is { } d && d >= EarliestRealUnlock ? d : null;

    private static XboxResult<T> Invalid<T>(string message) => XboxResult<T>.Fail(XboxErrorKind.InvalidResponse, message);
}
