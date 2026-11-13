using WarTable.Api.Services;

namespace WarTable.Api.Tests;

/// <summary>
/// A basis is a pointer, not a claim. These tests cover the shape check only;
/// whether an alias exists is resolved against a campaign by the service.
/// </summary>
public class NoteBasisTests
{
	[Theory]
	[InlineData("#12")]
	[InlineData("#3.2")]
	[InlineData("a1b2c3d")]
	[InlineData("A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4E5F6A1B2")]
	[InlineData("https://github.com/x/y")]
	[InlineData("  #12  ")]
	public void Validate_AcceptsPointers(string basis)
	{
		Assert.Null(NoteBasis.Validate(basis));
	}

	[Theory]
	[InlineData("v3.0.0")]
	[InlineData("the release tag")]
	[InlineData("#12.")]
	[InlineData("#abc")]
	[InlineData("abc123")]
	[InlineData("ftp://example.com")]
	[InlineData("")]
	public void Validate_RejectsNonPointers(string basis)
	{
		Assert.NotNull(NoteBasis.Validate(basis));
	}

	[Fact]
	public void Validate_NullMeansNotSupplied()
	{
		Assert.Null(NoteBasis.Validate(null));
	}

	[Fact]
	public void Validate_EmptyExplainsTheShape()
	{
		Assert.Equal(
			"A basis is a pointer to where a claim can be checked: " +
			"`#12` (objective or note alias), `#12.3` (a step), a 7-40 character commit sha, or an http(s) URL.",
			NoteBasis.Validate("   "));
	}

	[Fact]
	public void Validate_RejectionNamesTheValue()
	{
		Assert.Equal(
			"`v3.0.0` is not a citation. A basis is a pointer to where a claim can be checked: " +
			"`#12` (objective or note alias), `#12.3` (a step), a 7-40 character commit sha, or an http(s) URL.",
			NoteBasis.Validate("v3.0.0"));
	}
}
