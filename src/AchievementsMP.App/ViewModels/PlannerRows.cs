using AchievementsMP.Core.Domain;
using AchievementsMP.Core.Library;
using AchievementsMP.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AchievementsMP.App.ViewModels;

/// <summary>Jogo no seletor do planejador e do formulário de metas.</summary>
public sealed record GameChoice(long? AppId, string Label, bool IsPlanned)
{
    public override string ToString() => Label;

    public static GameChoice From(LibraryGame g, bool isPlanned)
    {
        var p = g.Progress;
        var progress = p.Total > 0 ? $"  ·  {p.Unlocked} de {p.Total}" : "";
        return new GameChoice(g.AppId, (isPlanned ? "● " : "") + g.Name + progress, isPlanned);
    }
}

/// <summary>Linha da lista do plano: cabeçalho de grupo ou conquista.</summary>
public abstract record PlanRow
{
    public abstract bool IsHeader { get; }
}

public sealed record PlanHeaderRow(PlanGroup Group, string Title, string Hint) : PlanRow
{
    public override bool IsHeader => true;
    public bool IsMissable => Group == PlanGroup.Missable;

    public static PlanHeaderRow For(PlanGroup group, int count) => group switch
    {
        PlanGroup.Missable => new(group, $"PERDÍVEIS · {count}", "Faça antes do ponto sem volta. Confira a fonte que você anotou."),
        PlanGroup.Quick => new(group, $"RÁPIDAS · {count}", "Marcadas por você como rápidas."),
        PlanGroup.Long => new(group, $"DEMORADAS · {count}", "Desafios longos, colecionáveis, grind. Planeje com calma."),
        PlanGroup.All => new(group, $"PENDENTES · {count}", "Só pela raridade: da mais comum para a mais rara."),
        _ => new(group, $"SEM CLASSIFICAÇÃO · {count}", "Da mais comum para a mais rara. Raridade não é tempo: classifique para o plano ficar melhor."),
    };
}

public sealed record PlanAchievementRow(
    string ApiName,
    string Name,
    string Description,
    string? IconUrl,
    string RarityText,
    Rarity Rarity,
    bool IsMissable,
    string? EffortLabel,
    string? PriorityLabel,
    bool IsHighPriority,
    string? Requirement,
    bool HasNote,
    string? GuidesText,
    string? TasksText) : PlanRow
{
    public override bool IsHeader => false;

    public static PlanAchievementRow From(PlanEntry e, int guideCount, (int Done, int Total) tasks)
    {
        var a = e.Achievement;
        var (rarity, rarityText) = AchievementItem.DescribeRarity(a.GlobalPercent);
        var description = a.Description ?? (a.Hidden ? "Conquista oculta: a Steam não mostra a descrição até você desbloquear." : "Sem descrição.");
        return new PlanAchievementRow(
            a.ApiName, a.DisplayName, description, a.IconGrayUrl ?? a.IconUrl, rarityText, rarity,
            e.Plan.Missable == Missable.Yes,
            e.Plan.Effort switch { Effort.Quick => "Rápida", Effort.Long => "Demorada", _ => null },
            e.Plan.Priority switch { Priority.High => "Alta prioridade", Priority.Low => "Baixa prioridade", _ => null },
            e.Plan.Priority == Priority.High,
            e.Plan.Requirement is { } r ? $"Requisito: {r}" : null,
            e.Plan.Note is not null,
            guideCount switch { 0 => null, 1 => "1 guia", _ => $"{guideCount} guias" },
            tasks.Total == 0 ? null : $"Checklist {tasks.Done}/{tasks.Total}");
    }
}

/// <summary>Link de guia, do jogo ou de uma conquista.</summary>
public sealed record GuideRow(long Id, string Name, Uri Url, string? ApiName)
{
    public string UrlText => Url.AbsoluteUri;

    public static GuideRow From(Guide g) => new(g.Id, GuideLink.DisplayName(g.Url, g.Title), g.Url, g.ApiName);
}

/// <summary>Tarefa do checklist do jogo ou de uma conquista. Marcar a caixa grava na hora (com a data).</summary>
public sealed class TaskRow : ObservableObject
{
    private readonly Func<TaskRow, bool, Task> _onToggle;
    private bool _isDone;

    public TaskRow(PlanTask task, Func<TaskRow, bool, Task> onToggle)
    {
        Id = task.Id;
        ApiName = task.ApiName;
        Title = task.Title;
        DoneText = task.DoneUtc is { } d ? $"feita em {d.ToLocalTime():dd/MM/yyyy}" : null;
        _isDone = task.IsDone;
        _onToggle = onToggle;
    }

    public long Id { get; }
    /// <summary>Conquista dona da tarefa; nulo quando é do jogo inteiro.</summary>
    public string? ApiName { get; }
    public bool IsGameTask => ApiName is null;
    public string Title { get; }
    public string? DoneText { get; }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (SetProperty(ref _isDone, value))
                _ = _onToggle(this, value);
        }
    }
}

/// <summary>Formulário de classificação de uma conquista. Só vai para o banco ao salvar.</summary>
public sealed partial class AchievementEditor : ObservableObject
{
    public static IReadOnlyList<Option<Effort>> EffortOptions { get; } =
    [
        new(Core.Planning.Effort.Unknown, "Não sei"),
        new(Core.Planning.Effort.Quick, "Rápida"),
        new(Core.Planning.Effort.Long, "Demorada"),
    ];

    public static IReadOnlyList<Option<Missable>> MissableOptions { get; } =
    [
        new(Core.Planning.Missable.Unknown, "Não sei"),
        new(Core.Planning.Missable.Yes, "Sim, pode ser perdida"),
        new(Core.Planning.Missable.No, "Não, dá para fazer depois"),
    ];

    public static IReadOnlyList<Option<Priority>> PriorityOptions { get; } =
    [
        new(Core.Domain.Priority.High, "Alta"),
        new(Core.Domain.Priority.Normal, "Normal"),
        new(Core.Domain.Priority.Low, "Baixa"),
    ];

    private AchievementPlan _saved;

    public AchievementEditor(PlanAchievementRow row, AchievementPlan plan, IReadOnlyList<GuideRow> guides)
    {
        AppId = plan.AppId;
        ApiName = plan.ApiName;
        Name = row.Name;
        Description = row.Description;
        IconUrl = row.IconUrl;
        RarityText = row.RarityText;
        Rarity = row.Rarity;
        _saved = plan.Normalized();
        Load(_saved);
        Guides = guides;
    }

    public long AppId { get; }
    public string ApiName { get; }
    public string Name { get; }
    public string Description { get; }
    public string? IconUrl { get; }
    public string RarityText { get; }
    public Rarity Rarity { get; }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsDirty))] private Option<Effort>? _effort;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsDirty))] private Option<Missable>? _missable;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsDirty))] private Option<Priority>? _priority;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsDirty))] private string _requirement = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsDirty))] private string _source = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(IsDirty))] private string _note = "";

    [ObservableProperty] private IReadOnlyList<GuideRow> _guides = [];
    [ObservableProperty] private string _newGuideUrl = "";
    [ObservableProperty] private string? _guideError;

    // Checklist desta conquista: grava na hora, como os guias, e não conta como alteração por salvar.
    [ObservableProperty] private IReadOnlyList<TaskRow> _tasks = [];
    [ObservableProperty] private string _taskSummary = "";
    [ObservableProperty] private string _newTaskTitle = "";

    public bool IsDirty => ToPlan() != _saved;

    public AchievementPlan ToPlan() => new AchievementPlan(
        AppId, ApiName, Effort?.Value ?? Core.Planning.Effort.Unknown, Missable?.Value ?? Core.Planning.Missable.Unknown,
        Priority?.Value ?? Core.Domain.Priority.Normal, Requirement, Source, Note).Normalized();

    public void MarkSaved(AchievementPlan plan)
    {
        _saved = plan.Normalized();
        OnPropertyChanged(nameof(IsDirty));
    }

    private void Load(AchievementPlan plan)
    {
        Effort = EffortOptions.First(o => o.Value == plan.Effort);
        Missable = MissableOptions.First(o => o.Value == plan.Missable);
        Priority = PriorityOptions.First(o => o.Value == plan.Priority);
        Requirement = plan.Requirement ?? "";
        Source = plan.Source ?? "";
        Note = plan.Note ?? "";
    }
}
