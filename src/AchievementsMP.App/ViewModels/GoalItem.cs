using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Planning;
using AchievementsMP.Data.Planning;

namespace AchievementsMP.App.ViewModels;

/// <summary>Uma meta pronta para exibir (lista de metas e visão geral).</summary>
public sealed record GoalItem(
    long Id,
    long? AppId,
    string Title,
    string Subtitle,
    string ProgressText,
    double Percent,
    bool ShowBar,
    bool IsMet,
    string? DeadlineText,
    DeadlineState Deadline,
    string? PriorityLabel,
    GoalStatus Status,
    string? StatusText,
    bool IsCustom,
    bool IsActive)
{
    public bool CanComplete => IsActive && IsCustom;
    public bool CanAbandon => IsActive;
    public bool CanReopen => !IsActive;
    public bool HasGame => AppId is not null;

    public static GoalItem From(GoalView v, DateTimeOffset now, TimeZoneInfo zone)
    {
        var g = v.Goal;
        var p = v.Progress;
        var game = v.Game?.Name;

        var subtitle = g.Kind switch
        {
            GoalKind.CompleteGame => $"100% · {game ?? "jogo removido"}",
            GoalKind.UnlockAchievement => $"Conquista “{v.AchievementName ?? g.ApiName}” · {game ?? "jogo removido"}",
            GoalKind.UnlockCount => (game is null ? "Em qualquer jogo" : $"Em {game}") + g.Period switch
            {
                GoalPeriod.Daily => " · recomeça todo dia",
                GoalPeriod.Weekly => " · recomeça toda segunda-feira",
                _ => $" · contando desde {Local(g.CreatedUtc, zone):dd/MM/yyyy}",
            },
            _ => "Meta livre: marque quando concluir",
        };

        var progress = (g.Kind, p) switch
        {
            (GoalKind.Custom, _) => g.Status == GoalStatus.Completed ? "Concluída" : "Em andamento",
            (_, { IsKnown: false, Current: { } c, Target: { } t }) => $"{c} de {t} · faltam dados da Steam de algumas conquistas",
            (_, { IsKnown: false }) => "Sem dados da Steam ainda. Sincronize para acompanhar.",
            (GoalKind.CompleteGame, { IsMet: true }) => "Completo",
            (GoalKind.CompleteGame, { Current: { } c, Target: { } t }) => $"{c} de {t} · faltam {t - c}",
            (GoalKind.UnlockAchievement, { IsMet: true }) => "Desbloqueada",
            (GoalKind.UnlockAchievement, _) => "Pendente",
            (GoalKind.UnlockCount, { Current: { } c, Target: { } t }) => g.Period switch
            {
                GoalPeriod.Daily => $"{c} de {t} hoje" + (p.IsMet ? " · cumprida" : ""),
                GoalPeriod.Weekly => $"{c} de {t} nesta semana" + (p.IsMet ? " · cumprida" : ""),
                _ => $"{c} de {t}",
            },
            _ => "",
        };

        var isActive = g.Status == GoalStatus.Active;
        string? statusText = g.Status switch
        {
            GoalStatus.Completed => g.DoneUtc is { } d ? $"Concluída em {Local(d, zone):dd/MM/yyyy}" : "Concluída",
            GoalStatus.Abandoned => "Abandonada",
            _ => null,
        };

        return new GoalItem(
            g.Id, g.AppId, g.Title, subtitle, progress, p.Percent ?? (p.IsMet ? 100 : 0), g.Kind != GoalKind.Custom && p.Target is > 0,
            p.IsMet, isActive ? DeadlineTextOf(g, p.Deadline, now, zone) : null, p.Deadline,
            g.Priority switch { Priority.High => "Alta prioridade", Priority.Low => "Baixa prioridade", _ => null },
            g.Status, statusText, g.Kind == GoalKind.Custom, isActive);
    }

    /// <summary>O prazo é guardado como o fim do dia escolhido; o dia exibido é o anterior a esse instante.</summary>
    public static DateTime DeadlineDay(DateTimeOffset deadline, TimeZoneInfo zone) => Local(deadline.AddSeconds(-1), zone).Date;

    private static string? DeadlineTextOf(Goal g, DeadlineState state, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (g.IsRecurring || g.DeadlineUtc is not { } deadline) return null;
        var day = DeadlineDay(deadline, zone);
        var days = (day - Local(now, zone).Date).Days;
        return state switch
        {
            DeadlineState.Overdue => $"Atrasada · o prazo era {day:dd/MM/yyyy}",
            DeadlineState.DueSoon or DeadlineState.OnTrack when days <= 0 => "Vence hoje",
            DeadlineState.DueSoon or DeadlineState.OnTrack when days == 1 => "Vence amanhã",
            DeadlineState.DueSoon => $"Vence em {days} dias",
            _ => $"Prazo {day:dd/MM/yyyy} · {days} dias",
        };
    }

    private static DateTime Local(DateTimeOffset value, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(value, zone).DateTime;

    /// <summary>Ativas primeiro as atrasadas, depois as que vencem logo, depois por prioridade e prazo.</summary>
    public static IEnumerable<GoalView> OrderActive(IEnumerable<GoalView> goals) => goals
        .Where(v => v.Goal.Status == GoalStatus.Active)
        .OrderBy(v => v.Progress.Deadline switch { DeadlineState.Overdue => 0, DeadlineState.DueSoon => 1, _ => 2 })
        .ThenByDescending(v => v.Goal.Priority)
        .ThenBy(v => v.Goal.DeadlineUtc ?? DateTimeOffset.MaxValue)
        .ThenBy(v => v.Goal.Id);
}
