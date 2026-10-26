using AchievementsMP.Core.Profile;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data;
using AchievementsMP.Data.Backups;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;
using AchievementsMP.Sync;
using AchievementsMP.Tests.Sync;
using Dapper;

namespace AchievementsMP.Tests.Profile;

public class ProfileTests
{
    private const ulong Me = 76561197960435530;
    private const string SteamAvatar = "https://avatars.steamstatic.com/abc_full.jpg";
    private static readonly byte[] Photo = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3];

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Pablo  ", "Pablo")]
    [InlineData("Pa\nblo\t", "Pablo")]
    [InlineData("PabloNaSteam", null)] // igual ao da Steam: continua acompanhando a Steam
    [InlineData("pablonasteam", "pablonasteam")]
    public void Typed_name_is_cleaned_and_steam_name_means_no_choice(string? typed, string? expected) =>
        Assert.Equal(expected, UserProfile.NormalizeName(typed, "PabloNaSteam"));

    [Fact]
    public void Long_name_is_cut_without_breaking_an_emoji()
    {
        Assert.Equal(new string('a', UserProfile.MaxNameLength), UserProfile.NormalizeName(new string('a', 40), null));
        var withEmoji = new string('a', UserProfile.MaxNameLength - 1) + "🏆";
        Assert.Equal(new string('a', UserProfile.MaxNameLength - 1), UserProfile.NormalizeName(withEmoji, null));
    }

    [Fact]
    public void Choice_wins_over_steam()
    {
        Assert.Null(UserProfile.Empty.Name);
        Assert.Equal("Steam", new UserProfile("Steam", null, null, null).Name);
        Assert.Equal("Meu", new UserProfile("Steam", null, "Meu", null).Name);
    }

    [Fact]
    public async Task Steam_data_and_user_choices_are_saved_separately()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new ProfileRepository(db.Factory);
        Assert.Equal((null, null, null), Fields(await repo.LoadAsync()));
        Assert.Null((await repo.LoadAsync()).CustomAvatar);

        await repo.SaveChoicesAsync("Pablo", Photo);
        await repo.SaveSteamAsync("PabloNaSteam", SteamAvatar); // a atualização da Steam não apaga a escolha

        var p = await repo.LoadAsync();
        Assert.Equal(("PabloNaSteam", SteamAvatar, "Pablo"), Fields(p));
        Assert.Equal(Photo, p.CustomAvatar);

        await repo.SaveChoicesAsync(null, null); // voltar para a Steam
        p = await repo.LoadAsync();
        Assert.Equal(("PabloNaSteam", SteamAvatar, null), Fields(p));
        Assert.Null(p.CustomAvatar);
        Assert.Equal("PabloNaSteam", p.Name);
    }

    [Fact]
    public async Task Oversized_photo_is_refused_and_a_tampered_one_is_ignored()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new ProfileRepository(db.Factory);
        var huge = new byte[ProfileRepository.MaxAvatarBytes + 1];
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveChoicesAsync(null, huge));

        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO settings(key, value) VALUES ('profile_custom_avatar', @huge)", new { huge });
        Assert.Null((await repo.LoadAsync()).CustomAvatar);
    }

    [Fact]
    public async Task Chosen_photo_and_name_go_into_the_backup()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await new ProfileRepository(db.Factory).SaveChoicesAsync("Pablo", Photo);

        var backup = await new BackupService(db.Factory, db.Paths).CreateAsync(Path.Combine(db.Paths.Root, "OneDrive"), DateTimeOffset.UtcNow);

        var fromBackup = await new ProfileRepository(new SqliteConnectionFactory(backup.Path)).LoadAsync();
        Assert.Equal("Pablo", fromBackup.CustomName);
        Assert.Equal(Photo, fromBackup.CustomAvatar);
    }

    [Fact]
    public async Task Refresh_saves_current_steam_name_and_photo()
    {
        var (db, settings, repo) = await CreateAsync(new AppSettings { SteamProfile = Me.ToString(), SteamId64 = Me });
        await using var _ = db;
        var steam = new FakeSteam { PersonaName = "PabloNaSteam", AvatarUrl = SteamAvatar, ProfilePublic = false };

        Assert.True(await new ProfileRefresher(steam, settings, repo).RefreshAsync());
        Assert.Equal(("PabloNaSteam", SteamAvatar, null), Fields(await repo.LoadAsync()));

        // Nada mudou na Steam: não regrava.
        Assert.False(await new ProfileRefresher(steam, settings, repo).RefreshAsync());
        Assert.Equal(2, steam.Count("summary"));
    }

    [Fact]
    public async Task Steam_failure_keeps_what_was_saved()
    {
        var (db, settings, repo) = await CreateAsync(new AppSettings { SteamProfile = Me.ToString(), SteamId64 = Me });
        await using var _ = db;
        await repo.SaveSteamAsync("Antigo", SteamAvatar);
        var steam = new FakeSteam();
        steam.Fail("summary", SteamErrorKind.Network);

        Assert.False(await new ProfileRefresher(steam, settings, repo).RefreshAsync());
        Assert.Equal(("Antigo", SteamAvatar, null), Fields(await repo.LoadAsync()));
    }

    [Fact]
    public async Task Unresolved_custom_url_waits_for_the_sync_without_calling_steam()
    {
        var (db, settings, repo) = await CreateAsync(new AppSettings { SteamProfile = "gabelogannewell" });
        await using var _ = db;
        var steam = new FakeSteam();

        Assert.False(await new ProfileRefresher(steam, settings, repo).RefreshAsync());
        Assert.Empty(steam.Calls);

        await settings.SaveAsync(new AppSettings { SteamProfile = "gabelogannewell", SteamId64 = Me });
        Assert.True(await new ProfileRefresher(steam, settings, repo).RefreshAsync());
        Assert.Equal("fake", (await repo.LoadAsync()).SteamName);
    }

    [Fact]
    public async Task Removing_the_profile_forgets_the_old_steam_account_but_not_the_choices()
    {
        var (db, settings, repo) = await CreateAsync(AppSettings.Default);
        await using var _ = db;
        await repo.SaveSteamAsync("Conta antiga", SteamAvatar);
        await repo.SaveChoicesAsync("Pablo", Photo);

        Assert.True(await new ProfileRefresher(new FakeSteam(), settings, repo).RefreshAsync());
        var p = await repo.LoadAsync();
        Assert.Equal((null, null, "Pablo"), Fields(p));
        Assert.Equal(Photo, p.CustomAvatar);
    }

    private static (string?, string?, string?) Fields(UserProfile p) => (p.SteamName, p.SteamAvatarUrl, p.CustomName);

    private static async Task<(TempDatabase, SettingsRepository, ProfileRepository)> CreateAsync(AppSettings initial)
    {
        var db = await TempDatabase.CreateMigratedAsync();
        var settings = new SettingsRepository(db.Factory, new FakeProtector());
        await settings.SaveAsync(initial);
        return (db, settings, new ProfileRepository(db.Factory));
    }
}
