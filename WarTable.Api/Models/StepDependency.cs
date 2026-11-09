namespace WarTable.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Join row expressing "StepId depends on DependsOnStepId". Dependencies may cross
/// objectives within the same campaign, but never campaigns.
/// </summary>
public class StepDependency
{
    public Guid StepId { get; set; }
    public Guid DependsOnStepId { get; set; }

    [JsonIgnore]
    public Step? Step { get; set; }

    [JsonIgnore]
    public Step? DependsOnStep { get; set; }
}
