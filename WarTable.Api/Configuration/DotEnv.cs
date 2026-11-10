using DotNetEnv;

namespace WarTable.Api.Configuration;

/// <summary>Result of attempting to load a local <c>.env</c> file.</summary>
public sealed record DotEnvResult(
	string? Path,
	IReadOnlyList<string> Keys,
	IReadOnlyList<string> Warnings)
{
	public bool Loaded => Path is not null;
}

/// <summary>
/// Thin wrapper around <see cref="Env"/> for local development. The package
/// handles dotenv parsing and variable interpolation; this wrapper only decides
/// which file to load, preserves existing process variables, and surfaces
/// warnings. Loading happens before the ASP.NET host is built, so normal
/// configuration precedence still applies: existing environment variables win,
/// then <c>.env</c>, then appsettings.
/// </summary>
public static class DotEnv
{
	private const string FileVariable = "WARTABLE_ENV_FILE";
	private const string SkipVariable = "WARTABLE_SKIP_DOTENV";

	private static readonly LoadOptions Options = new(
		clobberExistingVars: false,
		onlyExactPath: true);

	/// <summary>
	/// Loads <paramref name="explicitPath"/> when supplied; otherwise searches
	/// for the nearest <c>.env</c> from the current directory and the application
	/// directory upward. Missing files are not an error for the automatic search.
	/// </summary>
	public static DotEnvResult Load(string? explicitPath = null)
	{
		if (IsTruthy(Environment.GetEnvironmentVariable(SkipVariable)))
			return new DotEnvResult(null, Array.Empty<string>(), Array.Empty<string>());

		var warnings = new List<string>();
		var path = ResolveFile(explicitPath, warnings);
		if (path is null)
			return new DotEnvResult(null, Array.Empty<string>(), warnings);

		if (!File.Exists(path))
		{
			warnings.Add($"Environment file not found: {path}");
			return new DotEnvResult(null, Array.Empty<string>(), warnings);
		}

		try
		{
			var values = Env.Load(path, Options).ToList();
			return new DotEnvResult(
				path,
				values.Select(value => value.Key).ToList(),
				warnings);
		}
		catch (Exception ex)
		{
			warnings.Add($"Could not load environment file {path}: {ex.Message}");
			return new DotEnvResult(null, Array.Empty<string>(), warnings);
		}
	}

	private static string? ResolveFile(string? explicitPath, ICollection<string> warnings)
	{
		if (!string.IsNullOrWhiteSpace(explicitPath))
			return ResolvePath(explicitPath, warnings);

		var configuredPath = Environment.GetEnvironmentVariable(FileVariable);
		if (!string.IsNullOrWhiteSpace(configuredPath))
			return ResolvePath(configuredPath, warnings);

		return FindNearest();
	}

	private static string? ResolvePath(string path, ICollection<string> warnings)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch (Exception ex)
		{
			warnings.Add($"Invalid environment file path '{path}': {ex.Message}");
			return null;
		}
	}

	private static string? FindNearest()
	{
		foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
		{
			DirectoryInfo? directory;
			try
			{
				directory = new DirectoryInfo(start);
			}
			catch
			{
				continue;
			}

			while (directory is not null)
			{
				var candidate = Path.Combine(directory.FullName, ".env");
				if (File.Exists(candidate))
					return candidate;
				directory = directory.Parent;
			}
		}

		return null;
	}

	private static bool IsTruthy(string? value)
		=> value is not null &&
		   (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
			value.Equals("on", StringComparison.OrdinalIgnoreCase));
}
