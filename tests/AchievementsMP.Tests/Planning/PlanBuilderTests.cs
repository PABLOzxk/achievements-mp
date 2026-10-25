using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Planning;

namespace AchievementsMP.Tests.Planning;

public class PlanBuilderTests
{
    private const long App = 1145360;

    private static AchievementDetail A(string api, double? percent, UnlockState state = UnlockState.Locked) =>
        new(App, api, api, null, false, null, null, state, null, percent);

    private static Dictionary<string, AchievementPlan> Plans(params AchievementPlan[] plans) =>
        plans.ToDictionary(p => p.ApiName, StringComparer.Ordinal);

    [Fact]
    public void Recommended_order_is_missable_then_quick_then_unclassified_then_long()
    {
        AchievementDetail[] all = [A("longa", 60), A("semclasse", 50), A("rapida", 5), A("perdivel", 2)];
        var plans = Plans(
            new AchievementPlan(App, "longa", Effort.Long),
            new AchievementPlan(App, "rapida", Effort.Quick),
            new AchievementPlan(App, "perdivel", Effort.Long, Missable.Yes));

        var plan = PlanBuilder.Build(all, plans, PlanStrategy.Recommended);

        Assert.Equal(["perdivel", "rapida", "semclasse", "longa"], plan.Entries.Select(e => e.Achievement.ApiName));
        Assert.Equal([PlanGroup.Missable, PlanGroup.Quick, PlanGroup.Unclassified, PlanGroup.Long], plan.Entries.Select(e => e.Group));
        Assert.Equal(1, plan.Unclassified);
        Assert.Equal(1, plan.MissablePending);
    }

    [Fact]
    public void Quick_first_puts_quick_before_missable()
    {
        AchievementDetail[] all = [A("perdivel", 50), A("rapida", 10)];
        var plans = Plans(new AchievementPlan(App, "rapida", Effort.Quick), new AchievementPlan(App, "perdivel", Missable: Missable.Yes));

        var plan = PlanBuilder.Build(all, plans, PlanStrategy.QuickFirst);

        Assert.Equal(["rapida", "perdivel"], plan.Entries.Select(e => e.Achievement.ApiName));
    }

    [Fact]
    public void Within_a_group_high_priority_comes_first_then_most_common()
    {
        AchievementDetail[] all = [A("comum", 80), A("rara", 3), A("semdados", null), A("urgente", 1)];
        var plans = Plans(new AchievementPlan(App, "urgente", Priority: Priority.High));

        var plan = PlanBuilder.Build(all, plans, PlanStrategy.Recommended);

        Assert.Equal(["urgente", "comum", "rara", "semdados"], plan.Entries.Select(e => e.Achievement.ApiName));
    }

    [Fact]
    public void Rarity_only_ignores_classification_and_priority()
    {
        AchievementDetail[] all = [A("rara", 3), A("comum", 80)];
        var plans = Plans(new AchievementPlan(App, "rara", Effort.Quick, Missable.Yes, Priority.High));

        var plan = PlanBuilder.Build(all, plans, PlanStrategy.RarityOnly);

        Assert.Equal(["comum", "rara"], plan.Entries.Select(e => e.Achievement.ApiName));
        Assert.All(plan.Entries, e => Assert.Equal(PlanGroup.All, e.Group));
    }

    [Fact]
    public void Unlocked_and_unknown_are_never_in_the_plan()
    {
        AchievementDetail[] all = [A("feita", 90, UnlockState.Unlocked), A("semdados", 50, UnlockState.Unknown), A("falta", 10)];

        var plan = PlanBuilder.Build(all, Plans(), PlanStrategy.Recommended);

        Assert.Equal(["falta"], plan.Entries.Select(e => e.Achievement.ApiName));
        Assert.Equal(1, plan.Unlocked);
        Assert.Equal(1, plan.Unknown);
    }

    [Fact]
    public void Missable_marked_no_is_classified_but_stays_in_its_effort_group()
    {
        var plans = Plans(new AchievementPlan(App, "x", Missable: Missable.No));

        var plan = PlanBuilder.Build([A("x", 10)], plans, PlanStrategy.Recommended);

        Assert.Equal(PlanGroup.Unclassified, plan.Entries[0].Group);
        Assert.Equal(0, plan.Unclassified);
    }
}
