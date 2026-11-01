using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Core.Sync;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Xbox;

namespace AchievementsMP.Sync;

public sealed record XboxSyncOptions
{
    /// <summary>Intervalo mínimo entre duas chamadas ao Xbox. Uma de cada vez: o Xbox limita por usuário.</summary>
    public TimeSpan MinRequestSpacing { get; init; } = TimeSpan.FromMilliseconds(400);
    public int MaxAttempts { get; init; } = 4;
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(2);
    public SyncPolicy Policy { get; init; } = SyncPolicy.Default;

    public static readonly XboxSyncOptions Default = new();
}

/// <summary>O que fazer com cada jogo do histórico do Xbox.</summary>
internal enum XboxGameWork { Skip, Achievements, NoAchievements, Xbox360 }

internal static class XboxSyncPlanner
{
    public const string Xbox360Message = "Conquistas de jogos de Xbox 360 ainda não são lidas pelo app.";

    /// <param name="unlockedNow">Conquistas desbloqueadas que o banco já tem para o jogo.</param>
    public static XboxGameWork Plan(XboxTitle title, GameSyncState? state, int unlockedNow, string language, DateTimeOffset now, SyncPolicy policy, bool force)
    {
        if (title.TotalAchievements == 0)
            return state?.SchemaSyncedUtc is { } checkedAt && now - checkedAt < policy.NoAchievementsRecheck && !force
                ? XboxGameWork.Skip
                : XboxGameWork.NoAchievements;

        if (title.IsXbox360)
            return state?.Availability == DataAvailability.SchemaUnavailable && !force ? XboxGameWork.Skip : XboxGameWork.Xbox360;

        if (force || state is null || state.PlayerSyncedUtc is not { } synced || state.SchemaSyncedUtc is not { } schemaAt)
            return XboxGameWork.Achievements;

        var changed = unlockedNow != title.CurrentAchievements
            || title.LastPlayedUtc > synced
            || state.Availability is not DataAvailability.Available
            || !string.Equals(state.SchemaLanguage, language, StringComparison.Ordinal)
            || now - synced >= policy.PlayerMaxAge
            || now - schemaAt >= policy.SchemaMaxAge;
        return changed ? XboxGameWork.Achievements : XboxGameWork.Skip;
    }

    /// <summary>Idioma das configurações (nome da Steam) no formato que o Xbox entende.</summary>
    public static string Language(string steamLanguage) => steamLanguage switch
    {
        "english" => "en-US",
        "spanish" => "es-ES",
        "latam" => "es-MX",
        _ => "pt-BR",
    };
}

/// <summary>
/// Sincroniza os jogos e conquistas do Xbox. Mesmas garantias da sincronização da Steam: cada jogo é gravado sozinho,
/// falha não apaga dado anterior, cancelável, e nunca escreve em listas, metas ou anotações. Só mexe em jogos do Xbox.
/// </summary>
public sealed class XboxSyncService(IXboxSignIn signIn, IXboxLiveApi xbox, ISettingsStore settings, CatalogRepository catalog, SyncRunRepository runs,
    TimeProvider time, XboxSyncOptions? options = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly XboxSyncOptions _options = options ?? XboxSyncOptions.Default;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((d, ct) => Task.Delay(d, time, ct));
    private int _running;

    /// <summary>Há um login do Xbox guardado. Sem ele a sincronização do Xbox nem começa.</summary>
    public async Task<bool> IsConnectedAsync(CancellationToken ct = default) => signIn.IsAvailable && await signIn.HasAccountAsync(ct);

    /// <summary>Confere o login recém-feito e guarda a gamertag para mostrar em Configurações.</summary>
    public async Task<XboxResult<XboxIdentity>> IdentifyAsync(string microsoftAccessToken, CancellationToken ct = default)
    {
        var identity = await xbox.AuthorizeAsync(microsoftAccessToken, ct);
        if (identity.IsSuccess)
            await RememberGamertagAsync(identity.Value!, ct);
        return identity;
    }

    public async Task<SyncResult> SyncAsync(IProgress<SyncProgress>? progress = null, bool force = false, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new SyncResult(SyncOutcome.Failed, "Já existe uma sincronização do Xbox em andamento.");
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
        var runId = await runs.StartAsync(SyncRunRepository.XboxScope, null, time.GetUtcNow(), CancellationToken.None);
        var throttle = new RequestThrottle(_options.MinRequestSpacing, time, _delay);
        progress?.Report(new SyncProgress(SyncStage.Starting, 0, 0, null));

        try
        {
            var token = await signIn.GetAccessTokenAsync(ct);
            if (token is null)
                return await FinishAsync(runId, Failed("O login do Xbox venceu. Conecte de novo em Configurações."));

            var identity = await CallAsync(c => xbox.AuthorizeAsync(token, c), throttle, ct);
            if (!identity.IsSuccess)
                return await FinishAsync(runId, Failed(identity.Error!.Message));
            var me = identity.Value!;
            await catalog.SetOwnerAsync(Platform.Xbox, me.Xuid, ct);
            await RememberGamertagAsync(me, ct);

            var language = XboxSyncPlanner.Language((await settings.LoadAsync(ct)).Language);
            progress?.Report(new SyncProgress(SyncStage.Library, 0, 0, null));
            var history = await CallAsync(c => xbox.GetTitleHistoryAsync(me, language, c), throttle, ct);
            if (!history.IsSuccess)
                return await FinishAsync(runId, Failed($"Não foi possível ler seus jogos do Xbox: {history.Error!.Message}"));

            var titles = history.Value!;
            var ids = await catalog.UpsertPlatformLibraryAsync(Platform.Xbox,
                titles.Select(t => new PlatformGame(t.TitleId, t.Name, t.ImageUrl, 0, t.LastPlayedUtc)).ToList(),
                time.GetUtcNow(), isCompleteLibrary: true, ct);

            var states = (await catalog.GetSyncStatesAsync(Platform.Xbox, ct)).ToDictionary(s => s.AppId);
            var unlocked = (await catalog.GetLibraryAsync(includeNotOwned: true, ct))
                .Where(g => g.Platform == Platform.Xbox).ToDictionary(g => g.AppId, g => g.Progress.Unlocked);
            var now = time.GetUtcNow();
            var plan = titles
                .Where(t => ids.ContainsKey(t.TitleId))
                .Select(t =>
                {
                    var id = ids[t.TitleId];
                    return (Title: t, Id: id, Work: XboxSyncPlanner.Plan(t, states.GetValueOrDefault(id), unlocked.GetValueOrDefault(id), language, now, _options.Policy, force));
                })
                .Where(p => p.Work != XboxGameWork.Skip)
                .ToList();

            var outcome = await SyncGamesAsync(me, language, plan, throttle, progress, ct);
            progress?.Report(new SyncProgress(SyncStage.Finished, plan.Count, plan.Count, null));
            return await FinishAsync(runId, outcome with { GamesInLibrary = titles.Count });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await FinishAsync(runId, new SyncResult(SyncOutcome.Cancelled, "Sincronização do Xbox cancelada. O que já tinha sido atualizado foi mantido."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FinishAsync(runId, Failed($"Erro interno: {ex.Message}"));
            throw;
        }
    }

    private async Task<SyncResult> SyncGamesAsync(XboxIdentity me, string language, IReadOnlyList<(XboxTitle Title, long Id, XboxGameWork Work)> plan,
        RequestThrottle throttle, IProgress<SyncProgress>? progress, CancellationToken ct)
    {
        int done = 0, failed = 0;
        foreach (var (title, id, work) in plan)
        {
            ct.ThrowIfCancellationRequested();
            var now = time.GetUtcNow();
            switch (work)
            {
                case XboxGameWork.NoAchievements:
                    await catalog.UpsertSchemaAsync(id, [], now, language, ct);
                    break;
                case XboxGameWork.Xbox360:
                    await catalog.SetAvailabilityAsync(id, DataAvailability.SchemaUnavailable, XboxSyncPlanner.Xbox360Message, ct);
                    break;
                case XboxGameWork.Achievements:
                    var result = await CallAsync(c => xbox.GetAchievementsAsync(me, title.TitleId, language, c), throttle, ct);
                    if (!result.IsSuccess)
                    {
                        // Token recusado ou limite que não passou: as próximas chamadas também falhariam.
                        if (result.Error!.Kind is XboxErrorKind.Unauthorized or XboxErrorKind.RateLimited)
                            return Failed($"{result.Error.Message} {done} jogos atualizados antes de parar; o resto fica para a próxima sincronização.")
                                with { GamesChecked = done, GamesFailed = failed + 1 };
                        failed++;
                        await catalog.SetAvailabilityAsync(id, DataAvailability.SyncFailed, result.Error.Message, CancellationToken.None);
                        break;
                    }
                    await SaveAchievementsAsync(id, result.Value!, language, now, ct);
                    break;
            }
            done++;
            progress?.Report(new SyncProgress(SyncStage.Games, done, plan.Count, title.Name));
        }

        if (failed > 0)
            return new SyncResult(SyncOutcome.PartiallySucceeded,
                $"{done - failed} de {done} jogos do Xbox atualizados. {failed} falharam e mantêm os dados anteriores.", GamesChecked: done, GamesFailed: failed);
        return new SyncResult(SyncOutcome.Succeeded, done == 0 ? "Xbox já estava atualizado." : $"{done} jogos do Xbox atualizados.", GamesChecked: done);
    }

    private async Task SaveAchievementsAsync(long id, IReadOnlyList<XboxAchievement> achievements, string language, DateTimeOffset now, CancellationToken ct)
    {
        if (achievements.Count == 0)
        {
            // O resumo diz que há conquistas, mas a lista veio vazia: não dá para afirmar "sem conquistas".
            await catalog.SetAvailabilityAsync(id, DataAvailability.SchemaUnavailable, "O Xbox não devolveu as conquistas deste jogo.", ct);
            return;
        }

        await catalog.UpsertSchemaAsync(id,
            achievements.Select(a => new AchievementDefinition(id, a.Id, a.Name, a.Description, a.IsSecret, a.IconUrl, null)).ToList(),
            now, language, ct);
        var rarity = achievements.Where(a => a.RarityPercent is not null).ToDictionary(a => a.Id, a => a.RarityPercent!.Value, StringComparer.Ordinal);
        if (rarity.Count > 0)
            await catalog.UpsertGlobalPercentagesAsync(id, rarity, now, ct);
        await catalog.ApplyPlayerStatesAsync(id,
            achievements.Select(a => new PlayerAchievementState(id, a.Id, a.IsUnlocked ? UnlockState.Unlocked : UnlockState.Locked, a.UnlockedUtc)).ToList(),
            now, ct);
    }

    private async Task RememberGamertagAsync(XboxIdentity me, CancellationToken ct)
    {
        var s = await settings.LoadAsync(ct);
        if (s.XboxGamertag != me.Gamertag)
            await settings.SaveAsync(s with { XboxGamertag = me.Gamertag }, ct);
    }

    private async Task<XboxResult<T>> CallAsync<T>(Func<CancellationToken, Task<XboxResult<T>>> call, RequestThrottle throttle, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await throttle.WaitAsync(ct);
            var result = await call(ct);

            if (result.Error is { Kind: XboxErrorKind.Cancelled })
                ct.ThrowIfCancellationRequested();
            if (result.IsSuccess || !result.Error!.IsRetryable || attempt >= _options.MaxAttempts)
                return result;

            if (result.Error.Kind == XboxErrorKind.RateLimited)
                throttle.PauseFor(result.Error.RetryAfter ?? TimeSpan.FromMinutes(1));
            else
                await _delay(_options.BaseBackoff * Math.Pow(2, attempt - 1), ct);
        }
    }

    private static SyncResult Failed(string message) => new(SyncOutcome.Failed, message);

    private async Task<SyncResult> FinishAsync(long runId, SyncResult result)
    {
        var status = result.Outcome switch
        {
            SyncOutcome.Succeeded or SyncOutcome.PartiallySucceeded => SyncStatus.Succeeded,
            SyncOutcome.Cancelled => SyncStatus.Cancelled,
            _ => SyncStatus.Failed,
        };
        await runs.FinishAsync(runId, status, time.GetUtcNow(), null, result.Message, CancellationToken.None);
        return result;
    }
}
