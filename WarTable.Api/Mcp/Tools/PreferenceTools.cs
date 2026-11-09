using System.ComponentModel;
using ModelContextProtocol.Server;
using WarTable.Api.Services;

namespace WarTable.Api.Mcp.Tools;

/// <summary>Operator preference tools.</summary>
[McpServerToolType]
public static class PreferenceTools
{
    [McpServerTool(Name = "add_preference")]
    [Description("Capture an operator preference.")]
    public static async Task<object> AddPreferenceAsync(
        WarTableService service,
        string text,
        string? rationale = null,
        string? tags = null,
        string? source = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return McpResults.Error("text is required");

            return await service.CreatePreferenceAsync(
                text,
                rationale ?? "",
                tags ?? "",
                source ?? "human");
        }
        catch (Exception ex)
        {
            return McpResults.Error(ex.Message);
        }
    }

    [McpServerTool(Name = "list_preferences")]
    [Description("List operator preferences, active ones by default.")]
    public static async Task<object> ListPreferencesAsync(
        WarTableService service,
        bool? include_inactive = null)
    {
        try
        {
            return await service.GetPreferencesAsync(include_inactive ?? false);
        }
        catch (Exception ex)
        {
            return McpResults.Error(ex.Message);
        }
    }

    [McpServerTool(Name = "update_preference")]
    [Description("Update or retire an operator preference. Only the fields you include are changed.")]
    public static async Task<object> UpdatePreferenceAsync(
        WarTableService service,
        string preference_id,
        string? text = null,
        string? rationale = null,
        bool? active = null,
        string? tags = null)
    {
        try
        {
            var id = McpArgs.Guid(preference_id);
            if (id is null)
                return McpResults.Error("preference_id is required");

            var patch = new PreferencePatch(
                Text: text,
                Rationale: rationale,
                Active: active,
                Tags: tags);

            var updated = await service.UpdatePreferenceAsync(id.Value, patch);
            return updated is null
                ? McpResults.Error($"Preference not found: {id}")
                : updated;
        }
        catch (Exception ex)
        {
            return McpResults.Error(ex.Message);
        }
    }
}
