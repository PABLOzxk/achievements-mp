using System.Text;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;
using AchievementsMP.Sync;

namespace AchievementsMP.Tests.Sync;

/// <summary>Jogos emprestados da Família Steam: detecção automática na sincronização e importação única com token.</summary>
public sealed class FamilySharingTests : IAsyncLifetime
{
    private const ulong Me = 76561197960435530, Brother = 76561197960287930;
    private const long Portal = 620, Hades = 1145360, Celeste = 504230, NeverPlayed = 999;
    private const ulong Group = 123456789;

    private TempDatabase _db = null!;
    private readonly FakeSteam _steam = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
    private SettingsRepository _settings = null!;
    private SteamCatalogRepository _catalog = null!;
    private SyncRunRepository _runs = null!;

    public async Task InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync();
        _settings = new SettingsRepository(_db.Factory, new FakeProtector());
        _catalog = new SteamCatalogRepository(_db.Factory);
        _runs = new SyncRunRepository(_db.Factory);
        await _settings.SaveAsync(new AppSettings { SteamProfile = Me.ToString(), SteamId64 = Me });

        _steam.AddGame(Portal, "Portal 2", 600, ("A", true), ("B", false));
        // Hades é do irmão: tem conquistas, mas não está na biblioteca própria.
        _steam.Schemas[Hades] = [new(Hades, "H1", "H1", null, false, null, null), new(Hades, "H2", "H2", null, false, null, null)];
        _steam.Player[Hades] = [new(Hades, "H1", UnlockState.Unlocked, null), new(Hades, "H2", UnlockState.Locked, null)];
        _steam.Global[Hades] = new() { ["H1"] = 50, ["H2"] = 5 };
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private SyncService Sync() => new(_steam, _settings, _catalog, _runs, _clock,
        new SyncOptions { MinRequestSpacing = TimeSpan.Zero }, (d, _) => { _clock.Advance(d); return Task.CompletedTask; });

    private FamilyImporter Importer() => new(_steam, _settings, _catalog, _clock);

    private async Task<LibraryGame?> InLibrary(long appId) => (await _catalog.GetLibraryAsync()).SingleOrDefault(g => g.AppId == appId);

    private static Game Recent(long appId, string name, int minutes) => new(appId, name, "icon", minutes, null, null);

    private SharedFamilyApp Shared(long appId, string name, int minutes, params ulong[] owners) =>
        new(appId, name, "icon", owners, minutes, minutes > 0 ? _clock.GetUtcNow().AddDays(-30) : null);

    /// <summary>Token no formato da Steam (JWT), com conta e validade escolhidas. A assinatura é falsa: o app não confere.</summary>
    private static string Token(ulong steamId, DateTimeOffset expires)
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("""{"typ":"JWT","alg":"EdDSA"}""")}.{B64($$"""{"iss":"r:0","sub":"{{steamId}}","aud":["web:store"],"exp":{{expires.ToUnixTimeSeconds()}}}""")}.c2lnbmF0dXJh";
    }

    private string ValidToken => Token(Me, _clock.GetUtcNow().AddHours(20));

    // ----- detecção automática -----

    [Fact]
    public async Task Recently_played_game_that_is_not_owned_enters_the_library_as_family_with_achievements()
    {
        _steam.Recent = [Recent(Portal, "Portal 2", 600), Recent(Hades, "Hades", 95)];

        var result = await Sync().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        var hades = await InLibrary(Hades);
        Assert.NotNull(hades);
        Assert.True(hades.IsFamily);
        Assert.Equal(95, hades.PlaytimeMinutes);
        Assert.Equal(new(2, 1, 1, 0), hades.Progress);
        Assert.False((await InLibrary(Portal))!.IsFamily);
    }

    [Fact]
    public async Task Family_game_stays_after_it_leaves_recently_played()
    {
        _steam.Recent = [Recent(Hades, "Hades", 95)];
        await Sync().SyncAsync();

        _steam.Recent = [];
        _clock.Advance(TimeSpan.FromDays(20));
        await Sync().SyncAsync();

        // As conquistas são da conta: o jogo não some só porque saiu da janela de 2 semanas.
        Assert.True((await InLibrary(Hades))!.IsFamily);
    }

    [Fact]
    public async Task Buying_a_family_game_turns_it_into_an_owned_game()
    {
        _steam.Recent = [Recent(Hades, "Hades", 95)];
        await Sync().SyncAsync();

        _steam.Library.Add(new Game(Hades, "Hades", "icon", 120, null, true));
        _clock.Advance(TimeSpan.FromHours(7));
        await Sync().SyncAsync();

        var hades = await InLibrary(Hades);
        Assert.False(hades!.IsFamily);
        Assert.True(hades.IsOwned);
        Assert.Equal(120, hades.PlaytimeMinutes);
    }

    [Fact]
    public async Task Owned_game_in_recently_played_is_never_downgraded_to_family()
    {
        _steam.Recent = [Recent(Portal, "Portal 2", 600)];
        await Sync().SyncAsync();
        await _catalog.UpsertFamilyGamesAsync([Recent(Portal, "Portal 2 (outro nome)", 1)], _clock.GetUtcNow());

        var portal = await InLibrary(Portal);
        Assert.False(portal!.IsFamily);
        Assert.Equal("Portal 2", portal.Name);
        Assert.Equal(600, portal.PlaytimeMinutes);
    }

    [Fact]
    public async Task Complete_library_pass_does_not_hide_family_games()
    {
        _steam.Recent = [Recent(Hades, "Hades", 95)];
        await Sync().SyncAsync();

        // Sincronização seguinte marca como fora da biblioteca o que sumiu do GetOwnedGames; Hades nunca esteve lá.
        _clock.Advance(TimeSpan.FromHours(7));
        await _catalog.UpsertLibraryAsync(_steam.Library, _clock.GetUtcNow(), isCompleteLibrary: true);

        Assert.NotNull(await InLibrary(Hades));
    }

    [Fact]
    public async Task Failure_reading_recently_played_does_not_fail_the_sync()
    {
        _steam.Fail("recent", SteamErrorKind.InvalidResponse);

        var result = await Sync().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.NotNull(await InLibrary(Portal));
    }

    [Fact]
    public async Task Changing_account_drops_family_games_of_the_previous_account()
    {
        _steam.Recent = [Recent(Hades, "Hades", 95)];
        await Sync().SyncAsync();

        await _catalog.SetOwnerSteamIdAsync(Brother);

        Assert.Null(await InLibrary(Hades));
    }

    // ----- importação única com token -----

    [Fact]
    public async Task Import_adds_only_borrowed_games_you_played_and_uses_the_token_only_with_steam()
    {
        await Sync().SyncAsync();
        _steam.FamilyGroupId = Group;
        _steam.SharedApps =
        [
            Shared(Portal, "Portal 2", 600, Me),             // seu: fica como está
            Shared(Hades, "Hades", 95, Brother),             // emprestado e jogado: entra
            Shared(Celeste, "Celeste", 30, Brother, Me),     // os dois têm: é seu
            Shared(NeverPlayed, "Nunca jogado", 0, Brother), // emprestado, nunca aberto: fica de fora
        ];

        var result = await Importer().ImportAsync(ValidToken);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.Added);
        Assert.DoesNotContain(ValidToken, result.Message);
        var hades = await InLibrary(Hades);
        Assert.True(hades!.IsFamily);
        Assert.Equal(_clock.GetUtcNow().AddDays(-30), hades.LastPlayedUtc);
        Assert.Null(await InLibrary(NeverPlayed));
        Assert.Null(await InLibrary(Celeste));
        Assert.All(_steam.TokensSeen, t => Assert.Equal(ValidToken, t));
        Assert.Contains($"shared:{Group}", _steam.Calls);
    }

    [Fact]
    public async Task Imported_games_get_their_achievements_on_the_next_sync()
    {
        await Sync().SyncAsync();
        _steam.FamilyGroupId = Group;
        _steam.SharedApps = [Shared(Hades, "Hades", 95, Brother)];
        await Importer().ImportAsync(ValidToken);

        await Sync().SyncAsync();

        Assert.Equal(new(2, 1, 1, 0), (await InLibrary(Hades))!.Progress);
    }

    [Fact]
    public async Task Import_accepts_the_whole_page_pasted()
    {
        await Sync().SyncAsync();
        _steam.FamilyGroupId = Group;
        _steam.SharedApps = [Shared(Hades, "Hades", 95, Brother)];
        var page = $$$"""{"success":1,"data":{"webapi_token":"{{{ValidToken}}}","other":"x"}}""";

        var result = await Importer().ImportAsync(page);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(ValidToken, _steam.TokensSeen[0]);
    }

    [Fact]
    public async Task Token_from_another_account_is_refused_before_calling_steam()
    {
        await Sync().SyncAsync();
        _steam.Calls.Clear();

        var result = await Importer().ImportAsync(Token(Brother, _clock.GetUtcNow().AddHours(20)));

        Assert.False(result.Ok);
        Assert.Contains("outra conta", result.Message);
        Assert.Empty(_steam.Calls);
    }

    [Fact]
    public async Task Expired_token_is_refused_before_calling_steam()
    {
        await Sync().SyncAsync();
        _steam.Calls.Clear();

        var result = await Importer().ImportAsync(Token(Me, _clock.GetUtcNow().AddMinutes(-1)));

        Assert.False(result.Ok);
        Assert.Contains("venceu", result.Message);
        Assert.Empty(_steam.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("minha senha")]
    [InlineData("{\"success\":1,\"data\":{}}")]
    public async Task Garbage_is_refused(string pasted)
    {
        await Sync().SyncAsync();

        var result = await Importer().ImportAsync(pasted);

        Assert.False(result.Ok);
        Assert.Contains("Não reconheci", result.Message);
    }

    [Fact]
    public async Task Import_requires_a_first_sync_of_the_same_account()
    {
        var result = await Importer().ImportAsync(ValidToken);

        Assert.False(result.Ok);
        Assert.Contains("Sincronize", result.Message);
    }

    [Fact]
    public async Task Not_in_a_family_is_reported()
    {
        await Sync().SyncAsync();
        _steam.FamilyGroupId = null;

        var result = await Importer().ImportAsync(ValidToken);

        Assert.False(result.Ok);
        Assert.Contains("não está em nenhuma Família", result.Message);
    }

    [Fact]
    public async Task Token_refused_by_steam_is_explained()
    {
        await Sync().SyncAsync();
        _steam.Fail("family", SteamErrorKind.InvalidAccessToken);

        var result = await Importer().ImportAsync(ValidToken);

        Assert.False(result.Ok);
        Assert.Contains("recusou o token", result.Message);
    }

    [Fact]
    public async Task Family_with_nothing_you_played_adds_nothing()
    {
        await Sync().SyncAsync();
        _steam.FamilyGroupId = Group;
        _steam.SharedApps = [Shared(NeverPlayed, "Nunca jogado", 0, Brother)];

        var result = await Importer().ImportAsync(ValidToken);

        Assert.True(result.Ok);
        Assert.Equal(0, result.Found);
        Assert.Null(await InLibrary(NeverPlayed));
    }

    [Fact]
    public void Token_never_shows_up_in_ToString()
    {
        var token = SteamAccessToken.Parse(ValidToken)!;

        Assert.Equal(Me, token.SteamId);
        Assert.DoesNotContain(ValidToken, token.ToString());
        Assert.DoesNotContain(token.Value.Split('.')[1], token.ToString());
    }
}
