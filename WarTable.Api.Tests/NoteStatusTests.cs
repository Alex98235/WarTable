using WarTable.Api.Models;

namespace WarTable.Api.Tests;

/// <summary>
/// A note status is lifecycle, not outcome. Resolved meant "the question was
/// answered" or "the gotcha was fixed"; questions are gone and a gotcha that no
/// longer applies falls dormant on its own, so nothing produced it.
/// </summary>
public class NoteStatusTests
{
	[Fact]
	public void Enum_ContainsOnlyActiveAndSuperseded()
	{
		var names = Enum.GetNames<BunkerNoteStatus>();
		Assert.Equal(2, names.Length);
		Assert.Contains("Active", names);
		Assert.Contains("Superseded", names);
		Assert.DoesNotContain("Resolved", names);
	}

	[Fact]
	public void Active_IsDefaultValue()
	{
		Assert.Equal(0, (int)BunkerNoteStatus.Active);
		Assert.Equal(1, (int)BunkerNoteStatus.Superseded);
	}
}
