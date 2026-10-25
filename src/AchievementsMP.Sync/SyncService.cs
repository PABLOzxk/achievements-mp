using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Core.Sync;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Steam;

namespace AchievementsMP.Sync;

/// <summary>
/// Sincroniza biblioteca e conquistas. Garantias:
/// - cada jogo é gravado de forma independente: uma falha não desfaz o que já foi salvo, nem apaga dados antigos;
/// - erros transitórios são repetidos com espera crescente; 429 pausa todas as chamadas;
/// - cancelável a qualquer momento;
/// - nunca escreve em listas, metas ou anotações.
/// </summary>
public sealed class SyncService
{
    private readonly ISteamWebApi _steam;
    private readonly ISettingsStore _settings;
    private readonly SteamCatalogRepository _catalog;
    private readonly SyncRunRepository _runs;
    private readonly TimeProvider _time;
    private readonly SyncOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private int _running;

    public SyncService(ISteamWebApi steam, ISettingsStore settings, SteamCatalogRepository catalog, SyncRunRepository runs,
        TimeProvider time, SyncOptions? options = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _steam = steam;
        _settings = settings;
        _catalog = catalog;
        _runs = runs;
        _time = time;
        _options = options ?? SyncOptions.Default;
        _delay = delay ?? ((d, ct) => Task.Delay(d, time, ct));
    }

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <param name="force">Ignora o planejamento incremental e relê tudo.</param>
    public async Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress = null, bool force = false, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new SyncResult(SyncOutcome.Failed, "Já existe uma sincronização em andamento.");
        try
        {
            return await RunAsync(progress, force, ct);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<SyncResult> RunAsync(IProgress<SyncProgress>? progress, bool force, CancellationToken ct)
    {
        var runId = await _runs.StartAsync(SyncRunRepository.LibraryScope, null, _time.GetUtcNow(), CancellationToken.None);
        var throttle = new RequestThrottle(_options.MinRequestSpacing, _time, _delay);
        progress?.Report(new SyncProgress(SyncStage.Starting, 0, 0, null));

        try
        {
            var settings = await _settings.LoadAsync(ct);

            // 1. Quem é o jogador.
            var steamId = await ResolveSteamIdAsync(settings, throttle, ct);
            if (!steamId.IsSuccess)
                return await FinishAsync(runId, Failure(steamId.Error!, "identificar o perfil"));
            await _catalog.SetOwnerSteamIdAsync(steamId.Value, ct);

            // 2. Biblioteca.
            progress?.Report(new SyncProgress(SyncStage.Library, 0, 0, null));
            var owned = await CallAsync(c => _steam.GetOwnedGamesAsync(steamId.Value, c), throttle, ct);
            if (!owned.IsSuccess)
                return await FinishAsync(runId, Failure(owned.Error!, "ler a biblioteca"));
            await _catalog.UpsertLibraryAsync(owned.Value!.Games, _time.GetUtcNow(), isCompleteLibrary: true, ct);

            // 3. Conquistas, só onde precisa.
            var states = await _catalog.GetSyncStatesAsync(ct);
            var tasks = SyncPlanner.Plan(states, settings.Language, _time.GetUtcNow(), _options.Policy, force);
            var games = owned.Value.Games.Count;
            var outcome = await SyncGamesAsync(steamId.Value, settings.Language, tasks, throttle, progress, ct);

            progress?.Report(new SyncProgress(SyncStage.Finished, tasks.Count, tasks.Count, null));
            return await FinishAsync(runId, outcome with { GamesInLibrary = games });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await FinishAsync(runId, new SyncResult(SyncOutcome.Cancelled, "Sincronização cancelada. O que já tinha sido atualizado foi mantido."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Erro nosso (banco, bug). O que já foi gravado continua válido; o histórico registra a falha.
            await FinishAsync(runId, new SyncResult(SyncOutcome.Failed, $"Erro interno: {ex.Message}"));
            throw;
        }
    }

    private async Task<SyncResult> SyncGamesAsync(ulong steamId, string language, IReadOnlyList<GameSyncTask> tasks, RequestThrottle throttle,
        IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SteamError? fatal = null;
        int done = 0, failed = 0, privateCount = 0, playerOk = 0;

        void Abort(SteamError error)
        {
            Interlocked.CompareExchange(ref fatal, error, null);
            abort.Cancel();
        }

        try
        {
            await Parallel.ForEachAsync(tasks, new ParallelOptions { MaxDegreeOfParallelism = _options.MaxConcurrency, CancellationToken = abort.Token }, async (task, c) =>
            {
                var result = await SyncGameAsync(steamId, language, task, throttle, c);
                switch (result)
                {
                    case GameResult.Ok:
                        Interlocked.Increment(ref playerOk);
                        break;
                    case GameResult.Private:
                        var privates = Interlocked.Increment(ref privateCount);
                        if (privates >= _options.PrivateAbortThreshold && Volatile.Read(ref playerOk) == 0)
                            Abort(new SteamError(SteamErrorKind.PrivateProfile, "Os “Detalhes do jogo” do seu perfil estão privados."));
                        break;
                    case GameResult.Failed:
                        Interlocked.Increment(ref failed);
                        break;
                    case GameResult.Fatal fatalResult:
                        Abort(fatalResult.Error);
                        break;
                }

                var n = Interlocked.Increment(ref done);
                progress?.Report(new SyncProgress(SyncStage.Games, n, tasks.Count, task.Name));
            });
        }
        catch (OperationCanceledException) when (fatal is not null && !ct.IsCancellationRequested)
        {
            // Abortado por erro que afeta todos os jogos; tratado abaixo.
        }

        if (fatal is not null)
            return Failure(fatal, "ler as conquistas") with { GamesChecked = done, GamesFailed = failed };

        if (failed > 0)
            return new SyncResult(SyncOutcome.PartiallySucceeded,
                $"{done - failed} de {done} jogos atualizados. {failed} falharam e mantêm os dados anteriores; tentaremos de novo na próxima sincronização.",
                GamesChecked: done, GamesFailed: failed);

        return new SyncResult(SyncOutcome.Succeeded,
            done == 0 ? "Tudo já estava atualizado." : $"{done} jogos atualizados.", GamesChecked: done);
    }

    private abstract record GameResult
    {
        public sealed record Ok : GameResult;
        public sealed record NothingToRead : GameResult;
        public sealed record Private : GameResult;
        public sealed record Failed : GameResult;
        public sealed record Fatal(SteamError Error) : GameResult;
    }

    private async Task<GameResult> SyncGameAsync(ulong steamId, string language, GameSyncTask task, RequestThrottle throttle, CancellationToken ct)
    {
        var hasAchievements = task.HasKnownAchievements;

        if (task.Work.HasFlag(GameSyncWork.Schema))
        {
            var schema = await CallAsync(c => _steam.GetSchemaForGameAsync(task.AppId, language, c), throttle, ct);
            if (!schema.IsSuccess)
                return await RecordFailureAsync(task.AppId, schema.Error!);

            await _catalog.UpsertSchemaAsync(task.AppId, schema.Value!, _time.GetUtcNow(), language, ct);
            hasAchievements = schema.Value!.Count > 0;

            if (hasAchievements)
            {
                // Raridade é opcional: se falhar, o jogo continua válido.
                var global = await CallAsync(c => _steam.GetGlobalAchievementPercentagesAsync(task.AppId, c), throttle, ct);
                if (global.IsSuccess)
                    await _catalog.UpsertGlobalPercentagesAsync(task.AppId, global.Value!, _time.GetUtcNow(), ct);
            }
        }

        if (!hasAchievements || !task.Work.HasFlag(GameSyncWork.Player))
            return new GameResult.NothingToRead();

        var player = await CallAsync(c => _steam.GetPlayerAchievementsAsync(steamId, task.AppId, c), throttle, ct);
        if (!player.IsSuccess)
            return await RecordFailureAsync(task.AppId, player.Error!);

        await _catalog.ApplyPlayerStatesAsync(task.AppId, player.Value!, _time.GetUtcNow(), ct);
        return new GameResult.Ok();
    }

    private async Task<GameResult> RecordFailureAsync(long appId, SteamError error)
    {
        // Gravação de estado não usa o token: mesmo cancelando, registramos o motivo da falha.
        switch (error.Kind)
        {
            case SteamErrorKind.InvalidApiKey:
                return new GameResult.Fatal(error);
            case SteamErrorKind.PrivateProfile:
                await _catalog.SetAvailabilityAsync(appId, DataAvailability.PrivateProfile, error.Message);
                return new GameResult.Private();
            case SteamErrorKind.NoStats:
            case SteamErrorKind.NotFound:
                await _catalog.SetAvailabilityAsync(appId, DataAvailability.SchemaUnavailable, error.Message);
                return new GameResult.Failed();
            default:
                await _catalog.SetAvailabilityAsync(appId, DataAvailability.SyncFailed, error.Message);
                return new GameResult.Failed();
        }
    }

    private async Task<SteamResult<ulong>> ResolveSteamIdAsync(AppSettings settings, RequestThrottle throttle, CancellationToken ct)
    {
        switch (SteamIdentity.Parse(settings.SteamProfile))
        {
            case SteamProfileInput.SteamId id:
                return SteamResult<ulong>.Ok(id.Value);
            case SteamProfileInput.Vanity vanity:
                if (settings.SteamId64 is { } known)
                    return SteamResult<ulong>.Ok(known);
                var resolved = await CallAsync(c => _steam.ResolveVanityUrlAsync(vanity.Name, c), throttle, ct);
                if (resolved.IsSuccess)
                    await _settings.SaveAsync(settings with { SteamId64 = resolved.Value }, ct);
                return resolved;
            default:
                return SteamResult<ulong>.Fail(SteamErrorKind.NotFound, "Perfil Steam não configurado. Abra Configurações e informe seu perfil.");
        }
    }

    /// <summary>Chama a Steam respeitando o espaçamento, com novas tentativas para erros transitórios.</summary>
    private async Task<SteamResult<T>> CallAsync<T>(Func<CancellationToken, Task<SteamResult<T>>> call, RequestThrottle throttle, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await throttle.WaitAsync(ct);
            var result = await call(ct);

            if (result.Error is { Kind: SteamErrorKind.Cancelled })
                ct.ThrowIfCancellationRequested();
            if (result.IsSuccess || !result.Error!.IsRetryable || attempt >= _options.MaxAttempts)
                return result;

            if (result.Error.Kind == SteamErrorKind.RateLimited)
                throttle.PauseFor(result.Error.RetryAfter ?? _options.DefaultRateLimitPause);
            else
                await _delay(_options.BaseBackoff * Math.Pow(2, attempt - 1), ct);
        }
    }

    private static SyncResult Failure(SteamError error, string step) => new(SyncOutcome.Failed, error.Kind switch
    {
        SteamErrorKind.InvalidApiKey => "A Steam recusou a chave da API. Confira a chave em Configurações.",
        SteamErrorKind.PrivateProfile => $"{error.Message} Em Steam > Perfil > Editar perfil > Privacidade, deixe “Meu perfil” e “Detalhes do jogo” como Público.",
        SteamErrorKind.RateLimited => "A Steam pediu para esperar antes de novas consultas. Tente de novo mais tarde.",
        SteamErrorKind.Network or SteamErrorKind.Transient => $"Não foi possível {step}: sem conexão com a Steam. Seus dados anteriores continuam disponíveis.",
        _ => $"Não foi possível {step}: {error.Message}",
    }, Error: error.Kind);

    private async Task<SyncResult> FinishAsync(long runId, SyncResult result)
    {
        var status = result.Outcome switch
        {
            SyncOutcome.Succeeded or SyncOutcome.PartiallySucceeded => SyncStatus.Succeeded,
            SyncOutcome.Cancelled => SyncStatus.Cancelled,
            _ => SyncStatus.Failed,
        };
        await _runs.FinishAsync(runId, status, _time.GetUtcNow(), result.Error?.ToString(), result.Message, CancellationToken.None);
        return result;
    }
}
