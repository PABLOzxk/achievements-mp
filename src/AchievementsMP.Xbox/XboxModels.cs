namespace AchievementsMP.Xbox;

/// <summary>Quem está conectado e o token para falar com os serviços do Xbox. O token vale algumas horas.</summary>
public sealed record XboxIdentity(string Xuid, string Gamertag, string UserHash, string Token, DateTimeOffset? NotAfterUtc)
{
    public string AuthorizationHeader => $"XBL3.0 x={UserHash};{Token}";

    // O token nunca vai para logs ou mensagens.
    public override string ToString() => $"XboxIdentity {{ Xuid = {Xuid}, Gamertag = {Gamertag} }}";
}

/// <summary>Um jogo do histórico do Xbox, com o resumo de conquistas.</summary>
public sealed record XboxTitle(
    string TitleId,
    string Name,
    string? ImageUrl,
    DateTimeOffset? LastPlayedUtc,
    int CurrentAchievements,
    int TotalAchievements,
    int CurrentGamerscore,
    int TotalGamerscore,
    bool IsXbox360);

public sealed record XboxAchievement(
    string Id,
    string Name,
    string? Description,
    bool IsSecret,
    string? IconUrl,
    bool IsUnlocked,
    DateTimeOffset? UnlockedUtc,
    double? RarityPercent,
    int Gamerscore);

internal sealed record XboxAchievementPage(IReadOnlyList<XboxAchievement> Achievements, string? ContinuationToken);

internal sealed record XboxUserToken(string Token, string UserHash);
