namespace AchievementsMP.Core.Domain;

// Dados criados pelo usuário. Ficam em tabelas próprias e a sincronização nunca escreve nelas.

public sealed record UserList(long Id, string Name, string? Description, bool IsFavorites, DateTimeOffset CreatedUtc);

public enum GoalStatus { Active = 0, Completed = 1, Abandoned = 2 }

public enum Priority { Low = 0, Normal = 1, High = 2 }

public sealed record Goal(
    long Id,
    string Title,
    long? AppId,
    DateTimeOffset? DeadlineUtc,
    Priority Priority,
    GoalStatus Status,
    DateTimeOffset CreatedUtc);

public sealed record PlanTask(
    long Id,
    long AppId,
    string? ApiName,
    string Title,
    string? Notes,
    int SortOrder,
    DateTimeOffset? DoneUtc);

public sealed record Guide(
    long Id,
    long AppId,
    string? ApiName,
    Uri Url,
    string? Language,
    string? Source);
