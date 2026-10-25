namespace AchievementsMP.Core.Domain;

// Dados criados pelo usuário. Ficam em tabelas próprias e a sincronização nunca escreve nelas.

public sealed record UserList(long Id, string Name, string? Description, bool IsFavorites, DateTimeOffset CreatedUtc);

public enum GoalStatus { Active = 0, Completed = 1, Abandoned = 2 }

public enum Priority { Low = 0, Normal = 1, High = 2 }

/// <summary>O que a meta mede. Só <see cref="Custom"/> é marcada à mão; as outras são calculadas com os dados da Steam.</summary>
public enum GoalKind { Custom = 0, CompleteGame = 1, UnlockAchievement = 2, UnlockCount = 3 }

/// <summary>Metas recorrentes recomeçam a contagem a cada dia ou semana (segunda a domingo, horário local).</summary>
public enum GoalPeriod { None = 0, Daily = 1, Weekly = 2 }

public sealed record Goal(
    long Id,
    string Title,
    GoalKind Kind,
    long? AppId,
    string? ApiName,
    int? Target,
    GoalPeriod Period,
    DateTimeOffset? DeadlineUtc,
    Priority Priority,
    GoalStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? DoneUtc)
{
    public bool IsRecurring => Kind == GoalKind.UnlockCount && Period != GoalPeriod.None;
}

/// <summary>Dados de uma meta nova, antes de ir para o banco.</summary>
public sealed record GoalDraft(
    string Title,
    GoalKind Kind,
    long? AppId = null,
    string? ApiName = null,
    int? Target = null,
    GoalPeriod Period = GoalPeriod.None,
    DateTimeOffset? DeadlineUtc = null,
    Priority Priority = Priority.Normal);

public sealed record PlanTask(
    long Id,
    long AppId,
    string? ApiName,
    string Title,
    string? Notes,
    int SortOrder,
    DateTimeOffset? DoneUtc)
{
    public bool IsDone => DoneUtc is not null;
}

public sealed record Guide(
    long Id,
    long AppId,
    string? ApiName,
    Uri Url,
    string? Title,
    string? Language,
    string? Source);
