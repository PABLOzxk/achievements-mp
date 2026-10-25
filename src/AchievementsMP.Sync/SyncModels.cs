using AchievementsMP.Core.Sync;
using AchievementsMP.Steam;

namespace AchievementsMP.Sync;

public enum SyncOutcome
{
    /// <summary>Tudo atualizado.</summary>
    Succeeded,
    /// <summary>Biblioteca atualizada, mas alguns jogos falharam (ficam com os dados anteriores).</summary>
    PartiallySucceeded,
    /// <summary>Nada foi atualizado (perfil privado, chave inválida, sem internet...). Dados anteriores intactos.</summary>
    Failed,
    Cancelled,
}

public enum SyncStage { Starting, Library, Games, Finished }

public sealed record SyncProgress(SyncStage Stage, int Done, int Total, string? CurrentGame);

public sealed record SyncResult(
    SyncOutcome Outcome,
    string Message,
    int GamesInLibrary = 0,
    int GamesChecked = 0,
    int GamesFailed = 0,
    SteamErrorKind? Error = null);

public sealed record SyncOptions
{
    /// <summary>Jogos processados em paralelo.</summary>
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>Intervalo mínimo entre duas chamadas à Steam (todas as threads somadas).</summary>
    public TimeSpan MinRequestSpacing { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Tentativas por chamada em erros transitórios (rede, 5xx, 429).</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>Espera antes da 2ª tentativa; dobra a cada nova tentativa.</summary>
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Espera padrão quando a Steam devolve 429 sem dizer quanto esperar.</summary>
    public TimeSpan DefaultRateLimitPause { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Se os primeiros N jogos consultados derem "privado" e nenhum der certo, os "Detalhes do jogo" estão privados:
    /// para tudo em vez de gastar centenas de chamadas.
    /// </summary>
    public int PrivateAbortThreshold { get; init; } = 5;

    public SyncPolicy Policy { get; init; } = SyncPolicy.Default;

    public static readonly SyncOptions Default = new();
}
