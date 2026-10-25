using AchievementsMP.Core.Domain;

namespace AchievementsMP.Core.Progress;

/// <summary>
/// Progresso de um jogo. Conquistas com estado desconhecido não contam como pendentes:
/// o percentual é um piso e <see cref="IsExact"/> diz se pode ser exibido como definitivo.
/// </summary>
public sealed record GameProgress(int Total, int Unlocked, int Locked, int Unknown)
{
    public static readonly GameProgress Empty = new(0, 0, 0, 0);

    public bool IsExact => Unknown == 0;

    public bool IsComplete => Total > 0 && Unlocked == Total;

    /// <summary>Percentual desbloqueado (0–100), ou null quando o jogo não tem conquistas.</summary>
    public double? Percent => Total == 0 ? null : Math.Round(100.0 * Unlocked / Total, 1);

    /// <summary>Quantas faltam com certeza. Não inclui as desconhecidas.</summary>
    public int KnownRemaining => Locked;
}

public static class ProgressCalculator
{
    /// <summary>
    /// Cruza o esquema com o estado do jogador. Conquistas do esquema sem estado correspondente ficam como desconhecidas;
    /// estados de conquistas que não existem no esquema são ignorados.
    /// </summary>
    public static GameProgress Calculate(
        IReadOnlyCollection<AchievementDefinition> schema,
        IEnumerable<PlayerAchievementState> states)
    {
        if (schema.Count == 0)
            return GameProgress.Empty;

        var byName = new Dictionary<string, UnlockState>(StringComparer.Ordinal);
        foreach (var s in states)
            byName[s.ApiName] = s.State;

        int unlocked = 0, locked = 0, unknown = 0;
        foreach (var def in schema.DistinctBy(d => d.ApiName, StringComparer.Ordinal))
        {
            switch (byName.GetValueOrDefault(def.ApiName, UnlockState.Unknown))
            {
                case UnlockState.Unlocked: unlocked++; break;
                case UnlockState.Locked: locked++; break;
                default: unknown++; break;
            }
        }

        return new GameProgress(unlocked + locked + unknown, unlocked, locked, unknown);
    }
}
