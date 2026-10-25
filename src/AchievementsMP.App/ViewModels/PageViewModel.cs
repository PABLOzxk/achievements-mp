using CommunityToolkit.Mvvm.ComponentModel;

namespace AchievementsMP.App.ViewModels;

public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Glyph { get; }

    /// <summary>Página atualmente exibida (marca o item na navegação lateral).</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>Chamado sempre que a página é exibida. Carregue dados aqui, nunca no construtor.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
