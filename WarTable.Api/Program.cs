using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using WarTable.Api.Data;
using WarTable.Api.Endpoints;
using WarTable.Api.Mcp;
using WarTable.Api.Services;

namespace WarTable.Api;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var mode = "serve";
        var host = "localhost";
        var port = 5110;
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
            await RunMcpAsync(connectionString);
            return 0;
        }

        if (mode == "serve")
        {
            await RunServeAsync(connectionString, host, port);
            return 0;
        }

        Console.Error.WriteLine($"Unknown mode '{mode}'. Use --help for usage.");
        return 1;
    }

    private static async Task RunServeAsync(string connectionString, string host, int port)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddDbContext<WarTableDbContext>(options =>
            options.UseSqlite(connectionString));

        builder.Services.AddScoped<WarTableService>();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        var app = builder.Build();

        // Initialize database
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarTableDbContext>();
            db.Database.EnsureCreated();
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
        app.MapIntelEndpoints();
        app.MapSitRepEndpoints();
        app.MapAliasEndpoints();

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

    private static async Task RunMcpAsync(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<WarTableDbContext>();
        builder.UseSqlite(connectionString);

        await using var db = new WarTableDbContext(builder.Options);
        db.Database.EnsureCreated();

        var service = new WarTableService(db);
        var server = new McpServer(service);
        await server.RunAsync();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            WarTable — Human-AI planning workspace

            USAGE:
              WarTable.Api [--mode <serve|mcp>] [--port <port>] [--db <path>]

            OPTIONS:
              --mode, -m  serve | mcp
                          serve: run the web UI + HTTP API (default)
                          mcp:   run as an MCP stdio server
              --host, -H  interface to bind in serve mode (default: localhost)
                          use 0.0.0.0 to accept connections from other machines
              --port, -p  HTTP port for serve mode (default: 5110)
              --db        SQLite database path
                          (default: ~/.wartable/wartable.db)
              --help, -h  show this help

            EXAMPLES:
              WarTable.Api                          # web UI on :5110, local only
              WarTable.Api --host 0.0.0.0           # reachable from the LAN
              WarTable.Api --mode mcp               # MCP stdio server
            """);
    }
}
