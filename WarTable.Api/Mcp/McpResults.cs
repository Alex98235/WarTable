using ModelContextProtocol.Protocol;

namespace WarTable.Api.Mcp;

/// <summary>Helpers for building MCP tool results.</summary>
public static class McpResults
{
	/// <summary>
	/// Builds the explicit tool-error envelope: one text block containing the
	/// message, flagged as an error. Matches the old hand-rolled protocol.
	/// </summary>
	public static CallToolResult Error(string message) => new()
	{
		IsError = true,
		Content = [new TextContentBlock { Text = message }],
	};
}

/// <summary>
/// Tolerant argument helpers preserving the old hand-written protocol's parsing:
/// invalid GUIDs become null and invalid array entries are skipped.
/// </summary>
internal static class McpArgs
{
	/// <summary>Blank actors normalize to <c>agent:unknown</c>, as before.</summary>
	public static string Actor(string? actor)
		=> string.IsNullOrWhiteSpace(actor) ? "agent:unknown" : actor;

	public static Guid? Guid(string? raw)
		=> raw is not null && System.Guid.TryParse(raw, out var value) ? value : null;

	/// <summary>Returns null when the array is absent; invalid entries are skipped.</summary>
	public static Guid[]? Guids(IEnumerable<string>? raw)
	{
		if (raw is null)
			return null;

		var values = new List<Guid>();
		foreach (var item in raw)
		{
			if (System.Guid.TryParse(item, out var parsed))
				values.Add(parsed);
		}

		return values.ToArray();
	}

	/// <summary>Returns reorder keys that are not GUIDs; callers reject these first.</summary>
	public static string[] InvalidOrderKeys(Dictionary<string, int>? raw)
		=> raw is null
			? Array.Empty<string>()
			: raw.Keys.Where(key => !System.Guid.TryParse(key, out _)).ToArray();

	/// <summary>Parses reorder payload keys; non-GUID keys are skipped.</summary>
	public static Dictionary<Guid, int> Orders(Dictionary<string, int>? raw)
	{
		var orders = new Dictionary<Guid, int>();
		if (raw is null)
			return orders;

		foreach (var entry in raw)
		{
			if (System.Guid.TryParse(entry.Key, out var id))
				orders[id] = entry.Value;
		}

		return orders;
	}
}
