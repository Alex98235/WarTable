namespace WarTable.Api.Options;

/// <summary>
/// Configuration for the optional semantic knowledge index. WarTable talks to
/// any service that implements the OpenAI-compatible /v1/embeddings contract;
/// the service is stateless and never owns the index.
/// </summary>
public sealed class EmbeddingsOptions
{
	public const string SectionName = "Embeddings";

	public bool Enabled { get; set; }

	/// <summary>Base URL, usually ending in /v1. A full /embeddings URL also works.</summary>
	public string Endpoint { get; set; } = "http://localhost:11434/v1";

	public string? ApiKey { get; set; }

	public string Model { get; set; } = "";

	/// <summary>Optional built-in or custom profile name. Otherwise matched against the model id.</summary>
	public string? Profile { get; set; }

	/// <summary>Expected vector size. Zero lets the service decide on the first index pass.</summary>
	public int Dimensions { get; set; }

	/// <summary>Chunk size hint. Zero uses the profile default.</summary>
	public int MaxInputTokens { get; set; }

	public int BatchSize { get; set; } = 32;

	public int TimeoutSeconds { get; set; } = 30;

	/// <summary>How often the background worker reconciles notes into the sidecar index.</summary>
	public int PollSeconds { get; set; } = 30;

	public int TopK { get; set; } = 8;

	public double MinScore { get; set; } = 0.2;

	/// <summary>Defaults to wartable.embeddings.db beside the main database.</summary>
	public string? SidecarPath { get; set; }

	public List<EmbeddingProfileOptions> Profiles { get; set; } = new();
}

/// <summary>User-supplied profile override or addition.</summary>
public sealed class EmbeddingProfileOptions
{
	public string Name { get; set; } = "";
	public string Match { get; set; } = "";
	public int Dimensions { get; set; }
	public int MaxInputTokens { get; set; }
	public string QueryTemplate { get; set; } = "{text}";
	public string DocumentTemplate { get; set; } = "{text}";
	public bool Normalize { get; set; } = true;
	public string? Instruction { get; set; }
}
