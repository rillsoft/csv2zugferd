using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Unterbefehl <c>csv2zugferd mcp</c>: MCP-Server über stdio. stdout trägt ausschließlich JSON-RPC,
/// deshalb gibt es keinen Konsolen-Sink auf stdout; Logs gehen in die Datei, Warnungen zusätzlich nach stderr.
/// </summary>
public static class McpServerCommand
{
    public const string ServerName = "csv2zugferd";
    public const string WebsiteUrl = "https://zugferd-tools.de/docs/mcp-server/";

    public static async Task<int> RunAsync(bool verbose, CancellationToken cancellationToken)
    {
        var logLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.File("logs/zugferd-.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console(
                restrictedToMinimumLevel: LogEventLevel.Warning,
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: "[csv2zugferd {Level:u3}] {Message:lj}{NewLine}{Exception}",
                theme: ConsoleTheme.None)
            .CreateLogger();

        try
        {
            Log.Information("MCP-Server (stdio) gestartet, Version {Version}", GetVersion());

            var options = CreateServerOptions();
            await using var transport = new StdioServerTransport(options, loggerFactory: null);
            await using var server = McpServer.Create(transport, options);
            await server.RunAsync(cancellationToken);

            Log.Information("MCP-Server beendet");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Log.Information("MCP-Server abgebrochen");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fehler im MCP-Server");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    public static McpServerOptions CreateServerOptions()
    {
        return new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = ServerName,
                Title = "csv2zugferd",
                Version = GetVersion(),
                Description = "Setup assistant for the csv2zugferd CSV to ZUGFeRD/Factur-X mapping: inspects the CSV header, checks a YAML configuration, runs a trial mapping and saves the configuration file.",
                WebsiteUrl = WebsiteUrl,
            },
            ServerInstructions =
                "Workflow: 1) csv2zugferd_csv_header_get reads the CSV columns. " +
                "2) csv2zugferd_config_schema_get returns the complete configuration reference (every YAML key, allowed values, effects). " +
                "3) Draft the YAML and check it with csv2zugferd_config_get (pass csvPath to compare against the columns). " +
                "4) csv2zugferd_invoice_get runs the real mapping without writing files; fix the YAML until totals and warnings are right. " +
                "5) csv2zugferd_config_file_create saves the YAML and returns the CLI command for production runs. " +
                "Production XML/PDF generation is done by the csv2zugferd CLI, not by this server.",
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability(),
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(ToolRegistry.ListTools()),
                CallToolHandler = ToolRegistry.CallToolAsync,
            },
        };
    }

    /// <summary>Produktversion ohne Build-Metadaten (z. B. "1.3.0").</summary>
    public static string GetVersion()
    {
        var informational = typeof(McpServerCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? typeof(McpServerCommand).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }
}
