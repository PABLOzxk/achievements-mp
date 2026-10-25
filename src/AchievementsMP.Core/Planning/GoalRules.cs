using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Progress;

namespace AchievementsMP.Core.Planning;

public enum DeadlineState { None, OnTrack, DueSoon, Overdue }

/// <summary>Dados da Steam que a meta precisa. Null = não se sabe (nunca é tratado como zero).</summary>
public sealed record GoalFacts(
    GameProgress? GameProgress = null,
    UnlockState? AchievementState = null,
    DateTimeOffset? AchievementUnlockedUtc = null,
    int? UnlocksInWindow = null);

/// <summary>Situação calculada de uma meta.</summary>
/// <param name="IsKnown">false quando faltam dados da Steam para dizer se a meta foi cumprida.</param>
public sealed record GoalProgress(int? Current, int? Target, bool IsMet, bool IsKnown, DeadlineState Deadline)
{
    public double? Percent => Current is { } c && Target is > 0 and var t ? Math.Min(100.0, 100.0 * c / t) : null;
}

public static class GoalRules
{
    /// <summary>Faltando até isto para o prazo, a meta aparece como "vence em breve".</summary>
    public static readonly TimeSpan DueSoon = TimeSpan.FromDays(3);

    /// <summary>
    /// Intervalo [início, fim) em que desbloqueios contam para uma meta de quantidade.
    /// Sem período: da criação da meta até o prazo (ou sem fim). Diária/semanal: o dia ou a semana atual no fuso local.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset? End) CountingWindow(Goal goal, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (goal.Period == GoalPeriod.None)
            return (goal.CreatedUtc, goal.DeadlineUtc);

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var startDay = goal.Period == GoalPeriod.Weekly
            ? local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7)) // segunda-feira
            : local.Date;
        var start = AtLocalMidnight(startDay, zone);
        var end = AtLocalMidnight(startDay.AddDays(goal.Period == GoalPeriod.Weekly ? 7 : 1), zone);
        return (start, end);
    }

    public static GoalProgress Evaluate(Goal goal, GoalFacts facts, DateTimeOffset now)
    {
        (int? Current, int? Target, bool Met, bool Known) r = goal.Kind switch
        {
            GoalKind.CompleteGame => FromGame(facts.GameProgress),
            GoalKind.UnlockAchievement => facts.AchievementState switch
            {
                UnlockState.Unlocked => (1, 1, true, true),
                UnlockState.Locked => (0, 1, false, true),
                _ => (null, 1, false, false),
            },
            GoalKind.UnlockCount => facts.UnlocksInWindow is { } n && goal.Target is > 0 and var t
                ? (n, t, n >= t, true)
                : (null, goal.Target, false, false),
            _ => (null, null, goal.Status == GoalStatus.Completed, true),
        };

        // Uma meta já concluída continua concluída, mesmo que a Steam mude depois (ex.: DLC com conquistas novas).
        var met = r.Met || goal.Status == GoalStatus.Completed;

        return new GoalProgress(r.Current, r.Target, met, r.Known, DeadlineOf(goal, met, now));
    }

    private static (int? Current, int? Target, bool Met, bool Known) FromGame(GameProgress? p) => p switch
    {
        null or { Total: 0 } => (null, null, false, false),
        { IsComplete: true } => (p.Total, p.Total, true, true),
        // Conquistas sem dados: mostra o que se sabe, mas não afirma nada.
        _ => (p.Unlocked, p.Total, false, p.IsExact),
    };

    private static DeadlineState DeadlineOf(Goal goal, bool met, DateTimeOffset now)
    {
        if (met || goal.Status != GoalStatus.Active || goal.IsRecurring || goal.DeadlineUtc is not { } deadline)
            return DeadlineState.None;
        if (now >= deadline) return DeadlineState.Overdue;
        return deadline - now <= DueSoon ? DeadlineState.DueSoon : DeadlineState.OnTrack;
    }

    private static DateTimeOffset AtLocalMidnight(DateTime day, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        // Meia-noite que não existe (horário de verão começando à 0h): usa a primeira hora válida do dia.
        while (zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddMinutes(30);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    /// <summary>Título sugerido quando o usuário não escreve um.</summary>
    public static string DefaultTitle(GoalKind kind, string? gameName, string? achievementName, int? target, GoalPeriod period, DateTimeOffset? deadlineLocal)
    {
        var until = deadlineLocal is { } d ? $" até {d:dd/MM}" : "";
        var inGame = gameName is null ? "" : $" em {gameName}";
        return kind switch
        {
            GoalKind.CompleteGame => $"100%{inGame}{until}",
            GoalKind.UnlockAchievement => $"Desbloquear “{achievementName}”{inGame}{until}",
            GoalKind.UnlockCount => period switch
            {
                GoalPeriod.Daily => $"{target} {Plural(target, "conquista", "conquistas")} por dia{inGame}",
                GoalPeriod.Weekly => $"{target} {Plural(target, "conquista", "conquistas")} por semana{inGame}",
                _ => $"{target} {Plural(target, "conquista", "conquistas")}{inGame}{until}",
            },
            _ => "Meta",
        };
    }

    private static string Plural(int? n, string one, string many) => n == 1 ? one : many;
}
