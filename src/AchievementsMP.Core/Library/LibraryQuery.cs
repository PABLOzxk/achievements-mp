using AchievementsMP.Core.Progress;

namespace AchievementsMP.Core.Library;

public enum LibraryStatus
{
    All,
    /// <summary>Começados e ainda não completos.</summary>
    InProgress,
    Completed,
    /// <summary>Têm conquistas, nenhuma desbloqueada.</summary>
    NotStarted,
    NoAchievements,
}

public enum LibrarySort
{
    Name,
    FewestRemaining,
    MostViable,
    MostPlayed,
    RecentlyPlayed,
    HighestPercent,
}

public sealed record LibraryFilter
{
    public string Search { get; init; } = "";
    public LibraryStatus Status { get; init; } = LibraryStatus.All;
    /// <summary>Mostra só jogos com viabilidade igual ou maior (exclui desconhecidos e completos).</summary>
    public Viability? MinViability { get; init; }
    /// <summary>Restringe a estes jogos (lista personalizada). Null = todos.</summary>
    public IReadOnlySet<long>? OnlyAppIds { get; init; }
    public LibrarySort Sort { get; init; } = LibrarySort.Name;
}

public static class LibraryQuery
{
    public static IReadOnlyList<LibraryGame> Apply(IEnumerable<LibraryGame> games, LibraryFilter filter)
    {
        var term = filter.Search.Trim();
        var query = games.Where(g =>
            (term.Length == 0 || g.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            && (filter.OnlyAppIds is null || filter.OnlyAppIds.Contains(g.AppId))
            && MatchesStatus(g, filter.Status)
            && (filter.MinViability is not { } min || (g.Outlook.Viability >= min && g.Outlook.Viability != Viability.Done)));

        return Sort(query, filter.Sort).ToList();
    }

    public static bool MatchesStatus(LibraryGame g, LibraryStatus status) => status switch
    {
        LibraryStatus.InProgress => g.Progress.Total > 0 && g.Progress.Unlocked > 0 && !g.Progress.IsComplete,
        LibraryStatus.Completed => g.Progress.IsComplete,
        LibraryStatus.NotStarted => g.Progress.Total > 0 && g.Progress.Unlocked == 0,
        LibraryStatus.NoAchievements => g.Progress.Total == 0,
        _ => true,
    };

    private static IEnumerable<LibraryGame> Sort(IEnumerable<LibraryGame> games, LibrarySort sort)
    {
        var byName = StringComparer.CurrentCultureIgnoreCase;
        // Nas ordenações "para completar", jogos completos ou sem conquistas vão para o fim.
        static int Actionable(LibraryGame g) => g.Progress.Total > 0 && !g.Progress.IsComplete ? 0 : 1;

        return sort switch
        {
            LibrarySort.FewestRemaining => games
                .OrderBy(Actionable).ThenBy(g => g.Progress.KnownRemaining).ThenByDescending(g => g.Progress.Percent).ThenBy(g => g.Name, byName),
            LibrarySort.MostViable => games
                .OrderBy(Actionable)
                .ThenByDescending(g => g.Outlook.Viability == Viability.Unknown ? -1 : (int)g.Outlook.Viability)
                .ThenByDescending(g => g.Outlook.RarestRemainingPercent ?? -1)
                .ThenBy(g => g.Progress.KnownRemaining)
                .ThenBy(g => g.Name, byName),
            LibrarySort.MostPlayed => games.OrderByDescending(g => g.PlaytimeMinutes).ThenBy(g => g.Name, byName),
            LibrarySort.RecentlyPlayed => games.OrderByDescending(g => g.LastPlayedUtc ?? DateTimeOffset.MinValue).ThenBy(g => g.Name, byName),
            LibrarySort.HighestPercent => games
                .OrderByDescending(g => g.Progress.Percent ?? -1).ThenBy(g => g.Progress.KnownRemaining).ThenBy(g => g.Name, byName),
            _ => games.OrderBy(g => g.Name, byName),
        };
    }
}
