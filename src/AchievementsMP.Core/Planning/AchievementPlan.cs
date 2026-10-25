using AchievementsMP.Core.Domain;

namespace AchievementsMP.Core.Planning;

/// <summary>Quanto esforço o usuário acha que a conquista exige. A Steam não informa isso.</summary>
public enum Effort { Unknown = 0, Quick = 1, Long = 2 }

/// <summary>Se a conquista pode ser perdida (decisão ou momento específico da campanha). Padrão: não se sabe.</summary>
public enum Missable { Unknown = 0, Yes = 1, No = 2 }

/// <summary>
/// Classificação pessoal de uma conquista. Tudo começa como desconhecido: o programa nunca afirma que uma conquista
/// é perdível, rápida ou demorada sem o usuário dizer (e, de preferência, anotar a fonte).
/// </summary>
public sealed record AchievementPlan(
    long AppId,
    string ApiName,
    Effort Effort = Effort.Unknown,
    Missable Missable = Missable.Unknown,
    Priority Priority = Priority.Normal,
    string? Requirement = null,
    string? Source = null,
    string? Note = null)
{
    public static AchievementPlan Empty(long appId, string apiName) => new(appId, apiName);

    /// <summary>Nada preenchido: equivale a não ter classificação.</summary>
    public bool IsEmpty =>
        Effort == Effort.Unknown && Missable == Missable.Unknown && Priority == Priority.Normal
        && string.IsNullOrWhiteSpace(Requirement) && string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Note);

    /// <summary>Tem alguma classificação que muda a ordem do plano.</summary>
    public bool IsClassified => Effort != Effort.Unknown || Missable != Missable.Unknown;

    /// <summary>Remove espaços nas pontas e troca texto vazio por null.</summary>
    public AchievementPlan Normalized() => this with
    {
        Requirement = Clean(Requirement),
        Source = Clean(Source),
        Note = Clean(Note),
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
