using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WarTable.Api.Options;

namespace WarTable.Api.Services.Embeddings;

/// <summary>
/// A retrieval model's prompt contract. Most open embedding models expect
/// different prefixes for queries and documents; keeping that here means the
/// external service can stay a dumb text-to-vector endpoint.
/// </summary>
public sealed record EmbeddingProfile(
	string Name,
	string Match,
	int Dimensions,
	int MaxInputTokens,
	string QueryTemplate,
	string DocumentTemplate,
	bool Normalize,
	string? Instruction = null);

/// <summary>Resolves the configured model to a profile and renders its templates.</summary>
public sealed class EmbeddingProfileProvider
{
	private static readonly EmbeddingProfile[] BuiltIn =
	[
		new(
			"embeddinggemma",
			"embeddinggemma",
			768,
			2048,
			"task: search result | query: {text}",
			"title: {title} | text: {text}",
			true),
		new(
			"bge",
			@"(^|[/_\-.])bge",
			768,
			512,
			"Represent this sentence for searching relevant passages: {text}",
			"{text}",
			true),
		new(
			"e5",
			@"(^|[/_\-.])e5",
			768,
			512,
			"query: {text}",
			"passage: {text}",
			true),
		new(
			"nomic",
			"nomic",
			768,
			8192,
			"search_query: {text}",
			"search_document: {text}",
			true),
		new(
			"qwen3-embedding",
			@"qwen3[-_]?embedding",
			1024,
			8192,
			"Instruct: {instruction}\nQuery: {text}",
			"{text}",
			true,
			"Given a search query, retrieve relevant passages that answer the query"),
		new(
			"openai-text-embedding-3",
			@"text-embedding-3",
			1536,
			8191,
			"{text}",
			"{text}",
			true),
		new("raw", ".*", 0, 512, "{text}", "{text}", true),
	];

	private readonly EmbeddingsOptions _options;

	public EmbeddingProfileProvider(
		IOptions<EmbeddingsOptions> options,
		ILogger<EmbeddingProfileProvider> logger)
	{
		_options = options.Value;
		Profile = Resolve(_options, logger, out var usedFallback);
		UsedFallback = usedFallback;
		Fingerprint = ComputeFingerprint(_options.Model, Profile);
	}

	public EmbeddingProfile Profile { get; }
	public bool UsedFallback { get; }
	public string Fingerprint { get; }

	public bool IsConfigured =>
		_options.Enabled &&
		!string.IsNullOrWhiteSpace(_options.Model) &&
		!string.IsNullOrWhiteSpace(_options.Endpoint);

	public string RenderQuery(string query, string? instruction = null)
		=> Render(Profile.QueryTemplate, query, instruction: instruction ?? Profile.Instruction);

	public string RenderDocument(
		string text,
		string? title = null,
		string? kind = null,
		string? tags = null,
		string? heading = null,
		string? campaign = null)
		=> Render(Profile.DocumentTemplate, text, title, kind, tags, heading, campaign);

	public static string Render(
		string template,
		string text,
		string? title = null,
		string? kind = null,
		string? tags = null,
		string? heading = null,
		string? campaign = null,
		string? instruction = null)
	{
		if (string.IsNullOrWhiteSpace(template))
			return text;

		return template
			.Replace("{text}", text, StringComparison.Ordinal)
			.Replace("{title}", title ?? "", StringComparison.Ordinal)
			.Replace("{kind}", kind ?? "", StringComparison.Ordinal)
			.Replace("{tags}", tags ?? "", StringComparison.Ordinal)
			.Replace("{heading}", heading ?? "", StringComparison.Ordinal)
			.Replace("{campaign}", campaign ?? "", StringComparison.Ordinal)
			.Replace("{instruction}", instruction ?? "", StringComparison.Ordinal);
	}

	private static EmbeddingProfile Resolve(
		EmbeddingsOptions options,
		ILogger logger,
		out bool usedFallback)
	{
		usedFallback = false;
		var model = options.Model ?? "";
		var custom = options.Profiles ?? new List<EmbeddingProfileOptions>();

		EmbeddingProfile? selected = null;
		if (!string.IsNullOrWhiteSpace(options.Profile))
		{
			var requested = options.Profile.Trim();
			var overrideProfile = custom.FirstOrDefault(p =>
				string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase));
			if (overrideProfile is not null)
			{
				selected = ToProfile(overrideProfile);
			}
			else
			{
				selected = BuiltIn.FirstOrDefault(p =>
					string.Equals(p.Name, requested, StringComparison.OrdinalIgnoreCase));
				if (selected is null)
					logger.LogWarning(
						"Embedding profile '{Profile}' was not found; falling back to model matching.",
						requested);
			}
		}

		if (selected is null)
		{
			foreach (var candidate in custom)
			{
				if (Matches(candidate.Match, model))
				{
					selected = ToProfile(candidate);
					break;
				}
			}
		}

		if (selected is null)
		{
			foreach (var candidate in BuiltIn)
			{
				if (Equals(candidate.Name, "raw") || Matches(candidate.Match, model))
				{
					selected = candidate;
					usedFallback = Equals(candidate.Name, "raw");
					break;
				}
			}
		}

		if (selected is null)
		{
			selected = BuiltIn[^1];
			usedFallback = true;
		}

		if (usedFallback)
		{
			logger.LogWarning(
				"No embedding profile matched model '{Model}'; using raw text templates. " +
				"Configure Embeddings:Profile or Embeddings:Profiles if the model needs prefixes.",
				model);
		}

		var dimensions = options.Dimensions > 0 ? options.Dimensions : selected.Dimensions;
		var maxInputTokens = options.MaxInputTokens > 0
			? options.MaxInputTokens
			: selected.MaxInputTokens;

		return selected with
		{
			Dimensions = dimensions,
			MaxInputTokens = maxInputTokens,
		};
	}

	private static EmbeddingProfile ToProfile(EmbeddingProfileOptions options)
	{
		var name = string.IsNullOrWhiteSpace(options.Name) ? "custom" : options.Name.Trim();
		var match = string.IsNullOrWhiteSpace(options.Match) ? Regex.Escape(name) : options.Match;
		return new EmbeddingProfile(
			name,
			match,
			options.Dimensions,
			options.MaxInputTokens > 0 ? options.MaxInputTokens : 512,
			string.IsNullOrWhiteSpace(options.QueryTemplate) ? "{text}" : options.QueryTemplate,
			string.IsNullOrWhiteSpace(options.DocumentTemplate) ? "{text}" : options.DocumentTemplate,
			options.Normalize,
			options.Instruction);
	}

	private static bool Matches(string? pattern, string model)
	{
		if (string.IsNullOrWhiteSpace(pattern))
			return false;
		try
		{
			return Regex.IsMatch(model, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static string ComputeFingerprint(string model, EmbeddingProfile profile)
	{
		var material = string.Join('\n',
			model,
			profile.Name,
			profile.Dimensions,
			profile.MaxInputTokens,
			profile.QueryTemplate,
			profile.DocumentTemplate,
			profile.Normalize,
			profile.Instruction ?? "");
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
	}
}
