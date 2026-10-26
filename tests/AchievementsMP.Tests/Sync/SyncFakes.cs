using System.Collections.Concurrent;
using AchievementsMP.Core.Domain;
using AchievementsMP.Steam;

namespace AchievementsMP.Tests.Sync;

internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _lock = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() { lock (_lock) return _now; }

    public void Advance(TimeSpan by) { lock (_lock) _now += by; }
}

/// <summary>Steam em memória. Falhas são enfileiradas por chamada ("owned", "schema:620", "player:620", "global:620", "vanity").</summary>
internal sealed class FakeSteam : ISteamWebApi
{
    public Dictionary<string, ulong> Vanity { get; } = new();
    public List<Game> Library { get; set; } = [];
    public Dictionary<long, List<AchievementDefinition>> Schemas { get; } = new();
    public Dictionary<long, List<PlayerAchievementState>> Player { get; } = new();
    public Dictionary<long, Dictionary<string, double>> Global { get; } = new();
    public ConcurrentDictionary<string, ConcurrentQueue<SteamError>> Failures { get; } = new();
    public ConcurrentQueue<string> Calls { get; } = new();
    public bool ProfilePublic { get; set; } = true;

    /// <summary>Executado antes de cada chamada; útil para cancelar no meio.</summary>
    public Action<string>? OnCall { get; set; }

    public void Fail(string call, SteamErrorKind kind, int times = 1, TimeSpan? retryAfter = null)
    {
        var q = Failures.GetOrAdd(call, _ => new ConcurrentQueue<SteamError>());
        for (var i = 0; i < times; i++) q.Enqueue(new SteamError(kind, $"fake {kind}", retryAfter));
    }

    public void AddGame(long appId, string name, int playtime, params (string ApiName, bool Unlocked)[] achievements)
    {
        Library.Add(new Game(appId, name, "hash", playtime, null, true));
        Schemas[appId] = achievements.Select(a => new AchievementDefinition(appId, a.ApiName, a.ApiName, null, false, null, null)).ToList();
        Player[appId] = achievements.Select(a => new PlayerAchievementState(appId, a.ApiName, a.Unlocked ? UnlockState.Unlocked : UnlockState.Locked, null)).ToList();
        Global[appId] = achievements.ToDictionary(a => a.ApiName, _ => 50.0);
    }

    public int Count(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));

    private SteamResult<T> Respond<T>(string call, Func<SteamResult<T>> success)
    {
        Calls.Enqueue(call);
        OnCall?.Invoke(call);
        if (Failures.TryGetValue(call, out var q) && q.TryDequeue(out var error))
            return SteamResult<T>.Fail(error);
        return success();
    }

    public Task<SteamResult<ulong>> ResolveVanityUrlAsync(string vanityName, CancellationToken ct = default) =>
        Task.FromResult(Respond("vanity", () => Vanity.TryGetValue(vanityName, out var id)
            ? SteamResult<ulong>.Ok(id) : SteamResult<ulong>.Fail(SteamErrorKind.NotFound, "não achou")));

    public Task<SteamResult<PlayerSummary>> GetPlayerSummaryAsync(ulong steamId, CancellationToken ct = default) =>
        Task.FromResult(Respond("summary", () => SteamResult<PlayerSummary>.Ok(new PlayerSummary(steamId, "fake", null, ProfilePublic))));

    public Task<SteamResult<OwnedGames>> GetOwnedGamesAsync(ulong steamId, CancellationToken ct = default) =>
        Task.FromResult(Respond("owned", () => SteamResult<OwnedGames>.Ok(new OwnedGames(Library.Count, Library.ToList()))));

    public Task<SteamResult<IReadOnlyList<AchievementDefinition>>> GetSchemaForGameAsync(long appId, string language, CancellationToken ct = default) =>
        Task.FromResult(Respond($"schema:{appId}", () => SteamResult<IReadOnlyList<AchievementDefinition>>.Ok(Schemas.GetValueOrDefault(appId) ?? [])));

    public Task<SteamResult<IReadOnlyList<PlayerAchievementState>>> GetPlayerAchievementsAsync(ulong steamId, long appId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Respond($"player:{appId}", () => SteamResult<IReadOnlyList<PlayerAchievementState>>.Ok(Player.GetValueOrDefault(appId) ?? [])));
    }

    public Task<SteamResult<IReadOnlyDictionary<string, double>>> GetGlobalAchievementPercentagesAsync(long appId, CancellationToken ct = default) =>
        Task.FromResult(Respond($"global:{appId}", () => SteamResult<IReadOnlyDictionary<string, double>>.Ok(Global.GetValueOrDefault(appId) ?? new())));
}
