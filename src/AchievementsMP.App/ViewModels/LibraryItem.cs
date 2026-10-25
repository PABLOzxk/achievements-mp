using AchievementsMP.Core.Domain;
using AchievementsMP.Data.Repositories;

namespace AchievementsMP.App.ViewModels;

/// <summary>Uma linha da biblioteca, já formatada para exibição.</summary>
public sealed record LibraryItem(
    long AppId,
    string Name,
    string? IconUrl,
    string PlaytimeText,
    string ProgressText,
    double Percent,
    bool ShowBar,
    bool IsComplete,
    string? Badge,
    string? BadgeTooltip,
    int PlaytimeMinutes,
    int Remaining)
{
    public static LibraryItem From(LibraryGame g)
    {
        var p = g.Progress;
        var progressText = p.Total switch
        {
            > 0 => $"{p.Unlocked} de {p.Total}" + (p.Unknown > 0 ? $" · {p.Unknown} sem dados" : ""),
            _ => g.Availability switch
            {
                DataAvailability.NoAchievements => "Sem conquistas",
                DataAvailability.NotSynced => "Ainda não sincronizado",
                _ => "Conquistas indisponíveis",
            },
        };

        (string? badge, string? tooltip) = g.Availability switch
        {
            DataAvailability.PrivateProfile => ("Privado", "A Steam não liberou o progresso deste jogo. Confira se “Detalhes do jogo” está público."),
            DataAvailability.SyncFailed => ("Desatualizado", $"A última tentativa falhou e os dados anteriores foram mantidos. {g.LastError}"),
            DataAvailability.SchemaUnavailable => ("Indisponível", g.LastError ?? "A Steam não informou as conquistas deste jogo."),
            _ when p.IsComplete => ("100%", null),
            _ => (null, null),
        };

        return new LibraryItem(
            g.AppId, g.Name, g.IconUrl, FormatPlaytime(g.PlaytimeMinutes), progressText,
            p.Percent ?? 0, p.Total > 0, p.IsComplete, badge, tooltip, g.PlaytimeMinutes, p.KnownRemaining);
    }

    private static string FormatPlaytime(int minutes) => minutes switch
    {
        0 => "Nunca jogado",
        < 60 => $"{minutes} min",
        _ => $"{minutes / 60.0:0.#} h",
    };
}
