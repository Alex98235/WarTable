using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using WarTable.Api.Mcp.Tools;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api.Tests;

/// <summary>
/// Tests that the registered MCP tool surface for Bunker notes matches the
/// expected shape: the wide <c>add_note</c> and <c>add_reference</c> are removed,
/// and four per-kind tools are present with the correct required parameters.
/// </summary>
public class McpToolSurfaceTests
{
	/// <summary>Expected per-kind add tools, each with its required (non-nullable) parameters.</summary>
	private static readonly Dictionary<string, string[]> ExpectedAddTools = new()
	{
		["add_decision"] = ["campaign_id", "title", "chosen", "rejected", "reason", "basis"],
		["add_gotcha"] = ["campaign_id", "title", "trap", "escape"],
		["add_context"] = ["campaign_id", "title", "links"],
		["add_directive"] = ["campaign_id", "title", "body"],
	};

	[Fact]
	public void AddNote_IsAbsent()
	{
		var tools = GetMcpTools();
		Assert.DoesNotContain(tools, t => t.Name == "add_note");
	}

	[Fact]
	public void AddReference_IsAbsent()
	{
		var tools = GetMcpTools();
		Assert.DoesNotContain(tools, t => t.Name == "add_reference");
	}

	[Fact]
	public void FourPerKindTools_ArePresent()
	{
		var tools = GetMcpTools();
		foreach (var expectedName in ExpectedAddTools.Keys)
		{
			Assert.Contains(tools, t => t.Name == expectedName);
		}
	}

	[Fact]
	public void Decision_HasChosenRejectedReasonBasisAsRequired()
	{
		AssertRequired("add_decision", "chosen", "rejected", "reason", "basis");
	}

	[Fact]
	public void Gotcha_HasTrapEscapeAsRequired()
	{
		AssertRequired("add_gotcha", "trap", "escape");
	}

	[Fact]
	public void Context_HasLinksAsRequired()
	{
		AssertRequired("add_context", "links");
	}

	[Fact]
	public void Directive_HasBodyAsRequired()
	{
		AssertRequired("add_directive", "body");
	}

	/// <summary>
	/// Every tool that takes a <c>row_version</c> declares it required, so a client
	/// planning a write sees the dependency in the schema instead of learning it
	/// from an error after the call.
	/// </summary>
	[Fact]
	public void RowVersion_IsRequiredOnEveryWriteTool()
	{
		var toolsWithToken = ToolTypes()
			.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
			.Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
			.Where(x => x.Attribute is not null)
			.Select(x => (
				Name: x.Attribute!.Name ?? x.Method.Name,
				Parameter: x.Method.GetParameters().SingleOrDefault(p => p.Name == "row_version")))
			.Where(x => x.Parameter is not null)
			.ToList();

		Assert.Equal(10, toolsWithToken.Count);

		foreach (var (name, parameter) in toolsWithToken)
		{
			Assert.False(parameter!.HasDefaultValue, $"{name} must require row_version");
			var nullability = new NullabilityInfoContext().Create(parameter);
			Assert.Equal(NullabilityState.NotNull, nullability.WriteState);
		}
	}

	/// <summary>Every type in the MCP tools namespace, so a new tool cannot escape the scan.</summary>
	private static IEnumerable<Type> ToolTypes() =>
		typeof(BunkerTools).Assembly.GetTypes()
			.Where(type => type.Namespace == typeof(BunkerTools).Namespace);

	/// <summary>Asserts a named MCP tool has exactly the named non-nullable parameters.</summary>
	private static void AssertRequired(string toolName, params string[] expectedRequired)
	{
		var tool = GetMcpTools().Single(t => t.Name == toolName);
		var required = new HashSet<string>(expectedRequired, StringComparer.Ordinal);
		var actualRequired = new HashSet<string>();

		foreach (var parameter in tool.Parameters)
		{
			// A parameter with no default value or a non-nullable type (no ?
			// suffix on string etc.) is required. We scan for CsParameterInfo-style
			// properties if available, or fall back to method reflection.
			if (!parameter.IsOptional)
				actualRequired.Add(parameter.Name);
		}

		var missing = required.Except(actualRequired).ToList();
		var unexpected = actualRequired.Except(required).Where(r => !IsShared(r)).ToList();

		Assert.True(missing.Count == 0, $"Tool {toolName} missing required params: {string.Join(", ", missing)}");
		Assert.True(unexpected.Count == 0, $"Tool {toolName} has unexpected required params: {string.Join(", ", unexpected)}");
	}

	private static bool IsShared(string name) => name is
		"campaign_id" or "title" or "tags" or "pinned" or "links" or
		"supersedes_id" or "actor" or "body" or "basis";

	/// <summary>
	/// Types that the MCP framework injects from the service provider rather than
	/// receiving from the JSON-schema input. These are not tool parameters.
	/// </summary>
	private static readonly HashSet<Type> DiInjectedTypes = new()
	{
		typeof(WarTableService),
		typeof(KnowledgeSearchService),
		typeof(CancellationToken),
	};

	private static List<(string Name, List<ParamInfo> Parameters)> GetMcpTools()
	{
		var tools = new List<(string, List<ParamInfo>)>();

		foreach (var method in typeof(BunkerTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
		{
			var attr = method.GetCustomAttribute<McpServerToolAttribute>();
			if (attr is null)
				continue;

			var name = attr.Name ?? method.Name;
			var parameters = new List<ParamInfo>();

			foreach (var param in method.GetParameters())
			{
				// Skip DI-injected parameters — they do not appear in the JSON schema.
				if (DiInjectedTypes.Contains(param.ParameterType))
					continue;

				var descAttr = param.GetCustomAttribute<DescriptionAttribute>();
				var isOptional = param.HasDefaultValue || param.IsOptional;

				// Nullable reference types (e.g. NoteLinksInput?) are generic
				// Nullable<T> at the reflection level.
				var type = param.ParameterType;
				var isNullableRef = type.IsGenericType &&
					type.GetGenericTypeDefinition() == typeof(Nullable<>);

				parameters.Add(new ParamInfo(
					param.Name ?? "",
					isOptional || isNullableRef,
					descAttr?.Description));
			}

			tools.Add((name, parameters));
		}

		return tools;
	}

	private sealed record ParamInfo(string Name, bool IsOptional, string? Description);
}
