using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Progress;

namespace AchievementsMP.Core.Library;

/// <summary>Um jogo da biblioteca com progresso e viabilidade calculados.</summary>
public sealed record LibraryGame(
    long AppId,
    string Name,
    string? IconHash,
    int PlaytimeMinutes,
    DateTimeOffset? LastPlayedUtc,
    bool IsOwned,
    DataAvailability Availability,
    DateTimeOffset? PlayerSyncedUtc,
    string? LastError,
    GameProgress Progress,
    CompletionOutlook Outlook)
{
    public string? IconUrl => IconHash is null ? null : $"https://media.steampowered.com/steamcommunity/public/images/apps/{AppId}/{IconHash}.jpg";
}
