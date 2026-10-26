using System.Globalization;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Progress;
using AchievementsMP.Data.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AchievementsMP.App.ViewModels;

public enum AchievementFilter { All, Pending, Unlocked }

public enum AchievementSort { CommonFirst, RarestFirst, Name, RecentlyUnlocked }

/// <summary>Raridade global de uma conquista, nas mesmas faixas da viabilidade.</summary>
public enum Rarity { Unknown, Common, Uncommon, Rare, UltraRare }

public sealed record AchievementItem(
    string Name,
    string Description,
    string? IconUrl,
    bool IsUnlocked,
    bool IsUnknown,
    string StateText,
    double? GlobalPercent,
    string RarityText,
    Rarity Rarity,
    DateTimeOffset? UnlockedAtUtc)
{
    /// <summary>Desbloqueada e obtida por menos de 5% dos jogadores: destaque dourado.</summary>
    public bool IsRareUnlock => IsUnlocked && Rarity is Rarity.Rare or Rarity.UltraRare;

    public static AchievementItem From(AchievementDetail a)
    {
        var unlocked = a.State == UnlockState.Unlocked;
        var description = a.Description
            ?? (a.Hidden ? "Conquista oculta: a Steam não mostra a descrição até você desbloquear." : "Sem descrição.");
        var state = a.State switch
        {
            UnlockState.Unlocked when a.UnlockedAtUtc is { } at => $"Desbloqueada em {at.ToLocalTime():dd/MM/yyyy}",
            UnlockState.Unlocked => "Desbloqueada",
            UnlockState.Locked => "Pendente",
            _ => "Sem dados",
        };
        var (rarity, rarityText) = DescribeRarity(a.GlobalPercent);
        return new AchievementItem(a.DisplayName, description, unlocked ? a.IconUrl : a.IconGrayUrl ?? a.IconUrl,
            unlocked, a.State == UnlockState.Unknown, state, a.GlobalPercent, rarityText, rarity, a.UnlockedAtUtc);
    }

    public static (Rarity Rarity, string Text) DescribeRarity(double? percent) => (
        percent switch
        {
            null => Rarity.Unknown,
            >= CompletionOutlook.HighThreshold => Rarity.Common,
            >= CompletionOutlook.MediumThreshold => Rarity.Uncommon,
            >= CompletionOutlook.LowThreshold => Rarity.Rare,
            _ => Rarity.UltraRare,
        },
        percent is { } p ? $"{LibraryItem.FormatPercent(p)} dos jogadores" : "Raridade desconhecida");
}

public sealed partial class GameDetailsViewModel(SteamCatalogRepository catalog) : PageViewModel
{
    public override string Title => "Detalhes do jogo";
    public override string Glyph => "";

    private long? _appId;
    private IReadOnlyList<AchievementItem> _all = [];

    internal Action<long>? NavigateToPlanner { get; set; }

    [RelayCommand]
    private void OpenPlanner()
    {
        if (_appId is { } appId) NavigateToPlanner?.Invoke(appId);
    }

    public IReadOnlyList<Option<AchievementFilter>> FilterOptions { get; } =
    [
        new(AchievementFilter.All, "Todas"),
        new(AchievementFilter.Pending, "Pendentes"),
        new(AchievementFilter.Unlocked, "Desbloqueadas"),
    ];

    public IReadOnlyList<Option<AchievementSort>> SortOptions { get; } =
    [
        new(AchievementSort.CommonFirst, "Mais comuns primeiro"),
        new(AchievementSort.RarestFirst, "Mais raras primeiro"),
        new(AchievementSort.Name, "Nome"),
        new(AchievementSort.RecentlyUnlocked, "Desbloqueadas recentemente"),
    ];

    [ObservableProperty] private bool _hasGame;
    [ObservableProperty] private LibraryItem? _game;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _caveat = "";
    [ObservableProperty] private IReadOnlyList<AchievementItem> _achievements = [];
    [ObservableProperty] private Option<AchievementFilter>? _filter;
    [ObservableProperty] private Option<AchievementSort>? _sort;
    [ObservableProperty] private string _countsText = "";

    public void Show(long appId)
    {
        if (_appId != appId)
        {
            _appId = appId;
            Filter = null; // escolhe o filtro padrão de novo para o novo jogo
        }
    }

    public override async Task OnNavigatedToAsync()
    {
        if (_appId is not { } appId || await catalog.GetGameAsync(appId) is not { } game)
        {
            HasGame = false;
            return;
        }

        Game = LibraryItem.From(game);
        _all = (await catalog.GetAchievementsAsync(appId)).Select(AchievementItem.From).ToList();

        var p = game.Progress;
        Headline = p.Total switch
        {
            0 => "Este jogo não tem conquistas na Steam.",
            _ when p.IsComplete => $"Completo: {p.Total} de {p.Total} conquistas.",
            _ => $"Faltam {p.KnownRemaining} de {p.Total} conquistas"
                 + (game.Outlook.RarestRemainingPercent is { } r ? $" · a mais rara que falta foi obtida por {LibraryItem.FormatPercent(r)} dos jogadores" : "")
                 + (p.Unknown > 0 ? $" · {p.Unknown} sem dados" : ""),
        };
        Caveat = p.Total > 0 && !p.IsComplete
            ? "Raridade = % de todos os donos do jogo que desbloquearam. Conquista do fim da campanha pode parecer rara só porque muita gente parou de jogar antes."
            : "";

        var pending = _all.Count(a => !a.IsUnlocked && !a.IsUnknown);
        var rare = _all.Count(a => a.IsRareUnlock);
        CountsText = $"{_all.Count(a => a.IsUnlocked)} desbloqueadas" + (rare > 0 ? $" ({rare} {(rare == 1 ? "rara" : "raras")})" : "") + $" · {pending} pendentes";
        Sort ??= SortOptions[0];
        if (Filter is null)
            Filter = pending > 0 ? FilterOptions[1] : FilterOptions[0];
        else
            ApplyFilter();
        HasGame = true;
    }

    partial void OnFilterChanged(Option<AchievementFilter>? value) => ApplyFilter();
    partial void OnSortChanged(Option<AchievementSort>? value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<AchievementItem> q = (Filter?.Value ?? AchievementFilter.All) switch
        {
            AchievementFilter.Pending => _all.Where(a => !a.IsUnlocked),
            AchievementFilter.Unlocked => _all.Where(a => a.IsUnlocked),
            _ => _all,
        };
        var byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        q = (Sort?.Value ?? AchievementSort.CommonFirst) switch
        {
            AchievementSort.RarestFirst => q.OrderBy(a => a.GlobalPercent ?? double.MaxValue).ThenBy(a => a.Name, byName),
            AchievementSort.Name => q.OrderBy(a => a.Name, byName),
            AchievementSort.RecentlyUnlocked => q.OrderByDescending(a => a.UnlockedAtUtc ?? DateTimeOffset.MinValue).ThenBy(a => a.Name, byName),
            _ => q.OrderByDescending(a => a.GlobalPercent ?? -1).ThenBy(a => a.Name, byName),
        };
        Achievements = q.ToList();
    }
}
