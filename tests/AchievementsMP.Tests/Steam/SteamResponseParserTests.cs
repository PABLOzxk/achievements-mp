using AchievementsMP.Core.Domain;
using AchievementsMP.Steam;

namespace AchievementsMP.Tests.Steam;

public class SteamResponseParserTests
{
    [Fact]
    public void Owned_games_are_parsed()
    {
        var r = SteamResponseParser.ParseOwnedGames(Fixture.Read("owned_games.json"));

        Assert.True(r.IsSuccess);
        Assert.Equal(2, r.Value!.GameCount);
        var portal = r.Value.Games.Single(g => g.AppId == 620);
        Assert.Equal("Portal 2", portal.Name);
        Assert.Equal(1234, portal.PlaytimeMinutes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), portal.LastPlayedUtc);
        var tf2 = r.Value.Games.Single(g => g.AppId == 440);
        Assert.Null(tf2.IconHash);
        Assert.Null(tf2.LastPlayedUtc);
        Assert.Null(tf2.HasCommunityStats);
    }

    [Fact]
    public void Empty_owned_games_response_means_private_profile_not_empty_library()
    {
        var r = SteamResponseParser.ParseOwnedGames(Fixture.Read("owned_games_private.json"));
        Assert.Equal(SteamErrorKind.PrivateProfile, r.Error!.Kind);
        Assert.False(r.Error.IsRetryable);
    }

    [Fact]
    public void Schema_keeps_hidden_achievements_without_description()
    {
        var r = SteamResponseParser.ParseSchema(620, Fixture.Read("schema.json"));

        Assert.True(r.IsSuccess);
        Assert.Equal(2, r.Value!.Count);
        var secret = r.Value.Single(a => a.ApiName == "ACH_SECRET");
        Assert.True(secret.Hidden);
        Assert.Null(secret.Description);
        Assert.All(r.Value, a => Assert.Equal(620, a.AppId));
    }

    [Fact]
    public void Game_without_achievements_yields_empty_schema()
    {
        var r = SteamResponseParser.ParseSchema(10, Fixture.Read("schema_empty.json"));
        Assert.True(r.IsSuccess);
        Assert.Empty(r.Value!);
    }

    [Fact]
    public void Player_achievements_are_parsed()
    {
        var r = SteamResponseParser.ParsePlayerAchievements(620, Fixture.Read("player_achievements.json"));

        Assert.True(r.IsSuccess);
        var done = r.Value!.Single(a => a.ApiName == "ACH_SURVIVE_CONTAINER_RIDE");
        Assert.Equal(UnlockState.Unlocked, done.State);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1600000000), done.UnlockedAtUtc);
        var pending = r.Value!.Single(a => a.ApiName == "ACH_SECRET");
        Assert.Equal(UnlockState.Locked, pending.State);
        Assert.Null(pending.UnlockedAtUtc);
    }

    [Theory]
    [InlineData("player_achievements_private.json", SteamErrorKind.PrivateProfile)]
    [InlineData("player_achievements_nostats.json", SteamErrorKind.NoStats)]
    public void Player_achievement_errors_are_classified(string fixture, SteamErrorKind expected)
    {
        var r = SteamResponseParser.ParsePlayerAchievements(620, Fixture.Read(fixture));
        Assert.Equal(expected, r.Error!.Kind);
    }

    [Fact]
    public void Global_percentages_accept_string_and_number()
    {
        var r = SteamResponseParser.ParseGlobalPercentages(Fixture.Read("global_percentages.json"));
        Assert.Equal(87.5, r.Value!["ACH_SURVIVE_CONTAINER_RIDE"]);
        Assert.Equal(3.2, r.Value!["ACH_SECRET"]);
    }

    [Theory]
    [InlineData("<html>Service Unavailable</html>")]
    [InlineData("")]
    public void Garbage_is_an_error_not_an_empty_result(string body)
    {
        Assert.Equal(SteamErrorKind.InvalidResponse, SteamResponseParser.ParseOwnedGames(body).Error!.Kind);
        Assert.Equal(SteamErrorKind.InvalidResponse, SteamResponseParser.ParsePlayerAchievements(1, body).Error!.Kind);
    }

    [Fact]
    public void Vanity_resolution()
    {
        Assert.Equal(76561197960287930UL, SteamResponseParser.ParseVanity("""{"response":{"steamid":"76561197960287930","success":1}}""").Value);
        Assert.Equal(SteamErrorKind.NotFound, SteamResponseParser.ParseVanity("""{"response":{"success":42,"message":"No match"}}""").Error!.Kind);
    }
}
