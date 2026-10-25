namespace AchievementsMP.Core.Domain;

/// <summary>
/// Estado de uma conquista para o jogador. <see cref="Unknown"/> é o padrão:
/// ausência de dado nunca vira "pendente" nem "desbloqueada".
/// </summary>
public enum UnlockState
{
    Unknown = 0,
    Locked = 1,
    Unlocked = 2,
}
