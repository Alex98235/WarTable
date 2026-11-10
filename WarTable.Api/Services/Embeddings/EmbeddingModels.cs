namespace WarTable.Api.Services.Embeddings;

/// <summary>Minimal contract for an OpenAI-compatible embedding endpoint.</summary>
public interface IEmbeddingClient
{
	Task<IReadOnlyList<float[]>> EmbedAsync(
		IReadOnlyList<string> inputs,
		CancellationToken cancellationToken = default);
}

public sealed class EmbeddingException : Exception
{
	public EmbeddingException(string message) : base(message) { }
	public EmbeddingException(string message, Exception inner) : base(message, inner) { }
}

public static class VectorMath
{
	public static double Dot(IReadOnlyList<float> a, IReadOnlyList<float> b)
	{
		var length = Math.Min(a.Count, b.Count);
		var sum = 0.0;
		for (var i = 0; i < length; i++)
			sum += (double)a[i] * b[i];
		return sum;
	}

	public static double Cosine(IReadOnlyList<float> a, IReadOnlyList<float> b)
	{
		if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
			return 0;

		var dot = 0.0;
		var normA = 0.0;
		var normB = 0.0;
		for (var i = 0; i < a.Count; i++)
		{
			dot += (double)a[i] * b[i];
			normA += (double)a[i] * a[i];
			normB += (double)b[i] * b[i];
		}

		if (normA <= 0 || normB <= 0)
			return 0;
		return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
	}

	public static void NormalizeInPlace(float[] vector)
	{
		var sum = 0.0;
		for (var i = 0; i < vector.Length; i++)
			sum += (double)vector[i] * vector[i];
		if (sum <= 0)
			return;

		var scale = (float)(1.0 / Math.Sqrt(sum));
		for (var i = 0; i < vector.Length; i++)
			vector[i] *= scale;
	}
}
