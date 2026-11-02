using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Settings;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Sync;
using AchievementsMP.Tests.Sync;
using AchievementsMP.Xbox;

namespace AchievementsMP.Tests.Xbox;

internal sealed class FakeSignIn : IXboxSignIn
{
    public string? Token { get; set; } = "MSA-TOKEN";
    public bool IsAvailable => true;
    public Task<bool> HasAccountAsync(CancellationToken ct = default) => Task.FromResult(Token is not null);
    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(Token);
    public Task<string> SignInAsync(CancellationToken ct = default) => Task.FromResult(Token ?? "MSA-TOKEN");
    public Task SignOutAsync(CancellationToken ct = default) { Token = null; return Task.CompletedTask; }
}

/// <summary>Xbox em memória. Falhas são enfileiradas por chamada ("auth", "history", "achievements:&lt;titleId&gt;").</summary>
internal sealed class FakeXbox : IXboxLiveApi
{
    public static readonly XboxIdentity Me = new("2533274800000000", "pablordfs", "1", "T", null);
    public XboxIdentity Identity { get; set; } = Me;
    public List<XboxTitle> Titles { get; } = [];
    public Dictionary<string, List<XboxAchievement>> Achievements { get; } = [];
    public Dictionary<string, Queue<XboxError>> Failures { get; } = [];
    public List<string> Calls { get; } = [];

    public void Fail(string call, XboxError error, int times = 1)
    {
        if (!Failures.TryGetValue(call, out var q)) Failures[call] = q = new();
        for (var i = 0; i < times; i++) q.Enqueue(error);
    }

    public void AddGame(string titleId, string name, DateTimeOffset? lastPlayed, params (string Id, bool Unlocked, double? Rarity)[] achievements)
    {
        Titles.Add(new XboxTitle(titleId, name, $"https://images.example/{titleId}.png", lastPlayed,
            achievements.Count(a => a.Unlocked), achievements.Length, 0, 0, false));
        Achievements[titleId] = achievements.Select(a => new XboxAchievement(a.Id, $"Conquista {a.Id}", null, false, null, a.Unlocked,
            a.Unlocked ? lastPlayed : null, a.Rarity, 10)).ToList();
    }

    public void Unlock(string titleId, string achievementId, DateTimeOffset when)
    {
        var list = Achievements[titleId];
        var i = list.FindIndex(a => a.Id == achievementId);
        list[i] = list[i] with { IsUnlocked = true, UnlockedUtc = when };
        var t = Titles.FindIndex(x => x.TitleId == titleId);
        Titles[t] = Titles[t] with { CurrentAchievements = list.Count(a => a.IsUnlocked), LastPlayedUtc = when };
    }

    private bool Failing<T>(string call, out XboxResult<T> result)
    {
        Calls.Add(call);
        if (Failures.TryGetValue(call, out var q) && q.TryDequeue(out var e))
        {
            result = XboxResult<T>.Fail(e);
            return true;
        }
        result = default;
        return false;
    }

    public Task<XboxResult<XboxIdentity>> AuthorizeAsync(string microsoftAccessToken, CancellationToken ct = default) =>
        Task.FromResult(Failing<XboxIdentity>("auth", out var f) ? f : XboxResult<XboxIdentity>.Ok(Identity));

    public Task<XboxResult<IReadOnlyList<XboxTitle>>> GetTitleHistoryAsync(XboxIdentity identity, string language, CancellationToken ct = default) =>
        Task.FromResult(Failing<IReadOnlyList<XboxTitle>>("history", out var f) ? f : XboxResult<IReadOnlyList<XboxTitle>>.Ok(Titles.ToList()));

    public Task<XboxResult<IReadOnlyList<XboxAchievement>>> GetAchievementsAsync(XboxIdentity identity, string titleId, string language, CancellationToken ct = default) =>
        Task.FromResult(Failing<IReadOnlyList<XboxAchievement>>($"achievements:{titleId}", out var f)
            ? f
            : XboxResult<IReadOnlyList<XboxAchievement>>.Ok(Achievements.GetValueOrDefault(titleId)?.ToList() ?? []));
}

public sealed class XboxSyncServiceTests : IAsyncLifetime
{
    private const string Halo = "1717113201", Forza = "219630713";
    private static readonly DateTimeOffset Played = new(2026, 10, 20, 21, 0, 0, TimeSpan.Zero);

    private TempDatabase _db = null!;
    private readonly FakeXbox _xbox = new();
    private readonly FakeSignIn _signIn = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private SettingsRepository _settings = null!;
    private CatalogRepository _catalog = null!;
    private SyncRunRepository _runs = null!;

    public async Task InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync();
        _settings = new SettingsRepository(_db.Factory, new FakeProtector());
        _catalog = new CatalogRepository(_db.Factory);
        _runs = new SyncRunRepository(_db.Factory);

        _xbox.AddGame(Halo, "Halo Infinite", Played, ("1", true, 61.2), ("2", true, 3.4), ("3", false, 0.8));
        _xbox.AddGame(Forza, "Forza Horizon 5", Played.AddDays(-3), ("10", false, 40));
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private XboxSyncService CreateService() => new(_signIn, _xbox, _settings, _catalog, _runs, _clock,
        new XboxSyncOptions { MinRequestSpacing = TimeSpan.Zero },
        (d, _) => { _clock.Advance(d); return Task.CompletedTask; });

    private async Task<LibraryGame> Game(string name) => (await _catalog.GetLibraryAsync(includeNotOwned: true)).Single(g => g.Name == name);

    [Fact]
    public async Task First_sync_imports_games_achievements_and_rarity()
    {
        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, result.GamesInLibrary);
        var halo = await Game("Halo Infinite");
        Assert.Equal(Platform.Xbox, halo.Platform);
        Assert.True(Platform.Xbox.Owns(halo.AppId));
        Assert.Equal(new(3, 2, 1, 0), halo.Progress);
        Assert.Equal(0.8, halo.Outlook.RarestRemainingPercent);
        Assert.Equal("https://images.example/1717113201.png", halo.IconUrl);

        var details = await _catalog.GetAchievementsAsync(halo.AppId);
        Assert.Equal(Played, details.Single(a => a.ApiName == "1").UnlockedAtUtc);
        Assert.Equal("2533274800000000", await _catalog.GetOwnerAsync(Platform.Xbox));
        Assert.Equal("pablordfs", (await _settings.LoadAsync()).XboxGamertag);
        Assert.Equal(SyncStatus.Succeeded, (await _runs.GetLastAsync(SyncRunRepository.XboxScope, null))!.Status);
    }

    [Fact]
    public async Task Second_sync_without_changes_only_reads_the_history()
    {
        await CreateService().SyncAsync();
        _xbox.Calls.Clear();
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(["auth", "history"], _xbox.Calls);
    }

    [Fact]
    public async Task New_unlock_rereads_only_that_game()
    {
        await CreateService().SyncAsync();
        _xbox.Calls.Clear();
        _clock.Advance(TimeSpan.FromHours(2));
        _xbox.Unlock(Halo, "3", _clock.GetUtcNow());

        await CreateService().SyncAsync();

        Assert.Equal(["auth", "history", $"achievements:{Halo}"], _xbox.Calls);
        Assert.True((await Game("Halo Infinite")).Progress.IsComplete);
    }

    [Fact]
    public async Task Xbox_360_and_games_without_achievements_are_not_read()
    {
        _xbox.Titles.Add(new XboxTitle("1297287142", "Fable II", null, Played, 10, 50, 0, 0, IsXbox360: true));
        _xbox.Titles.Add(new XboxTitle("1828326430", "Minecraft Launcher", null, Played, 0, 0, 0, 0, false));

        await CreateService().SyncAsync();

        Assert.DoesNotContain("achievements:1297287142", _xbox.Calls);
        Assert.DoesNotContain("achievements:1828326430", _xbox.Calls);
        var fable = await Game("Fable II");
        Assert.Equal(DataAvailability.SchemaUnavailable, fable.Availability);
        Assert.Equal(XboxSyncPlanner.Xbox360Message, fable.LastError);
        Assert.Equal(DataAvailability.NoAchievements, (await Game("Minecraft Launcher")).Availability);
    }

    [Fact]
    public async Task Xbox_sync_never_touches_steam_games_or_personal_data()
    {
        await _catalog.UpsertLibraryAsync([new Game(620, "Portal 2", null, 60, null, true)], _clock.GetUtcNow(), isCompleteLibrary: true);
        var personal = new PersonalDataRepository(_db.Factory);
        var list = await personal.CreateListAsync("Platinar", null, _clock.GetUtcNow());
        await personal.AddGameToListAsync(list, 620, _clock.GetUtcNow());

        await CreateService().SyncAsync();
        _xbox.Titles.Clear();
        _xbox.AddGame(Forza, "Forza Horizon 5", Played, ("10", false, 40));
        await CreateService().SyncAsync();

        Assert.True((await Game("Portal 2")).IsOwned);
        Assert.False((await Game("Halo Infinite")).IsOwned);
        Assert.Equal([620L], await personal.GetListGamesAsync(list));
    }

    [Fact]
    public async Task Expired_login_fails_without_calling_xbox()
    {
        _signIn.Token = null;

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Contains("Conecte de novo", result.Message);
        Assert.Empty(_xbox.Calls);
    }

    [Fact]
    public async Task One_failing_game_keeps_previous_data_and_the_rest_is_saved()
    {
        await CreateService().SyncAsync();
        _clock.Advance(TimeSpan.FromDays(8));
        _xbox.Fail($"achievements:{Halo}", new XboxError(XboxErrorKind.InvalidResponse, "estranho"));

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.PartiallySucceeded, result.Outcome);
        var halo = await Game("Halo Infinite");
        Assert.Equal(DataAvailability.SyncFailed, halo.Availability);
        Assert.Equal(2, halo.Progress.Unlocked);
    }

    [Fact]
    public async Task Rate_limit_that_does_not_pass_stops_the_sync()
    {
        _xbox.Fail($"achievements:{Halo}", new XboxError(XboxErrorKind.RateLimited, "Limite de consultas do Xbox atingido (429).", TimeSpan.FromSeconds(5)), times: 10);

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.DoesNotContain($"achievements:{Forza}", _xbox.Calls);
        Assert.Equal(4, _xbox.Calls.Count(c => c == $"achievements:{Halo}"));
    }

    [Fact]
    public async Task Transient_error_is_retried()
    {
        _xbox.Fail("history", new XboxError(XboxErrorKind.Transient, "503"));

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, _xbox.Calls.Count(c => c == "history"));
    }

    [Fact]
    public async Task Other_xbox_account_replaces_previous_xbox_progress()
    {
        await CreateService().SyncAsync();
        _xbox.Identity = FakeXbox.Me with { Xuid = "2533274899999999", Gamertag = "outra" };
        _xbox.Titles.Clear();
        _xbox.AddGame(Forza, "Forza Horizon 5", Played, ("10", false, 40));

        await CreateService().SyncAsync();

        var halo = await Game("Halo Infinite");
        Assert.False(halo.IsOwned);
        Assert.Equal(0, halo.Progress.Unlocked);
        Assert.Equal("outra", (await _settings.LoadAsync()).XboxGamertag);
    }

    [Theory]
    [InlineData("brazilian", "pt-BR")]
    [InlineData("english", "en-US")]
    [InlineData("latam", "es-MX")]
    [InlineData("qualquer", "pt-BR")]
    public void Language_maps_to_xbox_format(string steam, string xbox) => Assert.Equal(xbox, XboxSyncPlanner.Language(steam));
}
