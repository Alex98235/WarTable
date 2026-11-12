using WarTable.Api.Models;

namespace WarTable.Api.Services;

/// <summary>
/// Derived dormancy rule for Bunker notes. A note is dormant when nothing live
/// points at it any more. It is derived on read, never stored, never a column,
/// and requires no migration: a dormant note keeps its row, alias, links and
/// supersession chain.
/// </summary>
public static class NoteDormancy
{
	/// <summary>
	/// A note becomes dormant once it has been this long with no live link.
	/// This is a starting value to be tuned against real use, not a derived fact.
	/// </summary>
	public static readonly TimeSpan Threshold = TimeSpan.FromDays(30);

	/// <summary>
	/// Returns true when <paramref name="note"/> is dormant at <paramref name="now"/>.
	/// The live-link sets are the ids of objectives/steps that are not finished and
	/// not deleted; a link to anything outside those sets is not live.
	/// </summary>
	public static bool IsDormant(
		BunkerNote note,
		DateTime now,
		IReadOnlySet<Guid> liveObjectiveIds,
		IReadOnlySet<Guid> liveStepIds)
	{
		if (note.DeletedAt is not null) return false;
		if (note.Pinned) return false;
		if (note.Kind is BunkerNoteKind.Directive or BunkerNoteKind.Decision) return false;
		if (note.Status != BunkerNoteStatus.Active) return false;
		if (now - note.UpdatedAt <= Threshold) return false;

		foreach (var link in note.Links)
		{
			if (link.ObjectiveId is Guid objectiveId && liveObjectiveIds.Contains(objectiveId))
				return false;
			if (link.StepId is Guid stepId && liveStepIds.Contains(stepId))
				return false;
		}

		return true;
	}

	/// <summary>Whole days since <see cref="BunkerNote.UpdatedAt"/>, used to mark a dormant note's age.</summary>
	public static int DormantDays(BunkerNote note, DateTime now)
	{
		var age = now - note.UpdatedAt;
		return age <= TimeSpan.Zero ? 0 : (int)age.TotalDays;
	}

	/// <summary>
	/// Populates <see cref="BunkerNote.Dormant"/> and <see cref="BunkerNote.DormantDays"/>
	/// on <paramref name="note"/> when it is dormant; otherwise leaves them at their
	/// defaults (false, 0).
	/// </summary>
	public static void MarkDormancy(
		BunkerNote note,
		DateTime now,
		IReadOnlySet<Guid> liveObjectiveIds,
		IReadOnlySet<Guid> liveStepIds)
	{
		if (IsDormant(note, now, liveObjectiveIds, liveStepIds))
		{
			note.Dormant = true;
			note.DormantDays = DormantDays(note, now);
		}
	}
}
