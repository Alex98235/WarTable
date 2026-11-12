using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using WarTable.Api.Configuration;
using WarTable.Api.Data;
using WarTable.Api.Endpoints;
using WarTable.Api.Mcp;
using WarTable.Api.Options;
using WarTable.Api.Services;
using WarTable.Api.Services.Embeddings;

namespace WarTable.Api;

public class Program
{
	public static async Task<int> Main(string[] args)
	{
		var dotEnv = DotEnv.Load();
		if (dotEnv.Loaded)
			Console.Error.WriteLine(
				$"WarTable: loaded environment from {dotEnv.Path} ({dotEnv.Keys.Count} variable(s)).");
		foreach (var warning in dotEnv.Warnings)
			Console.Error.WriteLine($"WarTable: {warning}");

		var mode = "serve";
		var host = "localhost";
		var port = 5110;
		var reset = false;
		var dbPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".wartable",
			"wartable.db");

		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--mode":
				case "-m":
					if (i + 1 < args.Length) mode = args[++i].ToLowerInvariant();
					break;
				case "--host":
				case "-H":
					if (i + 1 < args.Length) host = args[++i];
					break;
				case "--port":
				case "-p":
					if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p;
					break;
				case "--db":
					if (i + 1 < args.Length) dbPath = args[++i];
					break;
				case "--reset-db":
					reset = true;
					break;
				case "--help":
				case "-h":
					PrintHelp();
					return 0;
			}
		}

		Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

		var connectionString = $"Data Source={dbPath}";

		if (mode == "mcp")
		{
			await RunMcpAsync(connectionString, reset);
			return 0;
		}

		if (mode == "serve")
		{
			await RunServeAsync(connectionString, host, port, reset);
			return 0;
		}

		Console.Error.WriteLine($"Unknown mode '{mode}'. Use --help for usage.");
		return 1;
	}

	private static async Task RunServeAsync(string connectionString, string host, int port, bool reset)
	{
		var builder = WebApplication.CreateBuilder();

		builder.Services.AddDbContext<WarTableDbContext>(options =>
			options.UseSqlite(connectionString));

		builder.Services.AddScoped<WarTableService>();

		var databasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
		AddKnowledgeServices(builder.Services, builder.Configuration, databasePath);

		builder.Services
			.AddMcpServer(options =>
			{
				options.ServerInfo = new Implementation
				{
					Name = "wartable-mcp",
					Version = "1.0.0",
				};
				options.ServerInstructions = WarTableMcpInstructions.Text;
			})
			.WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
			.WithToolsFromAssembly(serializerOptions: McpJson.Options);

		// The broadcaster watches the SQLite file for writes. Resolve the concrete
		// path here (the same one EF opens) so the watcher can be constructed before
		// the host starts. A null/":memory:" path simply disables the watcher.
		builder.Services.AddSingleton<ActivityBroadcaster>(services => new ActivityBroadcaster(
			services.GetRequiredService<IServiceScopeFactory>(),
			databasePath,
			services.GetRequiredService<ILogger<ActivityBroadcaster>>()));
		builder.Services.AddSingleton<IHostedService>(
			services => services.GetRequiredService<ActivityBroadcaster>());

		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
			options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
		});

		var app = builder.Build();

		// Initialize database (applies pending migrations, backing up first)
		using (var scope = app.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<WarTableDbContext>();
			await DatabaseInitializer.InitializeAsync(db, reset);
		}

		// Serve the built Svelte bundle. A physical wwwroot wins when present
		// (dotnet run / local build); otherwise fall back to the copy embedded
		// in the assembly, which is what makes a single-file publish work.
		var webFiles = ResolveWebFileProvider(builder);
		if (webFiles is null)
		{
			Console.Error.WriteLine(
				"No wwwroot found. Build the frontend first: npm run build:frontend");
		}
		else
		{
			app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
			app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });
		}

		app.MapCampaignEndpoints();
		app.MapObjectiveEndpoints();
		app.MapStepEndpoints();
		app.MapNoteEndpoints();
		app.MapKnowledgeEndpoints();
		app.MapPreferenceEndpoints();
		app.MapBriefingEndpoints();
		app.MapAliasEndpoints();
		app.MapEventEndpoints();
		app.MapMcp("/mcp");

		// Health check
		app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "WarTable" }));

		app.Urls.Add($"http://{host}:{port}");

		var displayHost = host is "0.0.0.0" or "+" or "*" ? "localhost" : host;
		Console.WriteLine($"WarTable serving on http://{displayHost}:{port}");
		if (host is "0.0.0.0" or "+" or "*")
			Console.WriteLine($"Listening on all interfaces (port {port}).");
		Console.WriteLine($"Press Ctrl+C to stop.");

		await app.RunAsync();
	}

	/// <summary>
	/// Locates the built frontend. A physical wwwroot is preferred so local
	/// builds pick up frontend changes; a published single-file binary falls
	/// back to the bundle embedded in the assembly.
	/// </summary>
	private static IFileProvider? ResolveWebFileProvider(WebApplicationBuilder builder)
	{
		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, "wwwroot"),
			builder.Environment.WebRootPath,
		};

		foreach (var candidate in candidates)
		{
			if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
				return new PhysicalFileProvider(candidate);
		}

		try
		{
			return new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot");
		}
		catch (InvalidOperationException)
		{
			// Built without an embedded manifest (no wwwroot at compile time).
			return null;
		}
	}

	private static async Task RunMcpAsync(string connectionString, bool reset)
	{
		var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

		// stdio carries JSON-RPC on stdout; every log line must go to stderr.
		builder.Logging.ClearProviders();
		builder.Logging.AddConsole(options =>
			options.LogToStandardErrorThreshold = LogLevel.Trace);

		builder.Services.AddDbContext<WarTableDbContext>(options =>
			options.UseSqlite(connectionString));
		builder.Services.AddScoped<WarTableService>();

		var databasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
		AddKnowledgeServices(builder.Services, builder.Configuration, databasePath);

		builder.Services
			.AddMcpServer(options =>
			{
				options.ServerInfo = new Implementation
				{
					Name = "wartable-mcp",
					Version = "1.0.0",
				};
				options.ServerInstructions = WarTableMcpInstructions.Text;
			})
			.WithStdioServerTransport()
			.WithToolsFromAssembly(serializerOptions: McpJson.Options);

		var host = builder.Build();

		using (var scope = host.Services.CreateScope())
		{
			var db = scope.ServiceProvider.GetRequiredService<WarTableDbContext>();
			await DatabaseInitializer.InitializeAsync(db, reset);
		}

		await host.RunAsync();
	}

	private static void AddKnowledgeServices(
		IServiceCollection services,
		IConfiguration configuration,
		string databasePath)
	{
		services.Configure<EmbeddingsOptions>(configuration.GetSection(EmbeddingsOptions.SectionName));
		services.PostConfigure<EmbeddingsOptions>(options =>
		{
			if (!string.IsNullOrWhiteSpace(options.SidecarPath))
				return;

			var directory = Path.GetDirectoryName(databasePath);
			options.SidecarPath = Path.Combine(
				string.IsNullOrWhiteSpace(directory) ? "." : directory,
				"wartable.embeddings.db");
		});

		services.AddSingleton<EmbeddingProfileProvider>();
		services.AddHttpClient<IEmbeddingClient, OpenAiEmbeddingClient>((provider, client) =>
		{
			var options = provider.GetRequiredService<IOptions<EmbeddingsOptions>>().Value;
			client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
		});
		services.AddSingleton<IKnowledgeIndex>(provider =>
		{
			var options = provider.GetRequiredService<IOptions<EmbeddingsOptions>>().Value;
			return new SqliteKnowledgeIndex(options.SidecarPath!);
		});
		services.AddScoped<KnowledgeIndexService>();
		services.AddScoped<KnowledgeSearchService>();
		services.AddScoped<KnowledgeContextService>();
		services.AddHostedService<KnowledgeIndexWorker>();
	}

	private static void PrintHelp()
	{
		Console.WriteLine("""
            WarTable — Human-AI planning workspace

            USAGE:
              WarTable.Api [--mode <serve|mcp>] [--port <port>] [--db <path>] [--reset-db]

            OPTIONS:
              --mode, -m  serve | mcp
                          serve: run the web UI + HTTP API (default)
                          mcp:   run as an MCP stdio server
              --host, -H  interface to bind in serve mode (default: localhost)
                          use 0.0.0.0 to accept connections from other machines
              --port, -p  HTTP port for serve mode (default: 5110)
              --db        SQLite database path
                          (default: ~/.wartable/wartable.db)
              --reset-db  back up and delete the database, then create it fresh
              --help, -h  show this help

            EXAMPLES:
              WarTable.Api                          # web UI on :5110, local only
              WarTable.Api --host 0.0.0.0           # reachable from the LAN
              WarTable.Api --mode mcp               # MCP stdio server
            """);
	}
}
