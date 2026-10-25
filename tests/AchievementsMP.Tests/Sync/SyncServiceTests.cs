using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Core.Library;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;
using AchievementsMP.Sync;

namespace AchievementsMP.Tests.Sync;

public sealed class SyncServiceTests : IAsyncLifetime
{
    private const ulong Me = 76561197960435530;
    private const long Portal = 620, Celeste = 504230, Tf2 = 440;

    private TempDatabase _db = null!;
    private readonly FakeSteam _steam = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly List<TimeSpan> _delays = [];
    private SettingsRepository _settings = null!;
    private SteamCatalogRepository _catalog = null!;
    private PersonalDataRepository _personal = null!;
    private SyncRunRepository _runs = null!;

    public async Task InitializeAsync()
    {
        _db = await TempDatabase.CreateMigratedAsync();
        _settings = new SettingsRepository(_db.Factory, new FakeProtector());
        _catalog = new SteamCatalogRepository(_db.Factory);
        _personal = new PersonalDataRepository(_db.Factory);
        _runs = new SyncRunRepository(_db.Factory);
        await _settings.SaveAsync(new AppSettings { SteamProfile = Me.ToString(), SteamId64 = Me });

        _steam.AddGame(Portal, "Portal 2", 600, ("A", true), ("B", false));
        _steam.AddGame(Celeste, "Celeste", 300, ("C1", true), ("C2", true));
        _steam.AddGame(Tf2, "Team Fortress 2", 10); // sem conquistas
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private SyncService CreateService(SyncOptions? options = null) => new(_steam, _settings, _catalog, _runs, _clock,
        (options ?? new SyncOptions()) with { MinRequestSpacing = TimeSpan.Zero },
        (d, _) => { lock (_delays) _delays.Add(d); _clock.Advance(d); return Task.CompletedTask; });

    private async Task<LibraryGame> Game(long appId) => (await _catalog.GetLibraryAsync(includeNotOwned: true)).Single(g => g.AppId == appId);

    [Fact]
    public async Task First_sync_imports_library_achievements_and_rarity()
    {
        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, result.GamesInLibrary);
        Assert.Equal(new(2, 1, 1, 0), (await Game(Portal)).Progress);
        Assert.True((await Game(Celeste)).Progress.IsComplete);
        Assert.Equal(DataAvailability.NoAchievements, (await Game(Tf2)).Availability);
        Assert.Equal(0, _steam.Count($"player:{Tf2}"));
        Assert.Equal(SyncStatus.Succeeded, (await _runs.GetLastAsync(SyncRunRepository.LibraryScope, null))!.Status);
    }

    [Fact]
    public async Task Second_sync_without_changes_only_reads_the_library()
    {
        await CreateService().SyncAsync();
        _steam.Calls.Clear();
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(["owned"], _steam.Calls.ToArray());
    }

    [Fact]
    public async Task Playing_a_game_rereads_only_that_game()
    {
        await CreateService().SyncAsync();
        _steam.Calls.Clear();
        _steam.Library[0] = _steam.Library[0] with { PlaytimeMinutes = 700 };
        _steam.Player[Portal][1] = _steam.Player[Portal][1] with { State = UnlockState.Unlocked };

        await CreateService().SyncAsync();

        Assert.Equal(1, _steam.Count("player:"));
        Assert.Equal(1, _steam.Count($"player:{Portal}"));
        Assert.True((await Game(Portal)).Progress.IsComplete);
    }

    [Fact]
    public async Task Private_library_fails_and_keeps_existing_data()
    {
        await CreateService().SyncAsync();
        _steam.Fail("owned", SteamErrorKind.PrivateProfile);
        _clock.Advance(TimeSpan.FromHours(2));

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(SteamErrorKind.PrivateProfile, result.Error);
        Assert.Contains("Detalhes do jogo", result.Message);
        Assert.Equal(3, (await _catalog.GetLibraryAsync()).Count);
        Assert.Equal(new(2, 1, 1, 0), (await Game(Portal)).Progress);
    }

    [Fact]
    public async Task Invalid_key_aborts_the_whole_sync()
    {
        _steam.Fail($"schema:{Portal}", SteamErrorKind.InvalidApiKey);
        _steam.Fail($"schema:{Celeste}", SteamErrorKind.InvalidApiKey);
        _steam.Fail($"schema:{Tf2}", SteamErrorKind.InvalidApiKey);

        var result = await CreateService(new SyncOptions { MaxConcurrency = 1 }).SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(SteamErrorKind.InvalidApiKey, result.Error);
        Assert.Equal(1, _steam.Count("schema:"));
    }

    [Fact]
    public async Task Transient_errors_are_retried_with_growing_waits()
    {
        _steam.Fail($"player:{Portal}", SteamErrorKind.Network, times: 2);

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(3, _steam.Count($"player:{Portal}"));
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], _delays);
    }

    [Fact]
    public async Task Persistent_failure_marks_the_game_and_keeps_previous_progress()
    {
        await CreateService().SyncAsync();
        _steam.Library[0] = _steam.Library[0] with { PlaytimeMinutes = 700 };
        _steam.Fail($"player:{Portal}", SteamErrorKind.Transient, times: 10);

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.PartiallySucceeded, result.Outcome);
        Assert.Equal(1, result.GamesFailed);
        var portal = await Game(Portal);
        Assert.Equal(DataAvailability.SyncFailed, portal.Availability);
        Assert.NotNull(portal.LastError);
        Assert.Equal(new(2, 1, 1, 0), portal.Progress);

        // Próxima sincronização tenta de novo esse jogo, mesmo sem novo tempo de jogo.
        _steam.Failures.Clear();
        _steam.Calls.Clear();
        await CreateService().SyncAsync();
        Assert.Equal(1, _steam.Count($"player:{Portal}"));
        Assert.Equal(DataAvailability.Available, (await Game(Portal)).Availability);
    }

    [Fact]
    public async Task Rate_limit_pauses_before_retrying()
    {
        _steam.Fail("owned", SteamErrorKind.RateLimited, retryAfter: TimeSpan.FromSeconds(30));

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(2, _steam.Count("owned"));
        Assert.Contains(TimeSpan.FromSeconds(30), _delays);
    }

    [Fact]
    public async Task Cancelling_keeps_games_already_saved()
    {
        using var cts = new CancellationTokenSource();
        _steam.OnCall = call => { if (call.StartsWith("schema:", StringComparison.Ordinal) && _steam.Count("schema:") == 2) cts.Cancel(); };

        var result = await CreateService(new SyncOptions { MaxConcurrency = 1 }).SyncAsync(ct: cts.Token);

        Assert.Equal(SyncOutcome.Cancelled, result.Outcome);
        var synced = (await _catalog.GetLibraryAsync()).Count(g => g.Availability != DataAvailability.NotSynced);
        Assert.Equal(1, synced);
        Assert.Equal(SyncStatus.Cancelled, (await _runs.GetLastAsync(SyncRunRepository.LibraryScope, null))!.Status);
    }

    [Fact]
    public async Task Private_game_details_stop_the_sync_early()
    {
        for (var i = 0; i < 20; i++)
        {
            _steam.AddGame(1000 + i, $"Jogo {i:00}", 60, ("X", false));
            _steam.Fail($"player:{1000 + i}", SteamErrorKind.PrivateProfile);
        }
        _steam.Fail($"player:{Portal}", SteamErrorKind.PrivateProfile);
        _steam.Fail($"player:{Celeste}", SteamErrorKind.PrivateProfile);

        var result = await CreateService(new SyncOptions { MaxConcurrency = 1, PrivateAbortThreshold = 5 }).SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(SteamErrorKind.PrivateProfile, result.Error);
        Assert.Equal(5, _steam.Count("player:"));
    }

    [Fact]
    public async Task Game_without_achievements_is_not_queried_again_soon()
    {
        await CreateService().SyncAsync();
        _steam.Calls.Clear();
        _clock.Advance(TimeSpan.FromDays(8)); // vence o prazo do progresso, não o do "sem conquistas"

        await CreateService().SyncAsync();

        Assert.Equal(0, _steam.Count($"schema:{Tf2}"));
        Assert.Equal(1, _steam.Count($"player:{Portal}"));
    }

    [Fact]
    public async Task Changing_language_reloads_schemas()
    {
        await CreateService().SyncAsync();
        _steam.Calls.Clear();
        var s = await _settings.LoadAsync();
        await _settings.SaveAsync(s with { Language = "english" });

        await CreateService().SyncAsync();

        Assert.Equal(1, _steam.Count($"schema:{Portal}"));
        Assert.Equal(1, _steam.Count($"schema:{Celeste}"));
    }

    [Fact]
    public async Task Vanity_profile_is_resolved_once_and_saved()
    {
        _steam.Vanity["pablo"] = Me;
        await _settings.SaveAsync(new AppSettings { SteamProfile = "pablo" });

        await CreateService().SyncAsync();
        await CreateService().SyncAsync();

        Assert.Equal(1, _steam.Count("vanity"));
        Assert.Equal(Me, (await _settings.LoadAsync()).SteamId64);
    }

    [Fact]
    public async Task Missing_profile_fails_with_clear_message_and_no_calls()
    {
        await _settings.SaveAsync(new AppSettings());

        var result = await CreateService().SyncAsync();

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Contains("Configurações", result.Message);
        Assert.Empty(_steam.Calls);
    }

    [Fact]
    public async Task Switching_accounts_discards_old_progress_but_keeps_personal_data()
    {
        await CreateService().SyncAsync();
        var list = await _personal.CreateListAsync("Minha lista", null, _clock.GetUtcNow());
        await _personal.AddGameToListAsync(list, Portal, _clock.GetUtcNow());
        await _personal.SetAchievementNoteAsync(Portal, "B", "nota", _clock.GetUtcNow());

        const ulong other = 76561197960287930;
        _steam.Library = [_steam.Library[0] with { PlaytimeMinutes = 5 }];
        _steam.Player[Portal] = [new(Portal, "A", UnlockState.Locked, null), new(Portal, "B", UnlockState.Locked, null)];
        await _settings.SaveAsync(new AppSettings { SteamProfile = other.ToString(), SteamId64 = other });

        await CreateService().SyncAsync();

        var owned = await _catalog.GetLibraryAsync();
        Assert.Equal([Portal], owned.Select(g => g.AppId));
        Assert.Equal(new(2, 0, 2, 0), owned[0].Progress);
        Assert.Equal(0, (await Game(Celeste)).Progress.Unlocked); // progresso da conta anterior foi descartado
        Assert.Equal([Portal], await _personal.GetListGamesAsync(list));
        Assert.Equal("nota", await _personal.GetAchievementNoteAsync(Portal, "B"));
    }

    [Fact]
    public async Task Progress_is_reported_per_game()
    {
        var reports = new List<SyncProgress>();
        var progress = new SynchronousProgress<SyncProgress>(reports.Add);

        await CreateService(new SyncOptions { MaxConcurrency = 1 }).SyncAsync(progress);

        var games = reports.Where(r => r.Stage == SyncStage.Games).ToList();
        Assert.Equal(3, games.Count);
        Assert.Equal(3, games[^1].Done);
        Assert.Equal(SyncStage.Finished, reports[^1].Stage);
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Lock _lock = new();
        public void Report(T value) { lock (_lock) report(value); }
    }
}
