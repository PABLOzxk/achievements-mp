using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;

namespace AchievementsMP.Core.Planning;

/// <summary>Como ordenar o que falta. Configurável pelo usuário.</summary>
public enum PlanStrategy
{
    /// <summary>Perdíveis → rápidas → sem classificação → demoradas.</summary>
    Recommended = 0,

    /// <summary>Rápidas → perdíveis → sem classificação → demoradas.</summary>
    QuickFirst = 1,

    /// <summary>Ignora a classificação: só a raridade global, das mais comuns para as mais raras.</summary>
    RarityOnly = 2,
}

public enum PlanGroup { Missable, Quick, Unclassified, Long, All }

public sealed record PlanEntry(AchievementDetail Achievement, AchievementPlan Plan, PlanGroup Group);

/// <summary>
/// Plano de um jogo: só conquistas sabidamente pendentes. As sem dados da Steam ficam de fora e são contadas à parte
/// (dado ausente nunca vira pendente).
/// </summary>
public sealed record GamePlan(IReadOnlyList<PlanEntry> Entries, int Unlocked, int Unknown)
{
    public int Pending => Entries.Count;
    public int Unclassified => Entries.Count(e => !e.Plan.IsClassified);
    public int MissablePending => Entries.Count(e => e.Plan.Missable == Missable.Yes);
}

public static class PlanBuilder
{
    public static GamePlan Build(
        IEnumerable<AchievementDetail> achievements,
        IReadOnlyDictionary<string, AchievementPlan> plans,
        PlanStrategy strategy)
    {
        var list = achievements.ToList();
        var order = GroupOrder(strategy);
        var byName = StringComparer.CurrentCultureIgnoreCase;
        var ignoreClassification = strategy == PlanStrategy.RarityOnly;

        var entries = list
            .Where(a => a.State == UnlockState.Locked)
            .Select(a =>
            {
                var plan = plans.TryGetValue(a.ApiName, out var p) ? p : AchievementPlan.Empty(a.AppId, a.ApiName);
                return new PlanEntry(a, plan, GroupOf(plan, strategy));
            })
            .OrderBy(e => Rank(order, e.Group))
            .ThenByDescending(e => ignoreClassification ? Priority.Normal : e.Plan.Priority)
            .ThenByDescending(e => e.Achievement.GlobalPercent ?? -1)
            .ThenBy(e => e.Achievement.DisplayName, byName)
            .ThenBy(e => e.Achievement.ApiName, StringComparer.Ordinal)
            .ToList();

        return new GamePlan(
            entries,
            list.Count(a => a.State == UnlockState.Unlocked),
            list.Count(a => a.State == UnlockState.Unknown));
    }

    public static IReadOnlyList<PlanGroup> GroupOrder(PlanStrategy strategy) => strategy switch
    {
        PlanStrategy.QuickFirst => [PlanGroup.Quick, PlanGroup.Missable, PlanGroup.Unclassified, PlanGroup.Long],
        PlanStrategy.RarityOnly => [PlanGroup.All],
        _ => [PlanGroup.Missable, PlanGroup.Quick, PlanGroup.Unclassified, PlanGroup.Long],
    };

    public static PlanGroup GroupOf(AchievementPlan plan, PlanStrategy strategy)
    {
        if (strategy == PlanStrategy.RarityOnly) return PlanGroup.All;
        // Perdível vence o esforço: uma perdível demorada ainda precisa ser feita antes do ponto sem volta.
        if (plan.Missable == Missable.Yes) return PlanGroup.Missable;
        return plan.Effort switch
        {
            Effort.Quick => PlanGroup.Quick,
            Effort.Long => PlanGroup.Long,
            _ => PlanGroup.Unclassified,
        };
    }

    private static int Rank(IReadOnlyList<PlanGroup> order, PlanGroup group)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] == group) return i;
        return order.Count;
    }
}
