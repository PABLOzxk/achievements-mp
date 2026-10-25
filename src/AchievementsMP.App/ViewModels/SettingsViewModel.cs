using System.Diagnostics;
using AchievementsMP.App.Services;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public sealed record LanguageOption(string Code, string Label)
{
    // A caixa fechada do ComboBox exibe ToString(); sem isto aparece "LanguageOption { Code = … }".
    public override string ToString() => Label;
}

public sealed partial class SettingsViewModel(ISettingsStore store, ISecretStore secrets, ISteamWebApi steam, AppPaths paths) : PageViewModel
{
    public override string Title => "Configurações";
    public override string Glyph => "";

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("brazilian", "Português (Brasil)"),
        new("english", "English"),
        new("spanish", "Español"),
        new("latam", "Español (Latinoamérica)"),
    ];

    public string DataFolder => paths.Root;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileHint))]
    private string _steamProfile = "";

    [ObservableProperty] private string _language = AppSettings.Default.Language;
    [ObservableProperty] private bool _syncOnStartup = AppSettings.Default.SyncOnStartup;
    [ObservableProperty] private string _syncIntervalHours = (AppSettings.Default.SyncIntervalMinutes / 60).ToString();

    /// <summary>Chave digitada agora (vinda do PasswordBox). Vazia = manter a salva.</summary>
    [ObservableProperty] private string _newApiKey = "";
    [ObservableProperty] private string _apiKeyStatus = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isBusy;

    private ulong? _resolvedSteamId;

    public string ProfileHint => SteamIdentity.Parse(SteamProfile) switch
    {
        SteamProfileInput.SteamId id => $"SteamID64 {id.Value}",
        SteamProfileInput.Vanity v => $"Nome personalizado “{v.Name}” — será convertido em SteamID64 ao testar",
        _ when string.IsNullOrWhiteSpace(SteamProfile) => "Cole o link do seu perfil, o SteamID64 ou o nome personalizado.",
        _ => "Não reconheci esse formato.",
    };

    public override async Task OnNavigatedToAsync()
    {
        var s = await store.LoadAsync();
        SteamProfile = s.SteamProfile ?? "";
        _resolvedSteamId = s.SteamId64;
        Language = Languages.Any(l => l.Code == s.Language) ? s.Language : AppSettings.Default.Language;
        SyncOnStartup = s.SyncOnStartup;
        SyncIntervalHours = Math.Max(1, s.SyncIntervalMinutes / 60).ToString();
        await RefreshKeyStatusAsync();
        StatusMessage = "";
    }

    [RelayCommand]
    private Task SaveAsync() => TrySaveAsync();

    private async Task<bool> TrySaveAsync()
    {
        if (!int.TryParse(SyncIntervalHours, out var hours) || hours is < 1 or > 168)
        {
            StatusMessage = "O intervalo deve ser entre 1 e 168 horas.";
            return false;
        }

        var input = SteamIdentity.Parse(SteamProfile);
        if (!string.IsNullOrWhiteSpace(SteamProfile) && input is null)
        {
            StatusMessage = "Perfil Steam em formato não reconhecido.";
            return false;
        }

        var previous = await store.LoadAsync();
        var steamId = input switch
        {
            SteamProfileInput.SteamId id => id.Value,
            // Mesmo nome de antes: mantém o ID já resolvido.
            SteamProfileInput.Vanity when previous.SteamProfile == SteamProfile.Trim() => _resolvedSteamId,
            _ => null,
        };

        await store.SaveAsync(new AppSettings
        {
            SteamProfile = string.IsNullOrWhiteSpace(SteamProfile) ? null : SteamProfile.Trim(),
            SteamId64 = steamId,
            Language = Language,
            SyncOnStartup = SyncOnStartup,
            SyncIntervalMinutes = hours * 60,
        });
        _resolvedSteamId = steamId;

        if (!string.IsNullOrWhiteSpace(NewApiKey))
        {
            await secrets.SetSteamApiKeyAsync(NewApiKey);
            NewApiKey = "";
        }

        await RefreshKeyStatusAsync();
        StatusMessage = "Configurações salvas.";
        FileLog.Info("Configurações salvas.");
        return true;
    }

    [RelayCommand]
    private async Task RemoveApiKeyAsync()
    {
        await secrets.SetSteamApiKeyAsync(null);
        NewApiKey = "";
        await RefreshKeyStatusAsync();
        StatusMessage = "Chave removida deste computador.";
    }

    /// <summary>Salva e confirma com a Steam que chave e perfil funcionam e que o perfil é público.</summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsBusy = true;
        try
        {
            if (!await TrySaveAsync()) return;
            StatusMessage = "Consultando a Steam…";

            var input = SteamIdentity.Parse(SteamProfile);
            ulong steamId;
            switch (input)
            {
                case SteamProfileInput.SteamId id:
                    steamId = id.Value;
                    break;
                case SteamProfileInput.Vanity v:
                    var resolved = await steam.ResolveVanityUrlAsync(v.Name);
                    if (!resolved.IsSuccess) { StatusMessage = Describe(resolved.Error!); return; }
                    steamId = resolved.Value;
                    var s = await store.LoadAsync();
                    await store.SaveAsync(s with { SteamId64 = steamId });
                    _resolvedSteamId = steamId;
                    break;
                default:
                    StatusMessage = "Informe seu perfil Steam antes de testar.";
                    return;
            }

            var summary = await steam.GetPlayerSummaryAsync(steamId);
            if (!summary.IsSuccess) { StatusMessage = Describe(summary.Error!); return; }

            StatusMessage = summary.Value!.IsProfilePublic
                ? $"Conectado como {summary.Value.PersonaName}. Perfil público. Confira também se “Detalhes do jogo” está público."
                : $"Chave OK, mas o perfil de {summary.Value.PersonaName} não está público. Sem isso a Steam não informa conquistas.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static void OpenApiKeyPage() => OpenUrl("https://steamcommunity.com/dev/apikey");

    [RelayCommand]
    private static void OpenPrivacyPage() => OpenUrl("https://steamcommunity.com/my/edit/settings");

    [RelayCommand]
    private void OpenDataFolder() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{paths.Root}\"") { UseShellExecute = true });

    private async Task RefreshKeyStatusAsync()
    {
        var key = await secrets.GetSteamApiKeyAsync();
        ApiKeyStatus = string.IsNullOrEmpty(key)
            ? "Nenhuma chave salva."
            : $"Chave salva e criptografada neste computador (termina em …{key[^Math.Min(4, key.Length)..]}).";
    }

    private static string Describe(SteamError e) => e.Kind switch
    {
        SteamErrorKind.InvalidApiKey => "A Steam recusou a chave. Confira se copiou a chave inteira.",
        SteamErrorKind.NotFound => "Perfil não encontrado na Steam.",
        SteamErrorKind.PrivateProfile => "Perfil privado: a Steam não libera esses dados.",
        SteamErrorKind.RateLimited => "A Steam pediu para esperar um pouco antes de novas consultas.",
        SteamErrorKind.Network => "Sem conexão com a Steam. Verifique sua internet.",
        _ => $"Não foi possível falar com a Steam: {e.Message}",
    };

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
