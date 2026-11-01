using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Progress;

namespace AchievementsMP.Core.Library;

/// <summary>Um jogo da biblioteca com progresso e viabilidade calculados. <see cref="AppId"/> é o número interno do jogo
/// (na Steam, o próprio AppId; ver <see cref="Platforms"/>).</summary>
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
    CompletionOutlook Outlook,
    Platform Platform = Platform.Steam,
    string? ImageUrl = null)
{
    public string? IconUrl => Platform == Platform.Steam
        ? IconHash is null ? null : $"https://media.steampowered.com/steamcommunity/public/images/apps/{AppId}/{IconHash}.jpg"
        : ImageUrl;
}
