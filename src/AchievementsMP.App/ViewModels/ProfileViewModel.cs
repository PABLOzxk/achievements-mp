using AchievementsMP.App.Services;
using AchievementsMP.Core.Profile;
using AchievementsMP.Data.Repositories;
using AchievementsMP.Sync;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

/// <summary>Cartão no topo da barra lateral: foto e nome, da Steam ou escolhidos pelo usuário. Clicar abre a edição.</summary>
public sealed partial class ProfileViewModel(ProfileRepository profiles, ProfileRefresher refresher, IShell shell) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Initial), nameof(HasInitial), nameof(CustomAvatar), nameof(SteamAvatarUrl))]
    private UserProfile _profile = UserProfile.Empty;

    public string Name => Profile.Name ?? "Seu perfil";

    /// <summary>Letra no círculo enquanto não há foto (ou enquanto a da Steam carrega). Vazia = ícone de pessoa.</summary>
    public string Initial => Profile.Name?.FirstOrDefault(char.IsLetterOrDigit) is { } c and not '\0' ? char.ToUpperInvariant(c).ToString() : "";

    public bool HasInitial => Initial.Length > 0;

    /// <summary>Foto escolhida; quando existe, a da Steam não é usada.</summary>
    public byte[]? CustomAvatar => Profile.CustomAvatar;

    public string? SteamAvatarUrl => Profile.CustomAvatar is null ? Profile.SteamAvatarUrl : null;

    public async Task LoadAsync() => Profile = await profiles.LoadAsync();

    /// <summary>Busca nome e foto atuais na Steam. Sem perfil, sem chave ou sem internet, fica o que já estava.</summary>
    public async Task RefreshFromSteamAsync()
    {
        try
        {
            if (await Task.Run(() => refresher.RefreshAsync()))
                await LoadAsync();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao atualizar o perfil com a Steam", ex);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        var editor = new ProfileEditorViewModel(await profiles.LoadAsync(), profiles, shell);
        if (!shell.EditProfile(editor))
            return;
        await LoadAsync();
        // As cores ficam presas nos estilos já criados: o tema novo só aparece com o app reaberto.
        if (editor.ThemeChanged && shell.Confirm($"O tema “{editor.SelectedTheme.Name}” aparece quando o app reabrir. Reiniciar agora?", "Tema do app"))
            shell.Restart();
    }
}

/// <summary>Janela "Seu perfil": trocar a foto por qualquer imagem e mudar o nome. Nada é gravado até Salvar.</summary>
public sealed partial class ProfileEditorViewModel(UserProfile current, ProfileRepository profiles, IShell shell) : ObservableObject
{
    public string? SteamName => current.SteamName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseSteamName), nameof(Initial), nameof(HasInitial))]
    private string _name = current.Name ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomAvatar), nameof(SteamAvatarUrl))]
    private byte[]? _customAvatar = current.CustomAvatar;

    [ObservableProperty] private string _error = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ChoosePhotoCommand))]
    private bool _isBusy;

    public bool HasCustomAvatar => CustomAvatar is not null;
    public string? SteamAvatarUrl => CustomAvatar is null ? current.SteamAvatarUrl : null;
    public string Initial => Name.FirstOrDefault(char.IsLetterOrDigit) is var c and not '\0' ? char.ToUpperInvariant(c).ToString() : "";
    public bool HasInitial => Initial.Length > 0;

    public string ResetPhotoText => current.SteamAvatarUrl is null ? "Remover foto" : "Usar a foto da Steam";

    public IReadOnlyList<ThemeOption> Themes => ThemeOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeHint))]
    private ThemeOption _selectedTheme =
        ThemeOption.All.FirstOrDefault(t => t.Id == current.Theme) ?? ThemeOption.All[0];

    public string ThemeHint => SelectedTheme.Palette == ThemeManager.Current
        ? "Vale para o app inteiro, em todas as abas."
        : "O tema novo aparece quando o app reabrir. Ao salvar, dá para reiniciar na hora.";

    /// <summary>Verdadeiro depois de gravar um tema diferente do que estava: quem abriu a janela oferece reiniciar.</summary>
    public bool ThemeChanged { get; private set; }
    public bool CanUseSteamName => SteamName is not null && Name.Trim() != SteamName;
    public string NameHint => SteamName is null
        ? $"Até {UserProfile.MaxNameLength} caracteres."
        : $"Na Steam você é {SteamName}. Deixe vazio para usar esse nome.";

    /// <summary>Verdadeiro depois de gravar: quem abriu a janela recarrega o cartão.</summary>
    public bool Saved { get; private set; }

    public event Action? CloseRequested;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ChoosePhotoAsync()
    {
        if (shell.PickImageFile() is { } path)
            await LoadPhotoAsync(path);
    }

    /// <summary>Reduz a imagem fora da thread da interface (uma foto de celular leva um instante para abrir).</summary>
    internal async Task LoadPhotoAsync(string path)
    {
        IsBusy = true;
        try
        {
            CustomAvatar = await Task.Run(() => AvatarImage.FromFile(path));
            Error = "";
        }
        catch (InvalidAvatarException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void UseSteamPhoto() => CustomAvatar = null;

    [RelayCommand]
    private void UseSteamName() => Name = SteamName ?? "";

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SaveAsync()
    {
        await profiles.SaveChoicesAsync(UserProfile.NormalizeName(Name, SteamName), CustomAvatar);
        if (SelectedTheme.Palette != ThemeCatalog.Find(current.Theme))
        {
            await profiles.SaveThemeAsync(SelectedTheme.Palette == ThemeCatalog.Default ? null : SelectedTheme.Id);
            ThemeChanged = true;
            FileLog.Info($"Tema escolhido: {SelectedTheme.Id}.");
        }
        FileLog.Info("Perfil editado.");
        Saved = true;
        CloseRequested?.Invoke();
    }

    private bool IsIdle() => !IsBusy;
}
