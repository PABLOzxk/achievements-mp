using System.Globalization;
using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Progress;

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
    Viability Viability,
    string? ViabilityLabel,
    string? ViabilityTooltip,
    bool IsFavorite)
{
    /// <summary>Jogo emprestado da Família Steam (a conta não é dona). Aparece com a marca "Família".</summary>
    public bool IsFamily { get; init; }

    public const string FamilyTooltip =
        "Jogo da Família Steam: você jogou emprestado e as conquistas são suas. Se comprar o jogo, a marca some.";

    /// <summary>Percentual inteiro para exibir. Só mostra 100% quando está completo de verdade (ver <see cref="WholePercent"/>).</summary>
    public string PercentText => WholePercent(Percent, IsComplete);

    /// <summary>
    /// Corta em vez de arredondar: 249 de 250 (99,6%) aparece como 99%, não como um 100% que ainda não é.
    /// </summary>
    public static string WholePercent(double percent, bool complete) =>
        complete ? "100%" : Math.Clamp(Math.Floor(percent), 0, 99).ToString("0", CultureInfo.InvariantCulture) + "%";

    public static LibraryItem From(LibraryGame g, bool isFavorite = false)
    {
        var p = g.Progress;
        var progressText = p.Total switch
        {
            > 0 when p.IsComplete => $"{p.Unlocked} de {p.Total}",
            > 0 => $"{p.Unlocked} de {p.Total} · faltam {p.KnownRemaining}" + (p.Unknown > 0 ? $" · {p.Unknown} sem dados" : ""),
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
            _ => (null, null),
        };

        var (vLabel, vTooltip) = Describe(g.Outlook);

        return new LibraryItem(
            g.AppId, g.Name, g.IconUrl, FormatPlaytime(g.PlaytimeMinutes), progressText,
            p.Percent ?? 0, p.Total > 0, p.IsComplete, badge, tooltip, g.Outlook.Viability, vLabel, vTooltip, isFavorite)
        {
            IsFamily = g.IsFamily,
        };
    }

    public static (string? Label, string? Tooltip) Describe(CompletionOutlook o)
    {
        var rarest = o.RarestRemainingPercent is { } r ? FormatPercent(r) : null;
        return o.Viability switch
        {
            Viability.Done => ("100%", "Jogo completo."),
            Viability.High => ("Viável", $"Todas as que faltam são comuns: a mais rara foi obtida por {rarest} dos jogadores."),
            Viability.Medium => ("Moderado", $"A mais rara que falta foi obtida por {rarest} dos jogadores."),
            Viability.Low => ("Difícil", $"A mais rara que falta foi obtida por só {rarest} dos jogadores."),
            Viability.VeryLow => ("Muito difícil", $"A mais rara que falta foi obtida por menos de 1% dos jogadores ({rarest})."),
            _ => (null, null),
        };
    }

    /// <summary>
    /// Corta as casas em vez de arredondar, para o número mostrado nunca subir de faixa: 9,96% aparece como 9,9%
    /// (é rara) e não como 10% (que não é).
    /// </summary>
    public static string FormatPercent(double value)
    {
        if (!double.IsFinite(value))
            return value.ToString(CultureInfo.CurrentCulture) + "%";
        var d = (decimal)value;
        return value < 1
            ? (Math.Floor(d * 100) / 100).ToString("0.0#", CultureInfo.CurrentCulture) + "%"
            : (Math.Floor(d * 10) / 10).ToString("0.#", CultureInfo.CurrentCulture) + "%";
    }

    private static string FormatPlaytime(int minutes) => minutes switch
    {
        0 => "Nunca jogado",
        < 60 => $"{minutes} min",
        _ => $"{minutes / 60.0:0.#} h",
    };
}
