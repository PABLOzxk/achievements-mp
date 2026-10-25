namespace AchievementsMP.Steam;

public enum SteamErrorKind
{
    /// <summary>Chave ausente, inválida ou revogada.</summary>
    InvalidApiKey,
    /// <summary>Perfil privado ou "detalhes do jogo" privados. Não é erro transitório: repetir não resolve.</summary>
    PrivateProfile,
    /// <summary>Perfil, nome personalizado ou app inexistente.</summary>
    NotFound,
    /// <summary>O app não tem estatísticas/conquistas na Steam.</summary>
    NoStats,
    /// <summary>Limite de consultas atingido. Esperar e tentar de novo.</summary>
    RateLimited,
    /// <summary>Erro do lado da Steam (5xx). Tentar de novo mais tarde.</summary>
    Transient,
    /// <summary>Sem conexão, DNS, timeout.</summary>
    Network,
    /// <summary>Resposta em formato inesperado.</summary>
    InvalidResponse,
    Cancelled,
}

public sealed record SteamError(SteamErrorKind Kind, string Message, TimeSpan? RetryAfter = null)
{
    /// <summary>Vale a pena repetir automaticamente (com espera)?</summary>
    public bool IsRetryable => Kind is SteamErrorKind.RateLimited or SteamErrorKind.Transient or SteamErrorKind.Network;
}

/// <summary>Resultado de uma chamada: ou o valor, ou o motivo de não haver valor. Nunca "valor vazio" no lugar de erro.</summary>
public readonly record struct SteamResult<T>
{
    private SteamResult(T? value, SteamError? error) => (Value, Error) = (value, error);

    public T? Value { get; }
    public SteamError? Error { get; }
    public bool IsSuccess => Error is null;

    public static SteamResult<T> Ok(T value) => new(value, null);
    public static SteamResult<T> Fail(SteamError error) => new(default, error);
    public static SteamResult<T> Fail(SteamErrorKind kind, string message, TimeSpan? retryAfter = null) => new(default, new SteamError(kind, message, retryAfter));

    public SteamResult<TOut> Map<TOut>(Func<T, TOut> map) => IsSuccess ? SteamResult<TOut>.Ok(map(Value!)) : SteamResult<TOut>.Fail(Error!);
}
