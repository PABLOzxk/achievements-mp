namespace AchievementsMP.Core.Domain;

/// <summary>
/// Valores da coluna <c>games.is_owned</c>. O nome da coluna é da v1; o valor 2 entrou sem migração para que versões
/// anteriores do app continuem abrindo o banco (elas leem "diferente de 0" como "na biblioteca").
/// </summary>
public enum Ownership
{
    /// <summary>Saiu da biblioteca (reembolso, troca de conta). Dados mantidos, jogo escondido.</summary>
    NotOwned = 0,
    /// <summary>Comprado ou resgatado pela própria conta (está no GetOwnedGames).</summary>
    Owned = 1,
    /// <summary>
    /// Jogado sem ser dono, quase sempre emprestado da Família Steam. As conquistas são da própria conta, então o jogo
    /// entra na biblioteca do app. Comprar o jogo depois troca para <see cref="Owned"/>.
    /// </summary>
    Family = 2,
}
