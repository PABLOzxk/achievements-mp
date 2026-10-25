namespace AchievementsMP.Core.Progress;

/// <summary>
/// Quão viável é fechar 100%, medido pela raridade global da conquista mais rara que falta.
/// É um indicador, não uma verdade: raridade baixa também acontece quando muita gente abandona o jogo cedo.
/// </summary>
public enum Viability
{
    /// <summary>Faltam dados (estado ou raridade desconhecidos) ou o jogo não tem conquistas.</summary>
    Unknown = 0,
    /// <summary>A mais rara que falta foi obtida por menos de 1% dos jogadores.</summary>
    VeryLow = 1,
    /// <summary>Entre 1% e 5%.</summary>
    Low = 2,
    /// <summary>Entre 5% e 20%.</summary>
    Medium = 3,
    /// <summary>20% ou mais: todas as que faltam são comuns.</summary>
    High = 4,
    /// <summary>Já está em 100%.</summary>
    Done = 5,
}

public sealed record CompletionOutlook(int Remaining, double? RarestRemainingPercent, Viability Viability)
{
    public const double HighThreshold = 20, MediumThreshold = 5, LowThreshold = 1;

    public static readonly CompletionOutlook None = new(0, null, Viability.Unknown);

    /// <param name="remainingWithoutRarity">Conquistas pendentes sem percentual global conhecido.</param>
    public static CompletionOutlook From(GameProgress progress, double? rarestRemainingPercent, int remainingWithoutRarity)
    {
        if (progress.IsComplete)
            return new(0, null, Viability.Done);
        if (progress.Total == 0 || !progress.IsExact || remainingWithoutRarity > 0 || rarestRemainingPercent is null)
            return new(progress.KnownRemaining, rarestRemainingPercent, Viability.Unknown);

        var viability = rarestRemainingPercent.Value switch
        {
            >= HighThreshold => Viability.High,
            >= MediumThreshold => Viability.Medium,
            >= LowThreshold => Viability.Low,
            _ => Viability.VeryLow,
        };
        return new(progress.KnownRemaining, rarestRemainingPercent, viability);
    }
}
