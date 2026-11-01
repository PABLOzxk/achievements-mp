using AchievementsMP.Core.Settings;
using Microsoft.Identity.Client;

namespace AchievementsMP.Xbox;

/// <summary>Registro do app na Microsoft (Entra), feito uma vez pelo dono do projeto.</summary>
public static class XboxApp
{
    /// <summary>
    /// "ID do aplicativo (cliente)" do registro. Não é segredo: app de desktop não tem segredo, e o ID aparece na
    /// tela de login da Microsoft. Vazio = conexão com o Xbox indisponível nesta versão.
    /// </summary>
    public const string ClientId = "";

    /// <summary>Permissão de login do Xbox Live. A Microsoft devolve também o token de renovação (offline_access).</summary>
    public static readonly string[] Scopes = ["XboxLive.signin"];
}

public sealed class XboxSignInException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Login da Microsoft para o Xbox. O app só pede para ler: perfil, jogos e conquistas.</summary>
public interface IXboxSignIn
{
    /// <summary>Esta versão do app tem o registro na Microsoft e pode conectar o Xbox.</summary>
    bool IsAvailable { get; }

    /// <summary>Há um login guardado (pode ter vencido; só a próxima sincronização confirma).</summary>
    Task<bool> HasAccountAsync(CancellationToken ct = default);

    /// <summary>Token da Microsoft sem abrir nada. Null = não conectado, ou o login venceu e precisa conectar de novo.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>Abre o navegador no login da Microsoft e espera o usuário autorizar.</summary>
    Task<string> SignInAsync(CancellationToken ct = default);

    /// <summary>Apaga o login guardado neste computador.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>
/// Login com a biblioteca oficial da Microsoft (MSAL): abre o navegador padrão, recebe a resposta num endereço local
/// (http://localhost) e renova o token sozinha. A senha nunca passa pelo app.
/// </summary>
public sealed class MsalXboxSignIn : IXboxSignIn
{
    private const string BrowserDone = """
        <html><head><meta charset="utf-8"><title>Achievements MP</title></head>
        <body style="font-family:Segoe UI,sans-serif;background:#10161d;color:#e5eef5;padding:48px">
        <h2>Xbox conectado</h2><p>Pode fechar esta aba e voltar para o Achievements MP.</p></body></html>
        """;
    private const string BrowserFailed = """
        <html><head><meta charset="utf-8"><title>Achievements MP</title></head>
        <body style="font-family:Segoe UI,sans-serif;background:#10161d;color:#e5eef5;padding:48px">
        <h2>Não deu para conectar</h2><p>Volte para o Achievements MP e tente de novo.</p></body></html>
        """;

    private readonly IPublicClientApplication? _app;
    private readonly ITokenCacheStore _store;

    public MsalXboxSignIn(string clientId, ITokenCacheStore store)
    {
        _store = store;
        if (string.IsNullOrWhiteSpace(clientId)) return;

        _app = PublicClientApplicationBuilder.Create(clientId)
            .WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount)
            .WithRedirectUri("http://localhost")
            .Build();
        _app.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            var data = await _store.LoadAsync();
            args.TokenCache.DeserializeMsalV3(data, shouldClearExistingCache: true);
        });
        _app.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (args.HasStateChanged)
                await _store.SaveAsync(args.TokenCache.SerializeMsalV3());
        });
    }

    public bool IsAvailable => _app is not null;

    public async Task<bool> HasAccountAsync(CancellationToken ct = default) =>
        _app is not null && (await _app.GetAccountsAsync()).Any();

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_app is null) return null;
        var account = (await _app.GetAccountsAsync()).FirstOrDefault();
        if (account is null) return null;
        try
        {
            return (await _app.AcquireTokenSilent(XboxApp.Scopes, account).ExecuteAsync(ct)).AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            return null;
        }
    }

    public async Task<string> SignInAsync(CancellationToken ct = default)
    {
        if (_app is null)
            throw new XboxSignInException("Esta versão do app ainda não tem a conexão com o Xbox.");
        try
        {
            var result = await _app.AcquireTokenInteractive(XboxApp.Scopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .WithSystemWebViewOptions(new SystemWebViewOptions { HtmlMessageSuccess = BrowserDone, HtmlMessageError = BrowserFailed })
                .ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            throw new XboxSignInException("Login cancelado.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new XboxSignInException("O login demorou demais e foi cancelado. Tente de novo.", ex);
        }
        catch (MsalException ex)
        {
            throw new XboxSignInException($"A Microsoft recusou o login ({ex.ErrorCode}).", ex);
        }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        if (_app is not null)
        {
            foreach (var account in await _app.GetAccountsAsync())
                await _app.RemoveAsync(account);
        }
        await _store.SaveAsync(null, ct);
    }
}
