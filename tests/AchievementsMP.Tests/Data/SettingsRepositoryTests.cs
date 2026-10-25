using System.Text;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using Dapper;

namespace AchievementsMP.Tests.Data;

public class SettingsRepositoryTests
{
    [Fact]
    public async Task Defaults_when_nothing_saved()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        Assert.Equal(AppSettings.Default, await new SettingsRepository(db.Factory, new FakeProtector()).LoadAsync());
    }

    [Fact]
    public async Task Settings_survive_reopening_the_database()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var saved = new AppSettings { SteamProfile = "gabelogannewell", SteamId64 = 76561197960287930, Language = "english", SyncOnStartup = false, SyncIntervalMinutes = 60 };
        await new SettingsRepository(db.Factory, new FakeProtector()).SaveAsync(saved);

        var reopened = new SettingsRepository(new AchievementsMP.Data.SqliteConnectionFactory(db.Paths.Database), new FakeProtector());
        Assert.Equal(saved, await reopened.LoadAsync());
    }

    [Fact]
    public async Task Api_key_is_never_stored_in_plain_text()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SettingsRepository(db.Factory, new FakeProtector());
        const string key = "0123456789ABCDEF0123456789ABCDEF";

        await repo.SetSteamApiKeyAsync(key);

        await using var c = await db.Factory.OpenAsync();
        var raw = await c.QuerySingleAsync<byte[]>("SELECT value FROM settings WHERE key = 'steam_api_key'");
        Assert.DoesNotContain(key, Encoding.UTF8.GetString(raw));
        Assert.Equal(key, await repo.GetSteamApiKeyAsync());
    }

    [Fact]
    public async Task Clearing_the_key_removes_it()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new SettingsRepository(db.Factory, new FakeProtector());
        await repo.SetSteamApiKeyAsync("abc");
        await repo.SetSteamApiKeyAsync("  ");
        Assert.Null(await repo.GetSteamApiKeyAsync());
    }

    [Fact]
    public async Task Undecryptable_key_reads_as_missing_instead_of_crashing()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO settings(key, value) VALUES ('steam_api_key', @v)", new { v = new byte[] { 1, 2, 3, 4, 5 } });

        Assert.Null(await new SettingsRepository(db.Factory, new FakeProtector()).GetSteamApiKeyAsync());
    }

    [Fact]
    public async Task Corrupted_settings_fall_back_to_defaults()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        await using (var c = await db.Factory.OpenAsync())
            await c.ExecuteAsync("INSERT INTO settings(key, value) VALUES ('app_settings', @v)", new { v = Encoding.UTF8.GetBytes("{nao é json") });

        Assert.Equal(AppSettings.Default, await new SettingsRepository(db.Factory, new FakeProtector()).LoadAsync());
    }
}
