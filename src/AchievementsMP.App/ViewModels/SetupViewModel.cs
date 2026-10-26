using System.Diagnostics;
using AchievementsMP.App.Services;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Settings;
using AchievementsMP.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

/// <summary>Estado de um passo do guia, com o texto que explica o que fazer.</summary>
public sealed partial class SetupStep : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOk), nameof(IsProblem), nameof(Badge))]
    private CheckState _state;

    [ObservableProperty] private string _message = "";

    public bool IsOk => State == CheckState.Ok;
    public bool IsProblem => State == CheckState.Problem;
    public string Badge => State switch
    {
        CheckState.Ok => "Pronto",
        CheckState.Problem => "Falta ajustar",
        _ => "",
    };

    public void Set(CheckState state, string message)
    {
        State = state;
        Message = message;
    }
}

/// <summary>
/// Guia de primeira configuração para quem nunca usou a Steam Web API: perfil, chave, privacidade e backup,
/// com uma verificação que diz exatamente qual passo falta.
/// </summary>
public sealed partial class SetupViewModel(
    ISettingsStore store, ISecretStore secrets, SetupChecker checker, SyncCoordinator sync, BackupActions backupActions) : PageViewModel
{
    public override string Title => "Primeiros passos";
    public override string Glyph => "";

    internal Action? NavigateToOverview { get; set; }

    public SetupStep ProfileStep { get; } = new();
    public SetupStep KeyStep { get; } = new();
    public SetupStep PrivacyStep { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileHint))]
    private string _steamProfile = "";

    /// <summary>Chave digitada agora (vinda do PasswordBox). Vazia = manter a salva.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyShapeWarning))]
    private string _newApiKey = "";

    [ObservableProperty] private string _savedKeyText = "";
    [ObservableProperty] private string _notice = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(FinishCommand))]
    private bool _isChecking;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FinishCommand))]
    private bool _isReady;

    [ObservableProperty] private string _backupText = "";
    [ObservableProperty] private string _backupMessage = "";
    [ObservableProperty] private bool _isBackupOn;

    public string ProfileHint => SteamIdentity.Parse(SteamProfile) switch
    {
        SteamProfileInput.SteamId id => $"SteamID64 {id.Value}",
        SteamProfileInput.Vanity v => $"Nome personalizado “{v.Name}”",
        _ when string.IsNullOrWhiteSpace(SteamProfile) => "",
        _ => "Não reconheci esse formato.",
    };

    public string KeyShapeWarning =>
        string.IsNullOrWhiteSpace(NewApiKey) || SteamIdentity.LooksLikeApiKey(NewApiKey)
            ? ""
            : "A chave tem 32 letras e números. Confira se copiou ela inteira, sem espaços no meio.";

    public override async Task OnNavigatedToAsync()
    {
        var s = await store.LoadAsync();
        SteamProfile = s.SteamProfile ?? "";
        IsBackupOn = !string.IsNullOrWhiteSpace(s.BackupFolder);
        BackupText = IsBackupOn ? $"Backup automático ligado em {s.BackupFolder}" : "Backup automático desligado.";
        BackupMessage = "";
        await RefreshKeyAsync();
        Notice = "";
    }

    /// <summary>Salva perfil e chave e pergunta à Steam se está tudo pronto. Nada é sincronizado aqui.</summary>
    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        IsChecking = true;
        Notice = "Consultando a Steam…";
        try
        {
            var previous = await store.LoadAsync();
            var profile = string.IsNullOrWhiteSpace(SteamProfile) ? null : SteamProfile.Trim();
            if (!string.IsNullOrWhiteSpace(NewApiKey))
            {
                await secrets.SetSteamApiKeyAsync(NewApiKey);
                NewApiKey = "";
            }
            await RefreshKeyAsync();
            var hasKey = !string.IsNullOrEmpty(await secrets.GetSteamApiKeyAsync());

            var result = await Task.Run(() => checker.CheckAsync(profile, hasKey));

            // Guarda o perfil sempre; o SteamID64 só quando a Steam confirmou (mesmo critério das configurações).
            await store.SaveAsync(previous with
            {
                SteamProfile = profile,
                SteamId64 = result.SteamId ?? (previous.SteamProfile == profile ? previous.SteamId64 : null),
            });

            ProfileStep.Set(result.Profile, result.ProfileMessage);
            KeyStep.Set(result.Key, result.KeyMessage);
            PrivacyStep.Set(result.Privacy, result.PrivacyMessage);
            IsReady = result.AllOk;
            Notice = result.AllOk
                ? $"Tudo certo, {result.PersonaName}! Clique em Concluir para buscar suas conquistas."
                : result.Notice ?? "Ajuste o que está marcado e clique em Verificar de novo.";
            FileLog.Info($"Guia de configuração: perfil {result.Profile}, chave {result.Key}, privacidade {result.Privacy}.");
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool CanCheck() => !IsChecking;

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Finish()
    {
        NavigateToOverview?.Invoke();
        if (sync.SyncNowCommand.CanExecute(null))
            sync.SyncNowCommand.Execute(null);
    }

    private bool CanFinish() => IsReady && !IsChecking;

    [RelayCommand]
    private async Task ChooseBackupFolderAsync()
    {
        var result = await backupActions.ChooseFolderAsync(null);
        if (result is null) return;
        BackupMessage = result.Message;
        var folder = (await store.LoadAsync()).BackupFolder;
        IsBackupOn = !string.IsNullOrWhiteSpace(folder);
        BackupText = IsBackupOn ? $"Backup automático ligado em {folder}" : "Backup automático desligado.";
    }

    [RelayCommand]
    private static void OpenProfilePage() => OpenUrl("https://steamcommunity.com/my/profile");

    [RelayCommand]
    private static void OpenApiKeyPage() => OpenUrl("https://steamcommunity.com/dev/apikey");

    [RelayCommand]
    private static void OpenPrivacyPage() => OpenUrl("https://steamcommunity.com/my/edit/settings");

    private async Task RefreshKeyAsync()
    {
        var key = await secrets.GetSteamApiKeyAsync();
        SavedKeyText = string.IsNullOrEmpty(key)
            ? ""
            : $"Já existe uma chave salva (termina em …{key[^Math.Min(4, key.Length)..]}). Cole outra só se quiser trocar.";
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
