using AchievementsMP.Core.Planning;

namespace AchievementsMP.Core.Settings;

/// <summary>Preferências do usuário. A chave da API não fica aqui: ela é guardada criptografada via <see cref="ISecretStore"/>.</summary>
public sealed record AppSettings
{
    /// <summary>Texto que o usuário informou (SteamID64, URL do perfil ou nome personalizado).</summary>
    public string? SteamProfile { get; init; }

    /// <summary>SteamID64 resolvido a partir de <see cref="SteamProfile"/>, quando conhecido.</summary>
    public ulong? SteamId64 { get; init; }

    /// <summary>Idioma pedido à Steam para nomes e descrições (parâmetro "l"). Ex.: "brazilian", "english".</summary>
    public string Language { get; init; } = "brazilian";

    public bool SyncOnStartup { get; init; } = true;

    /// <summary>Intervalo mínimo entre sincronizações automáticas da biblioteca.</summary>
    public int SyncIntervalMinutes { get; init; } = 360;

    /// <summary>Ordem do planejador de 100%.</summary>
    public PlanStrategy PlanStrategy { get; init; } = PlanStrategy.Recommended;

    public static readonly AppSettings Default = new();
}
