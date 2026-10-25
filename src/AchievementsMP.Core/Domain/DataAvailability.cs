namespace AchievementsMP.Core.Domain;

/// <summary>Por que os dados de um jogo estão (ou não) completos. Exibido na UI em vez de números inventados.</summary>
public enum DataAvailability
{
    NotSynced = 0,
    Available = 1,
    /// <summary>O jogo não tem conquistas no esquema.</summary>
    NoAchievements = 2,
    /// <summary>Perfil ou "detalhes do jogo" privados: a Steam não informa o progresso.</summary>
    PrivateProfile = 3,
    /// <summary>A Steam não devolveu esquema/estatísticas para este app.</summary>
    SchemaUnavailable = 4,
    /// <summary>Última tentativa falhou (rede, limite de consultas, etc.). Dados anteriores, se houver, continuam válidos.</summary>
    SyncFailed = 5,
}
