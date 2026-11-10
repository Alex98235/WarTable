using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WarTable.Api.Mcp;

/// <summary>
/// Shared serializer configuration for MCP tool arguments, tool results and the
/// generated tool schemas. Tool payloads use snake_case; protocol envelopes are
/// handled separately by the SDK.
/// </summary>
public static class McpJson
{
	public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
		// AIFunctionFactory marks the options read-only; without an explicit
		// resolver that throws on first tool discovery.
		TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
		Converters = { new JsonStringEnumConverter() },
	};
}
