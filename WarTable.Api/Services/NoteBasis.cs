using System.Globalization;
using System.Text.RegularExpressions;

namespace WarTable.Api.Services;

/// <summary>
/// A basis is a pointer, not a claim: it has to name something a reader can
/// open. Shape is checked here; whether #N or #N.M actually exists needs the
/// campaign, so the caller resolves it.
/// </summary>
public static partial class NoteBasis
{
	[GeneratedRegex(@"^#(?<alias>\d+)(?:\.(?<step>\d+))?$")]
	private static partial Regex AliasForm();

	[GeneratedRegex(@"^[0-9a-fA-F]{7,40}$")]
	private static partial Regex CommitForm();

	private const string Shape =
		"`#12` (objective or note alias), `#12.3` (a step), a 7-40 character commit sha, or an http(s) URL";

	/// <summary>Null when the basis is a usable pointer; otherwise the reason it is not.</summary>
	public static string? Validate(string? basis)
	{
		if (basis is null)
			return null;

		var value = basis.Trim();
		if (value.Length == 0)
			return $"A basis is a pointer to where a claim can be checked: {Shape}.";

		if (IsUrl(value) || IsCommit(value) || TryParseAlias(value, out _, out _))
			return null;

		return $"`{value}` is not a citation. A basis is a pointer to where a claim can be checked: {Shape}.";
	}

	public static bool IsUrl(string value) =>
		Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
		(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

	public static bool IsCommit(string value) => CommitForm().IsMatch(value);

	public static bool TryParseAlias(string value, out int alias, out int? step)
	{
		alias = 0;
		step = null;

		var match = AliasForm().Match(value);
		if (!match.Success)
			return false;

		if (!int.TryParse(
				match.Groups["alias"].Value,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out alias))
		{
			return false;
		}

		if (match.Groups["step"].Success)
		{
			if (!int.TryParse(
					match.Groups["step"].Value,
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out var stepNumber))
			{
				return false;
			}

			step = stepNumber;
		}

		return true;
	}
}
