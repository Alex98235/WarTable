namespace WarTable.Api.Models;

/// <summary>
/// One objective referenced by a sitrep, with a snapshot of its title and lane at
/// the time the sitrep was written. The snapshot keeps the timeline readable after
/// the objective is renamed or deleted.
/// </summary>
public class SitRepObjective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SitRepId { get; set; }

    /// <summary>Objective alias (<c>#N</c>) within the owning campaign.</summary>
    public int ObjectiveAlias { get; set; }

    public string TitleSnapshot { get; set; } = "";
    public string LaneAtWrite { get; set; } = "";
    public SitRepObjectiveKind Kind { get; set; }

    public SitRep? SitRep { get; set; }
}

public enum SitRepObjectiveKind { Completed, Blocked, Next }
