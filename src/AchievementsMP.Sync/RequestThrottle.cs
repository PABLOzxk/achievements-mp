namespace AchievementsMP.Sync;

/// <summary>
/// Espaça as chamadas à Steam e aplica pausas globais quando ela pede (429).
/// Uma pausa vale para todas as threads: não adianta uma esperar enquanto as outras continuam batendo.
/// </summary>
internal sealed class RequestThrottle(TimeSpan spacing, TimeProvider time, Func<TimeSpan, CancellationToken, Task> delay)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _pauseLock = new();
    private DateTimeOffset _next = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                DateTimeOffset target;
                lock (_pauseLock)
                    target = _next > _pausedUntil ? _next : _pausedUntil;
                var now = time.GetUtcNow();
                if (target <= now) break;
                await delay(target - now, ct);
            }
            _next = time.GetUtcNow() + spacing;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void PauseFor(TimeSpan duration)
    {
        lock (_pauseLock)
        {
            var until = time.GetUtcNow() + duration;
            if (until > _pausedUntil) _pausedUntil = until;
        }
    }
}
