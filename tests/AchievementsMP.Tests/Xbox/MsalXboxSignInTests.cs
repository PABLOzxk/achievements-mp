using AchievementsMP.Core.Settings;
using AchievementsMP.Xbox;

namespace AchievementsMP.Tests.Xbox;

public class MsalXboxSignInTests
{
    private sealed class MemoryCache : ITokenCacheStore
    {
        public byte[]? Data { get; set; }
        public int Loads { get; private set; }
        public Task<byte[]?> LoadAsync(CancellationToken ct = default) { Loads++; return Task.FromResult(Data); }
        public Task SaveAsync(byte[]? data, CancellationToken ct = default) { Data = data; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Without_client_id_xbox_is_unavailable_and_never_signs_in()
    {
        var signIn = new MsalXboxSignIn("", new MemoryCache());

        Assert.False(signIn.IsAvailable);
        Assert.False(await signIn.HasAccountAsync());
        Assert.Null(await signIn.GetAccessTokenAsync());
        await Assert.ThrowsAsync<XboxSignInException>(() => signIn.SignInAsync());
    }

    [Fact]
    public async Task With_client_id_and_empty_cache_there_is_no_account_and_no_network_call()
    {
        var cache = new MemoryCache();
        var signIn = new MsalXboxSignIn("00000000-0000-0000-0000-000000000001", cache);

        Assert.True(signIn.IsAvailable);
        Assert.False(await signIn.HasAccountAsync());
        Assert.Null(await signIn.GetAccessTokenAsync());
        Assert.True(cache.Loads > 0);
    }

    [Fact]
    public async Task Sign_out_clears_the_stored_login()
    {
        var cache = new MemoryCache { Data = [1, 2, 3] };
        var signIn = new MsalXboxSignIn("", cache);

        await signIn.SignOutAsync();

        Assert.Null(cache.Data);
    }
}
