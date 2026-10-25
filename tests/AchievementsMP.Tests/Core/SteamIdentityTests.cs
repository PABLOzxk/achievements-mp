using AchievementsMP.Core.Domain;

namespace AchievementsMP.Tests.Core;

public class SteamIdentityTests
{
    [Theory]
    [InlineData("76561197960435530")]
    [InlineData("https://steamcommunity.com/profiles/76561197960435530")]
    [InlineData("https://steamcommunity.com/profiles/76561197960435530/")]
    [InlineData("  steamcommunity.com/profiles/76561197960435530 ")]
    public void Parses_steam_id_forms(string input)
    {
        var parsed = Assert.IsType<SteamProfileInput.SteamId>(SteamIdentity.Parse(input));
        Assert.Equal(76561197960435530UL, parsed.Value);
    }

    [Theory]
    [InlineData("https://steamcommunity.com/id/gabelogannewell", "gabelogannewell")]
    [InlineData("gabelogannewell", "gabelogannewell")]
    public void Parses_vanity_forms(string input, string expected)
    {
        var parsed = Assert.IsType<SteamProfileInput.Vanity>(SteamIdentity.Parse(input));
        Assert.Equal(expected, parsed.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]               // só dígitos mas não é SteamID64
    [InlineData("https://example.com/profiles/76561197960435530")]
    [InlineData("nome com espaço")]
    public void Rejects_invalid_input(string? input) => Assert.Null(SteamIdentity.Parse(input));
}
