using WarTable.Api.Configuration;

namespace WarTable.Api.Tests;

public class DotEnvTests
{
	[Fact]
	public void Load_UsesDotNetEnvParsingAndPreservesExistingEnvironment()
	{
		var suffix = Guid.NewGuid().ToString("N");
		var alpha = $"WARTABLE_DOTENV_TEST_{suffix}__Alpha";
		var beta = $"WARTABLE_DOTENV_TEST_{suffix}__Beta";
		var gamma = $"WARTABLE_DOTENV_TEST_{suffix}__Gamma";
		var delta = $"WARTABLE_DOTENV_TEST_{suffix}__Delta";
		var epsilon = $"WARTABLE_DOTENV_TEST_{suffix}__Epsilon";
		var newKey = $"WARTABLE_DOTENV_TEST_{suffix}__Enabled";
		var existingKey = $"WARTABLE_DOTENV_TEST_{suffix}__Existing";
		var path = Path.Combine(Path.GetTempPath(), $"wartable-dotenv-{suffix}.env");

		File.WriteAllLines(path,
		[
			"# comment",
			"",
			$"export {alpha}=one",
			$"{beta}=\"two words\"",
			$"{gamma}='a # b'",
			$"{delta}=path\\to\\file",
			$"{epsilon}=\"line\\nnext\"",
			$"{newKey}=true",
			$"{existingKey}=from-file",
		]);
		Environment.SetEnvironmentVariable(existingKey, "from-process");

		try
		{
			var result = DotEnv.Load(path);

			Assert.True(result.Loaded);
			Assert.Equal(path, result.Path);
			Assert.Contains(newKey, result.Keys);
			Assert.Contains(existingKey, result.Keys);
			Assert.Equal("one", Environment.GetEnvironmentVariable(alpha));
			Assert.Equal("two words", Environment.GetEnvironmentVariable(beta));
			Assert.Equal("a # b", Environment.GetEnvironmentVariable(gamma));
			Assert.Equal("path\\to\\file", Environment.GetEnvironmentVariable(delta));
			Assert.Equal("line\nnext", Environment.GetEnvironmentVariable(epsilon));
			Assert.Equal("true", Environment.GetEnvironmentVariable(newKey));
			Assert.Equal("from-process", Environment.GetEnvironmentVariable(existingKey));
		}
		finally
		{
			foreach (var key in new[] { alpha, beta, gamma, delta, epsilon, newKey, existingKey })
				Environment.SetEnvironmentVariable(key, null);
			File.Delete(path);
		}
	}

	[Fact]
	public void Load_SkipVariableDisablesLoading()
	{
		var suffix = Guid.NewGuid().ToString("N");
		var key = $"WARTABLE_DOTENV_TEST_{suffix}__Skipped";
		var path = Path.Combine(Path.GetTempPath(), $"wartable-dotenv-{suffix}.env");
		File.WriteAllText(path, $"{key}=true");

		var previous = Environment.GetEnvironmentVariable("WARTABLE_SKIP_DOTENV");
		Environment.SetEnvironmentVariable("WARTABLE_SKIP_DOTENV", "1");

		try
		{
			var result = DotEnv.Load(path);

			Assert.False(result.Loaded);
			Assert.Empty(result.Keys);
			Assert.Null(Environment.GetEnvironmentVariable(key));
		}
		finally
		{
			Environment.SetEnvironmentVariable("WARTABLE_SKIP_DOTENV", previous);
			Environment.SetEnvironmentVariable(key, null);
			File.Delete(path);
		}
	}

	[Fact]
	public void Load_MissingExplicitFile_ReturnsWarning()
	{
		var path = Path.Combine(
			Path.GetTempPath(),
			$"wartable-missing-{Guid.NewGuid():N}.env");

		var result = DotEnv.Load(path);

		Assert.False(result.Loaded);
		Assert.Empty(result.Keys);
		Assert.NotEmpty(result.Warnings);
	}
}
