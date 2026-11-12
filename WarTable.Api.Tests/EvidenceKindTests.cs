using WarTable.Api.Models;

namespace WarTable.Api.Tests;

/// <summary>
/// EvidenceKind is a pointer the reader can dereference, nothing else.
/// File, Test, Screenshot, and Other are removed — they either name a medium
/// WarTable does not store or cannot be followed at all.
/// </summary>
public class EvidenceKindTests
{
	[Fact]
	public void Enum_ContainsOnlyLinkAndCommit()
	{
		var names = Enum.GetNames<EvidenceKind>();
		Assert.Equal(2, names.Length);
		Assert.Contains("Link", names);
		Assert.Contains("Commit", names);
	}

	[Fact]
	public void Link_IsDefaultValue()
	{
		Assert.Equal(0, (int)EvidenceKind.Link);
		Assert.Equal(1, (int)EvidenceKind.Commit);
	}

	[Fact]
	public void RemovedKinds_AreNotInEnum()
	{
		foreach (var name in new[] { "File", "Test", "Screenshot", "Other" })
		{
			Assert.False(Enum.IsDefined(typeof(EvidenceKind), name),
				$"EvidenceKind.{name} should have been removed");
		}
	}

	/// <summary>Commit kind validation: rejects non-sha prose values.</summary>
	[Fact]
	public void IsValidCommitSha_RejectsProse()
	{
		var prose = "Fixed the bug by updating the connection string";
		var valid = IsValidCommitSha(prose);
		Assert.False(valid);
	}

	/// <summary>Commit kind validation: rejects too-short values.</summary>
	[Fact]
	public void IsValidCommitSha_RejectsTooShort()
	{
		Assert.False(IsValidCommitSha("abc"));
	}

	/// <summary>Commit kind validation: rejects too-long values.</summary>
	[Fact]
	public void IsValidCommitSha_RejectsTooLong()
	{
		Assert.False(IsValidCommitSha(new string('a', 41)));
	}

	/// <summary>Commit kind validation: rejects non-hex characters.</summary>
	[Fact]
	public void IsValidCommitSha_RejectsNonHex()
	{
		Assert.False(IsValidCommitSha("abcdefghijklmno")); // 'g' is not hex
	}

	/// <summary>Commit kind validation: accepts valid 7-char sha.</summary>
	[Fact]
	public void IsValidCommitSha_AcceptsShortSha()
	{
		Assert.True(IsValidCommitSha("abc1234"));
	}

	/// <summary>Commit kind validation: accepts valid 40-char sha.</summary>
	[Fact]
	public void IsValidCommitSha_AcceptsFullSha()
	{
		Assert.True(IsValidCommitSha("abcdef0123456789abcdef0123456789abcdef01"));
	}

	/// <summary>Commit kind validation: accepts uppercase hex.</summary>
	[Fact]
	public void IsValidCommitSha_AcceptsUppercase()
	{
		Assert.True(IsValidCommitSha("ABCDEF0123456789"));
	}

	/// <summary>Link kind validation: rejects relative paths.</summary>
	[Fact]
	public void IsValidUrl_RejectsRelativePath()
	{
		Assert.False(IsValidUrl("/relative/path/to/file.txt"));
	}

	/// <summary>Link kind validation: rejects file:// scheme.</summary>
	[Fact]
	public void IsValidUrl_RejectsFileScheme()
	{
		Assert.False(IsValidUrl("file:///c:/temp/evidence.png"));
	}

	/// <summary>Link kind validation: rejects plain text.</summary>
	[Fact]
	public void IsValidUrl_RejectsPlainText()
	{
		Assert.False(IsValidUrl("not a url at all"));
	}

	/// <summary>Link kind validation: accepts https URL.</summary>
	[Fact]
	public void IsValidUrl_AcceptsHttps()
	{
		Assert.True(IsValidUrl("https://ci.example.com/build/42"));
	}

	/// <summary>Link kind validation: accepts http URL.</summary>
	[Fact]
	public void IsValidUrl_AcceptsHttp()
	{
		Assert.True(IsValidUrl("http://deployed.example.com/instance"));
	}

	/// <summary>Matches the AddEvidenceAsync Commit validation predicate.</summary>
	private static bool IsValidCommitSha(string value) =>
		value.Length >= 7 && value.Length <= 40 &&
		value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

	/// <summary>Matches the AddEvidenceAsync Link validation predicate.</summary>
	private static bool IsValidUrl(string value) =>
		Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
		(uri.Scheme == "http" || uri.Scheme == "https");
}
