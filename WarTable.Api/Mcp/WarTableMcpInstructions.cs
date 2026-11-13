namespace WarTable.Api.Mcp;

/// <summary>
/// Baseline MCP usage guidance sent during the initialize handshake. Clients may
/// treat this as system context.
/// </summary>
/// <remarks>
/// The text lives in <c>instructions.txt</c> beside this file, which is the only
/// canonical copy: it is embedded at build time so a published single-file binary
/// carries it, and the site's sync script reads the same file so the fallback copy
/// cannot drift. Edit the text file, never this class.
/// </remarks>
public static class WarTableMcpInstructions
{
	private const string ResourceName = "WarTable.Api.Mcp.instructions.txt";

	public static readonly string Text = Load();

	private static string Load()
	{
		using var stream = typeof(WarTableMcpInstructions).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd();
	}
}
