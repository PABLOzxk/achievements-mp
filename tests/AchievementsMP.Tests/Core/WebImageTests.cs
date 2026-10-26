using AchievementsMP.Core.Domain;

namespace AchievementsMP.Tests.Core;

public class WebImageTests
{
    [Theory]
    [InlineData("https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/620/abc.jpg")]
    [InlineData("https://media.steampowered.com/steamcommunity/public/images/apps/620/abc.jpg")]
    public void Steam_icons_are_shown(string url) => Assert.Equal(url, WebImage.TryParse(url)?.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://example.com/icone.jpg")]
    [InlineData(@"\\servidor\compartilhamento\icone.jpg")]
    [InlineData("file://servidor/compartilhamento/icone.jpg")]
    [InlineData("file:///C:/Users/voce/Documents/foto.jpg")]
    [InlineData(@"C:\Users\voce\Documents\foto.jpg")]
    [InlineData("icone.jpg")]
    [InlineData("pack://application:,,,/Assets/app.ico")]
    public void Anything_else_is_not_loaded(string? url) => Assert.Null(WebImage.TryParse(url));
}
