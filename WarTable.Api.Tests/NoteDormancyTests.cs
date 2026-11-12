using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Tests;

/// <summary>
/// Pure-function tests for the derived dormancy rule. No database involved: the
/// note and the live-link id sets are supplied directly.
/// </summary>
public class NoteDormancyTests
{
	private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
	private static readonly Guid ObjectiveId = Guid.NewGuid();
	private static readonly Guid StepId = Guid.NewGuid();
	private static readonly IReadOnlySet<Guid> LiveObjectives = new HashSet<Guid> { ObjectiveId };
	private static readonly IReadOnlySet<Guid> LiveSteps = new HashSet<Guid> { StepId };
	private static readonly IReadOnlySet<Guid> NoLiveObjectives = new HashSet<Guid>();
	private static readonly IReadOnlySet<Guid> NoLiveSteps = new HashSet<Guid>();

	private static BunkerNote Note(
		BunkerNoteKind kind = BunkerNoteKind.Gotcha,
		BunkerNoteStatus status = BunkerNoteStatus.Active,
		bool pinned = false,
		DateTime? updatedAt = null,
		DateTime? deletedAt = null,
		params BunkerNoteLink[] links) => new()
		{
			Id = Guid.NewGuid(),
			Kind = kind,
			Status = status,
			Pinned = pinned,
			UpdatedAt = updatedAt ?? Now.AddDays(-40),
			DeletedAt = deletedAt,
			Links = links.ToList(),
		};

	private static BunkerNoteLink LinkToObjective(Guid id) => new() { ObjectiveId = id };
	private static BunkerNoteLink LinkToStep(Guid id) => new() { StepId = id };

	[Fact]
	public void PinnedNote_IsExempt()
	{
		var note = Note(pinned: true);
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void Directive_IsExempt()
	{
		var note = Note(kind: BunkerNoteKind.Directive);
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void Decision_IsExempt()
	{
		var note = Note(kind: BunkerNoteKind.Decision);
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void SupersededNote_IsExempt()
	{
		var note = Note(status: BunkerNoteStatus.Superseded);
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void DeletedNote_IsNotDormant()
	{
		var note = Note(deletedAt: Now.AddDays(-10));
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Theory]
	[InlineData(BunkerNoteKind.Gotcha)]
	[InlineData(BunkerNoteKind.Context)]
	public void NoLiveLink_OldEnough_IsDormant(BunkerNoteKind kind)
	{
		var note = Note(kind);
		Assert.True(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void LiveObjectiveLink_StaysLive()
	{
		var note = Note(links: LinkToObjective(ObjectiveId));
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void LiveStepLink_StaysLive()
	{
		var note = Note(links: LinkToStep(StepId));
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void LinkToCompletedObjective_IsNotLive()
	{
		// A completed objective is absent from the live set, so the link is not live.
		var note = Note(links: LinkToObjective(ObjectiveId));
		Assert.True(NoteDormancy.IsDormant(note, Now, NoLiveObjectives, LiveSteps));
	}

	[Fact]
	public void LinkToDoneStep_IsNotLive()
	{
		var note = Note(links: LinkToStep(StepId));
		Assert.True(NoteDormancy.IsDormant(note, Now, LiveObjectives, NoLiveSteps));
	}

	[Fact]
	public void LinkToSoftDeletedObjective_IsNotLive()
	{
		var note = Note(links: LinkToObjective(ObjectiveId));
		Assert.True(NoteDormancy.IsDormant(note, Now, NoLiveObjectives, LiveSteps));
	}

	[Fact]
	public void UnderThirtyDays_StaysLive()
	{
		var note = Note(updatedAt: Now.AddDays(-29));
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void ExactlyThirtyDays_StaysLive()
	{
		// The rule is strictly older than the threshold; 30 days exactly is not dormant.
		var note = Note(updatedAt: Now.AddDays(-30));
		Assert.False(NoteDormancy.IsDormant(note, Now, LiveObjectives, LiveSteps));
	}

	[Fact]
	public void DormantDays_ReportsWholeDaysSinceUpdatedAt()
	{
		var note = Note(updatedAt: Now.AddDays(-40).AddHours(-6));
		Assert.Equal(40, NoteDormancy.DormantDays(note, Now));
	}

	[Fact]
	public void MarkDormancy_SetsFlags_OnlyWhenDormant()
	{
		var dormant = Note();
		NoteDormancy.MarkDormancy(dormant, Now, LiveObjectives, LiveSteps);
		Assert.True(dormant.Dormant);
		Assert.Equal(40, dormant.DormantDays);

		var live = Note(links: LinkToObjective(ObjectiveId));
		NoteDormancy.MarkDormancy(live, Now, LiveObjectives, LiveSteps);
		Assert.False(live.Dormant);
		Assert.Equal(0, live.DormantDays);
	}
}
