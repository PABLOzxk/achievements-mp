using System.Windows.Input;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Progress;
using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

/// <summary>Lista do usuário para o filtro e para o menu "Adicionar à lista".</summary>
public sealed record ListOption(long? Id, string Name, bool IsFavorites, ICommand? AddSelected)
{
    public override string ToString() => Name;
}

public sealed partial class LibraryViewModel(SteamCatalogRepository catalog, PersonalDataRepository personal, TimeProvider time) : PageViewModel, IGameOpener
{
    public override string Title => "Minha biblioteca";
    public override string Glyph => "";

    internal Action<long>? NavigateToGame { get; set; }
    internal Action<long>? NavigateToPlanner { get; set; }

    public IReadOnlyList<Option<LibraryStatus>> StatusOptions { get; } =
    [
        new(LibraryStatus.All, "Todos"),
        new(LibraryStatus.InProgress, "Em andamento"),
        new(LibraryStatus.NotStarted, "Não começados"),
        new(LibraryStatus.Completed, "Completos (100%)"),
        new(LibraryStatus.NoAchievements, "Sem conquistas"),
    ];

    public IReadOnlyList<Option<Viability?>> ViabilityOptions { get; } =
    [
        new(null, "Qualquer viabilidade"),
        new(Viability.High, "Só viáveis"),
        new(Viability.Medium, "Moderados ou melhor"),
        new(Viability.Low, "Difíceis ou melhor"),
    ];

    public IReadOnlyList<Option<LibrarySort>> SortOptions { get; } =
    [
        new(LibrarySort.Name, "Nome"),
        new(LibrarySort.FewestRemaining, "Menos conquistas faltando"),
        new(LibrarySort.MostViable, "Mais viável de completar"),
        new(LibrarySort.HighestPercent, "Maior percentual"),
        new(LibrarySort.MostPlayed, "Mais jogados"),
        new(LibrarySort.RecentlyPlayed, "Jogados recentemente"),
    ];

    private IReadOnlyList<LibraryGame> _games = [];
    private ILookup<long, long> _membership = Array.Empty<long>().ToLookup(x => x);
    private long? _favoritesId;

    [ObservableProperty] private IReadOnlyList<LibraryItem> _items = [];
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private Option<LibraryStatus>? _status;
    [ObservableProperty] private Option<Viability?>? _minViability;
    [ObservableProperty] private Option<LibrarySort>? _sort;
    [ObservableProperty] private IReadOnlyList<ListOption> _listFilterOptions = [];
    [ObservableProperty] private IReadOnlyList<ListOption> _userLists = [];
    [ObservableProperty] private ListOption? _listFilter;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isListSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleFavoriteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFromCurrentListCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCreatingListWithGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlanSelectedCommand))]
    private LibraryItem? _selectedItem;

    [ObservableProperty] private bool _isCreatingList;
    [ObservableProperty] private string _newListName = "";

    public override async Task OnNavigatedToAsync()
    {
        Status ??= StatusOptions[0];
        MinViability ??= ViabilityOptions[0];
        Sort ??= SortOptions[0];

        _games = await catalog.GetLibraryAsync();
        await ReloadListsAsync();

        var withAchievements = _games.Count(g => g.Progress.Total > 0);
        var complete = _games.Count(g => g.Progress.IsComplete);
        Summary = _games.Count == 0 ? "" : $"{_games.Count} jogos · {withAchievements} com conquistas · {complete} em 100%";
        IsEmpty = _games.Count == 0;
        ApplyFilter();
    }

    public void OpenGame(long appId) => NavigateToGame?.Invoke(appId);

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnStatusChanged(Option<LibraryStatus>? value) => ApplyFilter();
    partial void OnMinViabilityChanged(Option<Viability?>? value) => ApplyFilter();
    partial void OnSortChanged(Option<LibrarySort>? value) => ApplyFilter();
    partial void OnListFilterChanged(ListOption? value)
    {
        IsListSelected = value?.Id is not null;
        DeleteCurrentListCommand.NotifyCanExecuteChanged();
        RemoveFromCurrentListCommand.NotifyCanExecuteChanged();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = new LibraryFilter
        {
            Search = Search,
            Status = Status?.Value ?? LibraryStatus.All,
            MinViability = MinViability?.Value,
            Sort = Sort?.Value ?? LibrarySort.Name,
            OnlyAppIds = ListFilter?.Id is { } listId
                ? _games.Where(g => _membership[g.AppId].Contains(listId)).Select(g => g.AppId).ToHashSet()
                : null,
        };
        Items = LibraryQuery.Apply(_games, filter)
            .Select(g => LibraryItem.From(g, _favoritesId is { } fav && _membership[g.AppId].Contains(fav)))
            .ToList();
        ResultText = Items.Count == _games.Count ? "" : $"{Items.Count} de {_games.Count} jogos";
    }

    private async Task ReloadListsAsync()
    {
        var lists = await personal.GetListsAsync();
        _membership = await personal.GetMembershipAsync();
        _favoritesId = lists.FirstOrDefault(l => l.IsFavorites)?.Id;

        UserLists = lists.Where(l => !l.IsFavorites)
            .Select(l => new ListOption(l.Id, l.Name, false, new AsyncRelayCommand(() => AddSelectedToListAsync(l.Id))))
            .ToList();

        var currentId = ListFilter?.Id;
        ListFilterOptions = [new ListOption(null, "Todos os jogos", false, null), .. lists.Select(l => new ListOption(l.Id, l.IsFavorites ? "★ Favoritos" : l.Name, l.IsFavorites, null))];
        ListFilter = ListFilterOptions.FirstOrDefault(o => o.Id == currentId) ?? ListFilterOptions[0];
    }

    private async Task AfterListChangeAsync()
    {
        var selected = SelectedItem?.AppId;
        await ReloadListsAsync();
        ApplyFilter();
        SelectedItem = Items.FirstOrDefault(i => i.AppId == selected);
    }

    private bool HasSelection() => SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void PlanSelected()
    {
        if (SelectedItem is { } item) NavigateToPlanner?.Invoke(item.AppId);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedItem is not { } item) return;
        var fav = await personal.GetOrCreateFavoritesAsync(time.GetUtcNow());
        if (item.IsFavorite)
            await personal.RemoveGameFromListAsync(fav, item.AppId);
        else
            await personal.AddGameToListAsync(fav, item.AppId, time.GetUtcNow());
        await AfterListChangeAsync();
    }

    private async Task AddSelectedToListAsync(long listId)
    {
        if (SelectedItem is not { } item) return;
        await personal.AddGameToListAsync(listId, item.AppId, time.GetUtcNow());
        await AfterListChangeAsync();
    }

    private bool CanRemoveFromCurrentList() => SelectedItem is not null && ListFilter?.Id is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveFromCurrentList))]
    private async Task RemoveFromCurrentListAsync()
    {
        if (SelectedItem is not { } item || ListFilter?.Id is not { } listId) return;
        await personal.RemoveGameFromListAsync(listId, item.AppId);
        await AfterListChangeAsync();
    }

    private bool _createWithSelected;

    [RelayCommand]
    private void StartCreatingList() => BeginCreate(withSelected: false);

    /// <summary>Pelo menu de contexto: a lista nova já nasce com o jogo clicado.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void StartCreatingListWithGame() => BeginCreate(withSelected: true);

    private void BeginCreate(bool withSelected)
    {
        _createWithSelected = withSelected;
        NewListName = "";
        IsCreatingList = true;
    }

    [RelayCommand]
    private void CancelCreatingList() => IsCreatingList = false;

    [RelayCommand]
    private async Task CreateListAsync()
    {
        var name = NewListName.Trim();
        if (name.Length == 0) return;
        var id = await personal.CreateListAsync(name, null, time.GetUtcNow());
        if (_createWithSelected && SelectedItem is { } item)
            await personal.AddGameToListAsync(id, item.AppId, time.GetUtcNow());
        IsCreatingList = false;
        await ReloadListsAsync();
        ListFilter = ListFilterOptions.FirstOrDefault(o => o.Id == id);
    }

    private bool CanDeleteCurrentList() => ListFilter?.Id is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentList))]
    private async Task DeleteCurrentListAsync()
    {
        if (ListFilter?.Id is not { } listId) return;
        var answer = System.Windows.MessageBox.Show(
            $"Excluir a lista “{ListFilter.Name}”? Os jogos continuam na sua biblioteca.",
            "Excluir lista", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        await personal.DeleteListAsync(listId);
        ListFilter = null;
        await ReloadListsAsync();
        ApplyFilter();
    }
}
