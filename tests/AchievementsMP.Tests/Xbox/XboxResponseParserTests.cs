using AchievementsMP.Xbox;

namespace AchievementsMP.Tests.Xbox;

public class XboxResponseParserTests
{
    [Fact]
    public void Title_history_keeps_only_games_and_flags_xbox_360()
    {
        var r = XboxResponseParser.ParseTitleHistory(Fixture.Read("xbox_titlehistory.json"));

        Assert.True(r.IsSuccess);
        Assert.Equal(["Halo Infinite", "Fable II", "Minecraft Launcher"], r.Value!.Select(t => t.Name));

        var halo = r.Value![0];
        Assert.Equal(("1717113201", 2, 3, 30, 60, false), (halo.TitleId, halo.CurrentAchievements, halo.TotalAchievements, halo.CurrentGamerscore, halo.TotalGamerscore, halo.IsXbox360));
        Assert.StartsWith("https://store-images.s-microsoft.com/image/apps.21536", halo.ImageUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 21, 14, 5, 333, TimeSpan.Zero), halo.LastPlayedUtc);

        Assert.True(r.Value[1].IsXbox360);
        Assert.Equal(0, r.Value[2].TotalAchievements);
        Assert.Null(r.Value[2].ImageUrl);
    }

    [Fact]
    public void Achievements_read_state_date_rarity_icon_and_gamerscore()
    {
        var r = XboxResponseParser.ParseAchievements(Fixture.Read("xbox_achievements.json"));

        Assert.True(r.IsSuccess);
        Assert.Null(r.Value!.ContinuationToken);
        var list = r.Value.Achievements;
        Assert.Equal(["1", "2", "3"], list.Select(a => a.Id));

        Assert.True(list[0].IsUnlocked);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 20, 0, 0, 123, TimeSpan.Zero), list[0].UnlockedUtc);
        Assert.Equal(61.2, list[0].RarityPercent);
        Assert.Equal(10, list[0].Gamerscore);
        Assert.Equal("https://images-eds-ssl.xboxlive.com/image?url=a1", list[0].IconUrl);

        Assert.Equal("https://images-eds.xboxlive.com/image?url=a2", list[1].IconUrl);

        var secret = list[2];
        Assert.False(secret.IsUnlocked);
        Assert.Null(secret.UnlockedUtc);
        Assert.True(secret.IsSecret);
        Assert.Null(secret.RarityPercent);
        Assert.Equal("Continue jogando para desbloquear.", secret.Description);
        Assert.Null(secret.IconUrl);
    }

    [Fact]
    public void Numeric_progress_state_is_understood()
    {
        var r = XboxResponseParser.ParseAchievements("""{"achievements":[{"id":"1","name":"A","progressState":1},{"id":"2","name":"B","progressState":3}]}""");

        Assert.Equal([true, false], r.Value!.Achievements.Select(a => a.IsUnlocked));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>")]
    [InlineData("""{"achievements":"x"}""")]
    [InlineData("[]")]
    public void Unexpected_achievement_format_is_an_error(string body) =>
        Assert.Equal(XboxErrorKind.InvalidResponse, XboxResponseParser.ParseAchievements(body).Error!.Kind);

    [Fact]
    public void Xsts_without_xuid_is_an_error() =>
        Assert.False(XboxResponseParser.ParseXsts("""{"Token":"T","DisplayClaims":{"xui":[{"uhs":"1","gtg":"x"}]}}""").IsSuccess);

    [Theory]
    [InlineData("http://store-images.s-microsoft.com/a.png", "https://store-images.s-microsoft.com/a.png")]
    [InlineData("http://example.com:8080/a.png", "https://example.com/a.png")]
    [InlineData("https://ok.example/a.png", "https://ok.example/a.png")]
    [InlineData("file:///C:/x.png", null)]
    [InlineData("\\\\servidor\\x.png", null)]
    [InlineData(null, null)]
    public void Images_are_https_only(string? input, string? expected) => Assert.Equal(expected, XboxResponseParser.Https(input));
}
