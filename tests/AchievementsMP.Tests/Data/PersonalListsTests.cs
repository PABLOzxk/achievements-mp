using AchievementsMP.Data.Repositories;

namespace AchievementsMP.Tests.Data;

public class PersonalListsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Favorites_list_is_created_once()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PersonalDataRepository(db.Factory);

        var a = await repo.GetOrCreateFavoritesAsync(T0);
        var b = await repo.GetOrCreateFavoritesAsync(T0);

        Assert.Equal(a, b);
        Assert.Single(await repo.GetListsAsync(), l => l.IsFavorites);
    }

    [Fact]
    public async Task Membership_add_remove_and_delete_list()
    {
        await using var db = await TempDatabase.CreateMigratedAsync();
        var repo = new PersonalDataRepository(db.Factory);
        var platinar = await repo.CreateListAsync("Platinar", null, T0);
        var depois = await repo.CreateListAsync("Depois", null, T0);
        await repo.AddGameToListAsync(platinar, 620, T0);
        await repo.AddGameToListAsync(platinar, 620, T0); // repetido não duplica
        await repo.AddGameToListAsync(depois, 620, T0);
        await repo.AddGameToListAsync(depois, 440, T0);

        var membership = await repo.GetMembershipAsync();
        Assert.Equal(new[] { platinar, depois }.Order(), membership[620].Order());

        await repo.RemoveGameFromListAsync(depois, 620);
        await repo.DeleteListAsync(platinar);

        membership = await repo.GetMembershipAsync();
        Assert.Empty(membership[620]);
        Assert.Equal([depois], membership[440]);
        Assert.Equal(["Depois"], (await repo.GetListsAsync()).Select(l => l.Name));
    }
}
