using AchievementsMP.Core.Planning;

namespace AchievementsMP.Tests.Planning;

public class GuideLinkTests
{
    [Theory]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=123", "https://steamcommunity.com/sharedfiles/filedetails/?id=123")]
    [InlineData("  http://example.com/guia  ", "http://example.com/guia")]
    [InlineData("www.trueachievements.com/game/Hades", "https://www.trueachievements.com/game/Hades")]
    public void Accepts_web_links(string input, string expected)
    {
        Assert.True(GuideLink.TryParse(input, out var url));
        Assert.Equal(expected, url.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("steam://run/620")]
    [InlineData("ftp://example.com/x")]
    [InlineData("https://")]
    [InlineData("guia sem link")]
    [InlineData("localhost")]
    public void Rejects_anything_that_is_not_a_web_page(string input)
    {
        Assert.False(GuideLink.TryParse(input, out _));
    }

    [Fact]
    public void Display_name_prefers_title_then_site()
    {
        var url = new Uri("https://www.example.com/a/b");
        Assert.Equal("Guia 100%", GuideLink.DisplayName(url, " Guia 100% "));
        Assert.Equal("example.com", GuideLink.DisplayName(url, null));
    }
}
