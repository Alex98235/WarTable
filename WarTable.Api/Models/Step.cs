namespace WarTable.Api.Models;

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

/// <summary>
/// One atomic, verifiable unit of work within an objective. Steps are ordered and
/// dependencies form a DAG; parallelism is derived from the graph.
/// </summary>
public class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectiveId { get; set; }

    /// <summary>Per-objective monotonic number. Displayed as <c>#N.M</c>. Never reused.</summary>
    public int StepNumber { get; set; }

    public string Title { get; set; } = "";

    /// <summary>Markdown: initial context and approach for this step.</summary>
    public string Context { get; set; } = "";

    /// <summary>Who should do the work, e.g. <c>"fixer"</c> or <c>"agent:fixer"</c>.</summary>
    public string Assignee { get; set; } = "";

    /// <summary>Who assigned the step. Defaults to the orchestrator.</summary>
    public string Assigner { get; set; } = "orchestrator";

    /// <summary>One of: <c>open</c>, <c>in-progress</c>, <c>done</c>, <c>flagged</c>, <c>blocked</c>.</summary>
    public string Status { get; set; } = "open";

    /// <summary>Display position within the objective. Reordering does not change StepNumber.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // ── Relationships ──
    [JsonIgnore]
    public Objective? Objective { get; set; }

    /// <summary>Edges where this step depends on another step.</summary>
    [JsonIgnore]
    public List<StepDependency> Dependencies { get; set; } = new();

    /// <summary>Edges where another step depends on this step.</summary>
    [JsonIgnore]
    public List<StepDependency> Dependents { get; set; } = new();

    public List<StepUpdate> Updates { get; set; } = new();
    public List<Evidence> Evidence { get; set; } = new();

    /// <summary>
    /// Lightweight dependency references populated by the service for serialization.
    /// The EF join entities are not serialized directly to avoid cycles.
    /// </summary>
    [NotMapped]
    public List<StepDependencyRef> DependsOn { get; set; } = new();
}

/// <summary>Serialization-friendly view of a step dependency.</summary>
public record StepDependencyRef(
    Guid StepId,
    Guid ObjectiveId,
    int ObjectiveAlias,
    int StepNumber,
    string Title,
    string Status);
