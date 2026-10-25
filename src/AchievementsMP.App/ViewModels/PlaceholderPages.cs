namespace AchievementsMP.App.ViewModels;

/// <summary>Página ainda não implementada: mostra o que virá e em que fase.</summary>
public abstract class PlaceholderPageViewModel : PageViewModel
{
    public abstract string Phase { get; }
    public abstract string Summary { get; }
    public abstract IReadOnlyList<string> Planned { get; }
}

public sealed class GameDetailsViewModel : PlaceholderPageViewModel
{
    public override string Title => "Detalhes do jogo";
    public override string Glyph => "";
    public override string Phase => "Fase 3 — Organização";
    public override string Summary => "Conquistas desbloqueadas e pendentes de um jogo, com raridade global.";
    public override IReadOnlyList<string> Planned =>
    [
        "Lista de conquistas com ícone, descrição e raridade",
        "Estado desconhecido mostrado como desconhecido, nunca como pendente",
        "Suas anotações por conquista",
        "Links para guias",
    ];
}

public sealed class PlannerViewModel : PlaceholderPageViewModel
{
    public override string Title => "Planejador de 100%";
    public override string Glyph => "";
    public override string Phase => "Fase 4 — Planejador e guias";
    public override string Summary => "Checklist e metas para fechar cada jogo, na ordem que você decidir.";
    public override IReadOnlyList<string> Planned =>
    [
        "Checklist pessoal por jogo",
        "Metas diárias ou semanais",
        "Prioridades configuráveis (perdível, rápida, demorada)",
        "Marcação manual de perdíveis: a Steam não fornece esse dado",
    ];
}
