namespace AchievementsMP.Xbox;

/// <summary>Os serviços do Xbox que o app usa. Só leitura.</summary>
public interface IXboxLiveApi
{
    /// <summary>Troca o token do login da Microsoft pelo token do Xbox e descobre o XUID e a gamertag.</summary>
    Task<XboxResult<XboxIdentity>> AuthorizeAsync(string microsoftAccessToken, CancellationToken ct = default);

    /// <summary>Jogos que a conta já abriu (Xbox One, Series, PC e 360), com o resumo de conquistas.</summary>
    Task<XboxResult<IReadOnlyList<XboxTitle>>> GetTitleHistoryAsync(XboxIdentity identity, string language, CancellationToken ct = default);

    /// <summary>Todas as conquistas de um jogo de Xbox One, Series ou PC, com estado e raridade.</summary>
    Task<XboxResult<IReadOnlyList<XboxAchievement>>> GetAchievementsAsync(XboxIdentity identity, string titleId, string language, CancellationToken ct = default);
}
