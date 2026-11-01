namespace AchievementsMP.Xbox;

public enum XboxErrorKind
{
    /// <summary>Sem login da Microsoft guardado, ou o login venceu. Conectar de novo em Configurações.</summary>
    NotSignedIn,
    /// <summary>A conta Microsoft não tem perfil Xbox.</summary>
    NoXboxAccount,
    /// <summary>Conta de criança sem permissão, ou país onde o Xbox Live não funciona.</summary>
    AccountRestricted,
    /// <summary>O Xbox recusou o token (401/403).</summary>
    Unauthorized,
    NotFound,
    /// <summary>Limite de consultas atingido. Esperar e tentar de novo.</summary>
    RateLimited,
    /// <summary>Erro do lado do Xbox (5xx).</summary>
    Transient,
    /// <summary>Sem conexão, DNS, timeout.</summary>
    Network,
    InvalidResponse,
    Cancelled,
}

public sealed record XboxError(XboxErrorKind Kind, string Message, TimeSpan? RetryAfter = null)
{
    public bool IsRetryable => Kind is XboxErrorKind.RateLimited or XboxErrorKind.Transient or XboxErrorKind.Network;
}

/// <summary>Ou o valor, ou o motivo de não haver valor. Nunca "valor vazio" no lugar de erro.</summary>
public readonly record struct XboxResult<T>
{
    private XboxResult(T? value, XboxError? error) => (Value, Error) = (value, error);

    public T? Value { get; }
    public XboxError? Error { get; }
    public bool IsSuccess => Error is null;

    public static XboxResult<T> Ok(T value) => new(value, null);
    public static XboxResult<T> Fail(XboxError error) => new(default, error);
    public static XboxResult<T> Fail(XboxErrorKind kind, string message, TimeSpan? retryAfter = null) => new(default, new XboxError(kind, message, retryAfter));
}
