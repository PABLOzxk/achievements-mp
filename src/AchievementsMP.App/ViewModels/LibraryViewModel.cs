using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AchievementsMP.App.ViewModels;

/// <summary>Biblioteca básica (Fase 2): busca por nome e progresso por jogo. Filtros e listas vêm na Fase 3.</summary>
public sealed partial class LibraryViewModel(SteamCatalogRepository catalog) : PageViewModel
{
    public override string Title => "Minha biblioteca";
    public override string Glyph => "";

    private IReadOnlyList<LibraryItem> _all = [];

    [ObservableProperty] private IReadOnlyList<LibraryItem> _items = [];
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isEmpty;

    public override async Task OnNavigatedToAsync()
    {
        var games = await catalog.GetLibraryAsync();
        _all = games.Select(LibraryItem.From).ToList();
        var withAchievements = games.Count(g => g.Progress.Total > 0);
        var complete = games.Count(g => g.Progress.IsComplete);
        Summary = games.Count == 0 ? "" : $"{games.Count} jogos · {withAchievements} com conquistas · {complete} em 100%";
        IsEmpty = games.Count == 0;
        ApplyFilter();
    }

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = Search.Trim();
        Items = term.Length == 0
            ? _all
            : _all.Where(i => i.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }
}
