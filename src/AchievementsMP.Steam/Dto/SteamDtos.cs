using System.Text.Json.Serialization;

namespace AchievementsMP.Steam.Dto;

// Formato bruto das respostas JSON. Campos opcionais porque a Steam omite campos em vez de mandar null.

internal sealed class OwnedGamesEnvelope { [JsonPropertyName("response")] public OwnedGamesResponse? Response { get; set; } }

internal sealed class OwnedGamesResponse
{
    [JsonPropertyName("game_count")] public int? GameCount { get; set; }
    [JsonPropertyName("games")] public List<OwnedGameDto>? Games { get; set; }
}

internal sealed class OwnedGameDto
{
    [JsonPropertyName("appid")] public long AppId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("playtime_forever")] public int PlaytimeForever { get; set; }
    [JsonPropertyName("img_icon_url")] public string? ImgIconUrl { get; set; }
    [JsonPropertyName("rtime_last_played")] public long? RtimeLastPlayed { get; set; }
    [JsonPropertyName("has_community_visible_stats")] public bool? HasCommunityVisibleStats { get; set; }
}

internal sealed class SchemaEnvelope { [JsonPropertyName("game")] public SchemaGame? Game { get; set; } }

internal sealed class SchemaGame
{
    [JsonPropertyName("gameName")] public string? GameName { get; set; }
    [JsonPropertyName("availableGameStats")] public SchemaStats? AvailableGameStats { get; set; }
}

internal sealed class SchemaStats { [JsonPropertyName("achievements")] public List<SchemaAchievementDto>? Achievements { get; set; } }

internal sealed class SchemaAchievementDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("hidden")] public int Hidden { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("icongray")] public string? IconGray { get; set; }
}

internal sealed class PlayerStatsEnvelope { [JsonPropertyName("playerstats")] public PlayerStats? PlayerStats { get; set; } }

internal sealed class PlayerStats
{
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("achievements")] public List<PlayerAchievementDto>? Achievements { get; set; }
}

internal sealed class PlayerAchievementDto
{
    [JsonPropertyName("apiname")] public string? ApiName { get; set; }
    [JsonPropertyName("achieved")] public int Achieved { get; set; }
    [JsonPropertyName("unlocktime")] public long UnlockTime { get; set; }
}

internal sealed class GlobalPercentagesEnvelope { [JsonPropertyName("achievementpercentages")] public GlobalPercentages? AchievementPercentages { get; set; } }

internal sealed class GlobalPercentages { [JsonPropertyName("achievements")] public List<GlobalPercentDto>? Achievements { get; set; } }

internal sealed class GlobalPercentDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    // Já foi número e string em épocas diferentes; aceitamos os dois.
    [JsonPropertyName("percent")] public double Percent { get; set; }
}

internal sealed class VanityEnvelope { [JsonPropertyName("response")] public VanityResponse? Response { get; set; } }

internal sealed class VanityResponse
{
    [JsonPropertyName("success")] public int Success { get; set; }
    [JsonPropertyName("steamid")] public string? SteamId { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal sealed class PlayerSummariesEnvelope { [JsonPropertyName("response")] public PlayerSummariesResponse? Response { get; set; } }

internal sealed class PlayerSummariesResponse { [JsonPropertyName("players")] public List<PlayerSummaryDto>? Players { get; set; } }

internal sealed class PlayerSummaryDto
{
    [JsonPropertyName("steamid")] public string? SteamId { get; set; }
    [JsonPropertyName("personaname")] public string? PersonaName { get; set; }
    [JsonPropertyName("avatarfull")] public string? AvatarFull { get; set; }
    /// <summary>3 = público; 1 = privado ou só amigos (a API não distingue para terceiros).</summary>
    [JsonPropertyName("communityvisibilitystate")] public int CommunityVisibilityState { get; set; }
}
