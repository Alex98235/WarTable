using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WarTable.Api.Options;

namespace WarTable.Api.Services.Embeddings;

/// <summary>
/// Calls an OpenAI-compatible /v1/embeddings endpoint. The client intentionally
/// knows nothing about notes or the index; it only converts text to vectors.
/// </summary>
public sealed class OpenAiEmbeddingClient : IEmbeddingClient
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly HttpClient _http;
	private readonly EmbeddingsOptions _options;
	private readonly EmbeddingProfileProvider _profiles;

	public OpenAiEmbeddingClient(
		HttpClient http,
		IOptions<EmbeddingsOptions> options,
		EmbeddingProfileProvider profiles)
	{
		_http = http;
		_options = options.Value;
		_profiles = profiles;
	}

	public async Task<IReadOnlyList<float[]>> EmbedAsync(
		IReadOnlyList<string> inputs,
		CancellationToken cancellationToken = default)
	{
		if (inputs.Count == 0)
			return Array.Empty<float[]>();

		if (string.IsNullOrWhiteSpace(_options.Model))
			throw new EmbeddingException("Embeddings:Model is not configured.");
		if (string.IsNullOrWhiteSpace(_options.Endpoint))
			throw new EmbeddingException("Embeddings:Endpoint is not configured.");

		var dimensions = _options.Dimensions > 0 ? _options.Dimensions : (int?)null;
		object payload = dimensions.HasValue
			? new
			{
				model = _options.Model,
				input = inputs,
				encoding_format = "float",
				dimensions = dimensions.Value,
			}
			: new
			{
				model = _options.Model,
				input = inputs,
				encoding_format = "float",
			};

		using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(_options.Endpoint))
		{
			Content = JsonContent.Create(payload),
		};
		if (!string.IsNullOrWhiteSpace(_options.ApiKey))
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

		using var response = await _http.SendAsync(request, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new EmbeddingException(
				$"Embedding service returned {(int)response.StatusCode}: {Trim(body, 500)}");
		}

		EmbeddingResponseDto? parsed;
		try
		{
			parsed = JsonSerializer.Deserialize<EmbeddingResponseDto>(body, JsonOptions);
		}
		catch (JsonException ex)
		{
			throw new EmbeddingException("Embedding service returned invalid JSON.", ex);
		}

		var data = parsed?.Data;
		if (data is null || data.Count != inputs.Count)
		{
			throw new EmbeddingException(
				$"Embedding service returned {data?.Count ?? 0} vectors for {inputs.Count} inputs.");
		}

		var vectors = new List<float[]>(data.Count);
		foreach (var item in data.OrderBy(d => d.Index ?? 0))
		{
			if (item.Embedding is null || item.Embedding.Length == 0)
				throw new EmbeddingException("Embedding service returned an empty vector.");

			var vector = item.Embedding;
			if (_profiles.Profile.Normalize)
				VectorMath.NormalizeInPlace(vector);
			vectors.Add(vector);
		}

		var firstLength = vectors[0].Length;
		if (vectors.Any(v => v.Length != firstLength))
			throw new EmbeddingException("Embedding service returned vectors with inconsistent dimensions.");

		return vectors;
	}

	private static string BuildEndpoint(string endpoint)
	{
		var trimmed = endpoint.Trim().TrimEnd('/');
		if (trimmed.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
			return trimmed;
		return trimmed + "/embeddings";
	}

	private static string Trim(string value, int max)
		=> value.Length <= max ? value : value[..max] + "…";

	private sealed class EmbeddingResponseDto
	{
		public List<EmbeddingDataDto>? Data { get; set; }
	}

	private sealed class EmbeddingDataDto
	{
		public int? Index { get; set; }
		public float[]? Embedding { get; set; }
	}
}
