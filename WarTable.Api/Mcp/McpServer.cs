using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarTable.Api.Models;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp;

/// <summary>
/// MCP (Model Context Protocol) stdio server running over JSON-RPC 2.0.
/// Reads requests from stdin and writes responses to stdout.
/// Run via <see cref="Start"/> (background) or <see cref="RunAsync"/> (foreground).
/// </summary>
public class McpServer : IDisposable
{
    private readonly WarTableService _service;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly StreamReader _input;
    private readonly StreamWriter _output;
    private readonly CancellationTokenSource _cts = new();
    private Task? _runTask;
    private bool _disposed;

    /// <summary>
    /// Serializer for the JSON-RPC envelope and the tool definitions. MCP mandates
    /// camelCase protocol keys (<c>protocolVersion</c>, <c>serverInfo</c>,
    /// <c>inputSchema</c>, <c>isError</c>), so this must NOT use the snake_case
    /// policy that applies to tool payload data.
    /// </summary>
    private static readonly JsonSerializerOptions ProtocolJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() },
    };

    public McpServer(WarTableService service)
    {
        _service = service;

        // MCP stdio is UTF-8. Console.In/Out default to the active code page on
        // Windows, which mangles non-ASCII text (— becomes ΓÇö) when the server
        // is spawned with a pipe. Bind the standard streams explicitly instead.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _input = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false);
        _output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    /// <summary>Starts the JSON-RPC dispatch loop as a fire-and-forget background task.</summary>
    public void Start()
    {
        _runTask = Task.Run(RunAsync);
    }

    /// <summary>
    /// Runs the JSON-RPC read/dispatch/write loop synchronously on the calling thread.
    /// Reads lines from stdin, parses JSON-RPC 2.0 requests, dispatches them, and writes responses to stdout.
    /// Exits on end-of-input (stdin closes) or cancellation.
    /// </summary>
    public async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await _input.ReadLineAsync(_cts.Token);
                if (line is null)
                    break; // EOF
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    await HandleLineAsync(line);
                }
                catch (JsonException)
                {
                    // Malformed JSON on the transport — skip, keep the server alive.
                }
                catch (Exception ex)
                {
                    // Unknown error during dispatch — keep the server alive.
                    await WriteErrorAsync(null, -32603, $"Internal error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    // ---------------------------------------------------------------
    //  JSON-RPC / MCP protocol handlers
    // ---------------------------------------------------------------

    private async Task HandleLineAsync(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (!root.TryGetProperty("method", out var methodElement))
            return;
        var method = methodElement.GetString();
        if (method is null)
            return;

        root.TryGetProperty("id", out var idElement);
        var hasId = idElement.ValueKind is JsonValueKind.Number or JsonValueKind.String;
        object? id = hasId ? idElement : null;

        // -- Notifications -------------------------------------------------
        // JSON-RPC notifications carry no "id" and must not be answered.
        if (method is "notifications/initialized" or "initialized")
        {
            // If a client sends an id anyway, acknowledge with empty result.
            if (hasId)
                await WriteResponseAsync(id, new { });
            return;
        }
        if (!hasId)
            return; // other notification — silently ignore

        // -- Standard MCP methods ------------------------------------------
        switch (method)
        {
            case "initialize":
                await WriteResponseAsync(id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "wartable-mcp", version = "1.0.0" },
                });
                break;

            case "tools/list":
            case "mcp_list_tools":
                await WriteResponseAsync(id, new { tools = ToolDefinitions });
                break;

            case "tools/call":
            case "mcp_call_tool":
                await HandleToolsCallAsync(id, root);
                break;

            case "resources/list":
            case "mcp_list_resources":
                await WriteResponseAsync(id, new { resources = Array.Empty<object>() });
                break;

            case "ping":
                await WriteResponseAsync(id, new { });
                break;

            default:
                await WriteErrorAsync(id, -32601, $"Method not found: {method}");
                break;
        }
    }

    /// <summary>
    /// Dispatches a "tools/call" request to the appropriate tool handler.
    /// </summary>
    private async Task HandleToolsCallAsync(object? id, JsonElement root)
    {
        string? toolName = null;
        JsonElement args = default;

        if (root.TryGetProperty("params", out var p) && p.ValueKind is JsonValueKind.Object)
        {
            // Standard MCP: params = { name: "...", arguments: { ... } }
            if (p.TryGetProperty("name", out var nameEl) && nameEl.ValueKind is JsonValueKind.String)
                toolName = nameEl.GetString();

            // Also accept the non-standard "tool" field (backward compat).
            if (string.IsNullOrWhiteSpace(toolName)
                && p.TryGetProperty("tool", out var toolEl) && toolEl.ValueKind is JsonValueKind.String)
                toolName = toolEl.GetString();

            if (p.TryGetProperty("arguments", out var argsEl))
                args = argsEl;
            else if (p.TryGetProperty("args", out var aEl))
                args = aEl;
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            await WriteErrorAsync(id, -32602, "Tool name is required");
            return;
        }

        try
        {
            var result = await CallToolAsync(toolName, args);
            await WriteResponseAsync(id, result);
        }
        catch (Exception ex)
        {
            await WriteErrorAsync(id, -32603, ex.Message);
        }
    }

    /// <summary>Invokes the named tool with the given arguments.</summary>
    private async Task<object> CallToolAsync(string toolName, JsonElement args)
    {
        return toolName switch
        {
            "create_campaign" => await CallCreateCampaignAsync(args),
            "get_campaign" => await CallGetCampaignAsync(args),
            "list_campaigns" => await CallListCampaignsAsync(args),
            "update_campaign_status" => await CallUpdateCampaignStatusAsync(args),
            "create_objective" => await CallCreateObjectiveAsync(args),
            "list_objectives" => await CallListObjectivesAsync(args),
            "update_objective_lane" => await CallUpdateObjectiveLaneAsync(args),
            "log_intel" => await CallLogIntelAsync(args),
            "search_intel" => await CallSearchIntelAsync(args),
            "create_sitrep" => await CallCreateSitRepAsync(args),
            "get_object" => await CallGetObjectAsync(args),
            _ => ToolError($"Unknown tool: {toolName}"),
        };
    }

    // ---------------------------------------------------------------
    //  Tool implementations
    // ---------------------------------------------------------------

    private async Task<object> CallCreateCampaignAsync(JsonElement args)
    {
        var title = GetString(args, "title");
        if (string.IsNullOrWhiteSpace(title))
            return ToolError("title is required");

        var campaign = new Campaign
        {
            Title = title,
            Description = GetString(args, "description") ?? "",
            Tags = GetString(args, "tags") ?? "",
        };
        var created = await _service.CreateCampaignAsync(campaign);
        return ToolResult(created);
    }

    private async Task<object> CallGetCampaignAsync(JsonElement args)
    {
        var id = GetGuid(args, "campaign_id");
        if (id is null)
            return ToolError("campaign_id is required");

        var campaign = await _service.GetCampaignAsync(id.Value);
        return campaign is null ? ToolError($"Campaign not found: {id}") : ToolResult(campaign);
    }

    private async Task<object> CallListCampaignsAsync(JsonElement args)
    {
        var search = GetString(args, "search");
        var statusStr = GetString(args, "status");
        CampaignStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusStr) && Enum.TryParse<CampaignStatus>(statusStr, true, out var s))
            status = s;

        var campaigns = await _service.GetCampaignsAsync(search, status);
        return ToolResult(campaigns);
    }

    private async Task<object> CallUpdateCampaignStatusAsync(JsonElement args)
    {
        var id = GetGuid(args, "campaign_id");
        var statusStr = GetString(args, "status");
        if (id is null)
            return ToolError("campaign_id is required");
        if (string.IsNullOrWhiteSpace(statusStr))
            return ToolError("status is required");
        if (!Enum.TryParse<CampaignStatus>(statusStr, true, out var status))
            return ToolError($"Invalid campaign status: {statusStr}");

        var campaign = await _service.GetCampaignAsync(id.Value);
        if (campaign is null)
            return ToolError($"Campaign not found: {id}");

        var updated = await _service.UpdateCampaignAsync(id.Value, new CampaignPatch(Status: status));
        return ToolResult(updated!);
    }

    private async Task<object> CallCreateObjectiveAsync(JsonElement args)
    {
        var campaignId = GetGuid(args, "campaign_id");
        var title = GetString(args, "title");
        if (campaignId is null)
            return ToolError("campaign_id is required");
        if (string.IsNullOrWhiteSpace(title))
            return ToolError("title is required");

        var objective = new Objective
        {
            CampaignId = campaignId.Value,
            Title = title,
            Description = GetString(args, "description") ?? "",
            Lane = GetString(args, "lane") ?? "backlog",
        };
        try
        {
            var created = await _service.CreateObjectiveAsync(objective);
            return ToolResult(created);
        }
        catch (InvalidOperationException ex)
        {
            return ToolError(ex.Message);
        }
    }

    private async Task<object> CallListObjectivesAsync(JsonElement args)
    {
        var campaignId = GetGuid(args, "campaign_id");
        var lane = GetString(args, "lane");

        var objectives = await _service.GetObjectivesAsync(campaignId, lane);
        return ToolResult(objectives);
    }

    private async Task<object> CallUpdateObjectiveLaneAsync(JsonElement args)
    {
        var id = GetGuid(args, "id");
        var lane = GetString(args, "lane");
        if (id is null)
            return ToolError("id is required");
        if (string.IsNullOrWhiteSpace(lane))
            return ToolError("lane is required");

        var objective = await _service.GetObjectiveAsync(id.Value);
        if (objective is null)
            return ToolError($"Objective not found: {id}");

        try
        {
            var updated = await _service.UpdateObjectiveAsync(id.Value, new ObjectivePatch(Lane: lane));
            return ToolResult(updated!);
        }
        catch (InvalidOperationException ex)
        {
            return ToolError(ex.Message);
        }
    }

    private async Task<object> CallLogIntelAsync(JsonElement args)
    {
        var title = GetString(args, "title");
        var body = GetString(args, "body");
        if (string.IsNullOrWhiteSpace(title))
            return ToolError("title is required");
        if (string.IsNullOrWhiteSpace(body))
            return ToolError("body is required");

        var categoryStr = GetString(args, "category");
        var category = IntelCategory.Context;
        if (!string.IsNullOrWhiteSpace(categoryStr) && !Enum.TryParse<IntelCategory>(categoryStr, true, out category))
            return ToolError($"Invalid intel category: {categoryStr}");

        var campaignId = GetGuid(args, "campaign_id");
        if (campaignId is null || campaignId == Guid.Empty)
            return ToolError("campaign_id is required; intel always belongs to a campaign");

        var intel = new Intel
        {
            Title = title,
            Body = body,
            Category = category,
            CampaignId = campaignId.Value,
            Tags = GetString(args, "tags") ?? "",
        };
        var created = await _service.CreateIntelAsync(intel);
        return ToolResult(created);
    }

    private async Task<object> CallSearchIntelAsync(JsonElement args)
    {
        var search = GetString(args, "search");
        var categoryStr = GetString(args, "category");
        IntelCategory? category = null;
        if (!string.IsNullOrWhiteSpace(categoryStr) && Enum.TryParse<IntelCategory>(categoryStr, true, out var c))
            category = c;
        var campaignId = GetGuid(args, "campaign_id");

        var intel = await _service.GetIntelAsync(search, category, campaignId);
        return ToolResult(intel);
    }

    private async Task<object> CallCreateSitRepAsync(JsonElement args)
    {
        var campaignId = GetGuid(args, "campaign_id");
        var summary = GetString(args, "summary");
        if (campaignId is null)
            return ToolError("campaign_id is required");
        if (string.IsNullOrWhiteSpace(summary))
            return ToolError("summary is required");

        var completed = GetIntArray(args, "completed") ?? Array.Empty<int>();
        var blocked = GetIntArray(args, "blocked") ?? Array.Empty<int>();
        var next = GetIntArray(args, "next") ?? Array.Empty<int>();
        var source = GetString(args, "source");

        try
        {
            var created = await _service.CreateSitRepAsync(
                campaignId.Value,
                summary!,
                source,
                completed,
                blocked,
                next);

            return created is null
                ? ToolError($"Campaign not found: {campaignId}")
                : ToolResult(created);
        }
        catch (UnknownAliasException ex)
        {
            var listed = string.Join(", ", ex.Aliases.Select(a => $"#{a}"));
            return ToolError(
                $"Unknown objective aliases: {listed}. " +
                "Aliases are the per-campaign `alias` numbers returned on objectives — " +
                "call list_objectives to see valid ones.");
        }
    }

    private async Task<object> CallGetObjectAsync(JsonElement args)
    {
        var campaignId = GetGuid(args, "campaign_id");
        var alias = GetInt(args, "alias");
        if (campaignId is null)
            return ToolError("campaign_id is required");
        if (alias is null)
            return ToolError("alias is required (integer, e.g. 42 for #42)");

        var resolved = await _service.ResolveAliasAsync(campaignId.Value, alias.Value);
        return resolved is null
            ? ToolError($"No object #{alias} in campaign {campaignId}")
            : ToolResult(resolved);
    }

    // ---------------------------------------------------------------
    //  Tool definitions (returned by tools/list)
    // ---------------------------------------------------------------

    private static readonly object[] ToolDefinitions =
    [
        new
        {
            name = "create_campaign",
            description = "Create a new campaign in the planning workspace.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string", description = "Campaign title (required)" },
                    description = new { type = "string", description = "Campaign description" },
                    tags = new { type = "string", description = "Comma-separated tags" },
                },
                required = new[] { "title" },
            },
        },
        new
        {
            name = "get_campaign",
            description =
                "Get a single campaign by id, including objectives, intel entries and sitreps. " +
                "Every objective, intel entry and sitrep carries an `alias` integer — its " +
                "campaign-scoped #N identifier, used for cross-referencing.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    campaign_id = new { type = "string", description = "Campaign id (GUID)" },
                },
                required = new[] { "campaign_id" },
            },
        },
        new
        {
            name = "list_campaigns",
            description = "List campaigns, optionally filtered by search text and status.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Free-text search over title, description and tags" },
                    status = new { type = "string", description = "Campaign status: Planning, Active, Completed, Abandoned" },
                },
                required = Array.Empty<string>(),
            },
        },
        new
        {
            name = "update_campaign_status",
            description = "Update the status of an existing campaign.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    campaign_id = new { type = "string", description = "Campaign id (GUID)" },
                    status = new { type = "string", description = "New status: Planning, Active, Completed, Abandoned" },
                },
                required = new[] { "campaign_id", "status" },
            },
        },
        new
        {
            name = "create_objective",
            description = "Create a new objective under a campaign.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    campaign_id = new { type = "string", description = "Campaign id (GUID)" },
                    title = new { type = "string", description = "Objective title (required)" },
                    description = new { type = "string", description = "Objective description" },
                    lane = new { type = "string", description = "Lane: backlog, ready, in-progress, review, done. Defaults to backlog." },
                },
                required = new[] { "campaign_id", "title" },
            },
        },
        new
        {
            name = "list_objectives",
            description =
                "List objectives, optionally filtered by campaign and lane. " +
                "Each result carries `alias` — the campaign-scoped #N used when referencing " +
                "objectives from sitreps and intel. Pass these numbers, not titles.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    campaign_id = new { type = "string", description = "Campaign id (GUID) to filter by" },
                    lane = new { type = "string", description = "Lane filter: backlog, ready, in-progress, review, done" },
                },
                required = Array.Empty<string>(),
            },
        },
        new
        {
            name = "update_objective_lane",
            description = "Move an objective to a different lane. Lane is the objective's only state field.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", description = "Objective id (GUID)" },
                    lane = new { type = "string", description = "New lane: backlog, ready, in-progress, review, done" },
                },
                required = new[] { "id", "lane" },
            },
        },
        new
        {
            name = "log_intel",
            description = "Log a piece of intelligence (intel entry) on a campaign. Every intel entry belongs to a campaign.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    title = new { type = "string", description = "Intel title (required)" },
                    body = new { type = "string", description = "Intel body text (required)" },
                    category = new { type = "string", description = "Category: Decision, Context, Gotcha, Reference" },
                    campaign_id = new { type = "string", description = "Campaign id (GUID). Required — intel is always campaign-scoped." },
                    tags = new { type = "string", description = "Comma-separated tags" },
                },
                required = new[] { "title", "body", "campaign_id" },
            },
        },
        new
        {
            name = "search_intel",
            description = "Search intel entries by text, category and/or campaign.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    search = new { type = "string", description = "Free-text search over title, body and tags" },
                    category = new { type = "string", description = "Filter by category: Decision, Context, Gotcha, Reference" },
                    campaign_id = new { type = "string", description = "Filter by campaign id (GUID)" },
                },
                required = Array.Empty<string>(),
            },
        },
        new
        {
            name = "create_sitrep",
            description =
                "Append a situation report to a campaign's journal. Reference objectives by their " +
                "alias number (`alias` field, shown as #N), NOT by title or by a count. " +
                "The objectives table remains the source of truth for current state; a sitrep is a " +
                "timestamped observation of what this session did.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    campaign_id = new { type = "string", description = "Campaign id (GUID)" },
                    summary = new { type = "string", description = "Narrative summary of the session (required)" },
                    source = new { type = "string", description = "Who is reporting: 'human' or 'agent:<name>'. Defaults to 'human'." },
                    completed = new
                    {
                        type = "array",
                        items = new { type = "integer" },
                        description = "Objective alias numbers (#N) completed or advanced this session",
                    },
                    blocked = new
                    {
                        type = "array",
                        items = new { type = "integer" },
                        description = "Objective alias numbers (#N) that are blocked",
                    },
                    next = new
                    {
                        type = "array",
                        items = new { type = "integer" },
                        description = "Objective alias numbers (#N) intended for the next session",
                    },
                },
                required = new[] { "campaign_id", "summary" },
            },
        },
        new
        {
            name = "get_object",
            description =
                "Resolve a campaign-scoped alias (#N) to the object it identifies. " +
                "Aliases are shared across objectives, intel and sitreps within a campaign, " +
                "so a bare number is unambiguous inside one campaign. " +
                "Soft-deleted objects still resolve, with \"deleted\": true.",
            inputSchema = new
            {
                type = "object",
                properties = new
                {
                    campaign_id = new { type = "string", description = "Campaign id (GUID)" },
                    alias = new { type = "integer", description = "Alias number, e.g. 42 for #42" },
                },
                required = new[] { "campaign_id", "alias" },
            },
        },
    ];

    // ---------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------

    private async Task WriteResponseAsync(object? id, object result)
    {
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };
        var json = JsonSerializer.Serialize(payload, ProtocolJsonOptions);
        await _output.WriteLineAsync(json);
        await _output.FlushAsync();
    }

    private async Task WriteErrorAsync(object? id, int code, string message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        var json = JsonSerializer.Serialize(payload, ProtocolJsonOptions);
        await _output.WriteLineAsync(json);
        await _output.FlushAsync();
    }

    private object ToolResult(object data)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(data, _jsonOptions) },
            },
        };
    }

    private object ToolError(string message)
    {
        return new
        {
            content = new[]
            {
                new { type = "text", text = message },
            },
            isError = true,
        };
    }

    // ---------------------------------------------------------------
    //  Argument extraction helpers
    // ---------------------------------------------------------------

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Object &&
            element.TryGetProperty(name, out var prop) &&
            prop.ValueKind is JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Object &&
            element.TryGetProperty(name, out var prop) &&
            prop.ValueKind is JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return null;
    }

    private static Guid? GetGuid(JsonElement element, string name)
    {
        var s = GetString(element, name);
        return s is not null && Guid.TryParse(s, out var g) ? g : null;
    }

    private static string[]? GetStringArray(JsonElement element, string name)
    {
        if (element.ValueKind is JsonValueKind.Object &&
            element.TryGetProperty(name, out var prop) &&
            prop.ValueKind is JsonValueKind.Array)
        {
            return prop.EnumerateArray()
                .Where(e => e.ValueKind is JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(s => s is not null)
                .Cast<string>()
                .ToArray();
        }
        return null;
    }

    private static int[]? GetIntArray(JsonElement element, string name)
    {
        if (element.ValueKind is not JsonValueKind.Object ||
            !element.TryGetProperty(name, out var prop) ||
            prop.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<int>();
        foreach (var item in prop.EnumerateArray())
        {
            // Be lenient: agents sometimes send "#42" or "42" instead of 42.
            if (item.ValueKind is JsonValueKind.Number)
            {
                values.Add(item.GetInt32());
            }
            else if (item.ValueKind is JsonValueKind.String)
            {
                var raw = item.GetString()?.Trim().TrimStart('#');
                if (int.TryParse(raw, out var parsed))
                    values.Add(parsed);
            }
        }
        return values.ToArray();
    }

    // ---------------------------------------------------------------
    //  IDisposable
    // ---------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
