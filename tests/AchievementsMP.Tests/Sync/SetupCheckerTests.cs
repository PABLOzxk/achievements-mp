using AchievementsMP.Core.Domain;
using AchievementsMP.Steam;
using AchievementsMP.Sync;

namespace AchievementsMP.Tests.Sync;

public class SetupCheckerTests
{
    private const ulong Id = 76561197960435530UL;

    private static FakeSteam SteamWithLibrary()
    {
        var steam = new FakeSteam();
        steam.Vanity["gabe"] = Id;
        steam.AddGame(620, "Portal 2", 600, ("a", true));
        return steam;
    }

    [Fact]
    public async Task Everything_public_and_valid_is_ok_with_the_game_count()
    {
        var check = await new SetupChecker(SteamWithLibrary()).CheckAsync("https://steamcommunity.com/id/gabe/", hasKey: true);

        Assert.True(check.AllOk);
        Assert.Equal(Id, check.SteamId);
        Assert.Equal(1, check.GameCount);
        Assert.Null(check.Notice);
    }

    [Fact]
    public async Task Missing_or_unreadable_inputs_are_flagged_without_calling_steam()
    {
        var steam = SteamWithLibrary();
        var checker = new SetupChecker(steam);

        var nothing = await checker.CheckAsync("", hasKey: false);
        Assert.Equal((CheckState.Problem, CheckState.Problem), (nothing.Profile, nothing.Key));

        var noKey = await checker.CheckAsync("gabe", hasKey: false);
        Assert.Equal((CheckState.NotChecked, CheckState.Problem), (noKey.Profile, noKey.Key));

        var garbage = await checker.CheckAsync("isto não é um perfil!", hasKey: true);
        Assert.Equal(CheckState.Problem, garbage.Profile);

        Assert.Empty(steam.Calls);
    }

    [Fact]
    public async Task Rejected_key_blames_the_key_not_the_profile()
    {
        var steam = SteamWithLibrary();
        steam.Fail("vanity", SteamErrorKind.InvalidApiKey);

        var check = await new SetupChecker(steam).CheckAsync("gabe", hasKey: true);

        Assert.Equal((CheckState.NotChecked, CheckState.Problem), (check.Profile, check.Key));
    }

    [Fact]
    public async Task Unknown_profile_blames_the_profile()
    {
        var check = await new SetupChecker(SteamWithLibrary()).CheckAsync("ninguem", hasKey: true);

        Assert.Equal(CheckState.Problem, check.Profile);
        Assert.NotEqual(CheckState.Problem, check.Key);
    }

    [Fact]
    public async Task Private_profile_and_private_game_details_are_privacy_problems()
    {
        var privateProfile = SteamWithLibrary();
        privateProfile.ProfilePublic = false;
        var a = await new SetupChecker(privateProfile).CheckAsync(Id.ToString(), hasKey: true);
        Assert.Equal((CheckState.Ok, CheckState.Ok, CheckState.Problem), (a.Profile, a.Key, a.Privacy));
        Assert.Contains("perfil está privado", a.PrivacyMessage);

        var hiddenGames = SteamWithLibrary();
        hiddenGames.Fail("owned", SteamErrorKind.PrivateProfile);
        var b = await new SetupChecker(hiddenGames).CheckAsync(Id.ToString(), hasKey: true);
        Assert.Equal((CheckState.Ok, CheckState.Ok, CheckState.Problem), (b.Profile, b.Key, b.Privacy));
        Assert.Contains("Detalhes do jogo", b.PrivacyMessage);
    }

    [Fact]
    public async Task Network_failure_proves_nothing_wrong()
    {
        var steam = SteamWithLibrary();
        steam.Fail("summary", SteamErrorKind.Network);

        var check = await new SetupChecker(steam).CheckAsync(Id.ToString(), hasKey: true);

        Assert.Equal((CheckState.NotChecked, CheckState.NotChecked, CheckState.NotChecked), (check.Profile, check.Key, check.Privacy));
        Assert.Contains("internet", check.Notice);
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789abcdef", true)]
    [InlineData("  0123456789ABCDEF0123456789ABCDEF  ", true)]
    [InlineData("0123456789ABCDEF0123456789ABCDE", false)]
    [InlineData("chave", false)]
    [InlineData(null, false)]
    public void Api_key_shape_is_checked_only_to_warn(string? key, bool expected) =>
        Assert.Equal(expected, SteamIdentity.LooksLikeApiKey(key));
}
