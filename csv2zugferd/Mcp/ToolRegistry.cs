using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;

namespace Csv2Zugferd.Mcp;

/// <summary>Ein Registryeintrag: Vertrag (Name, Titel, Beschreibung, Schemata, Annotationen) und Handler an einer Stelle.</summary>
public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required JsonElement InputSchema { get; init; }
    public required JsonElement OutputSchema { get; init; }

    /// <summary>Zugriffsklasse: lesend oder schreibend. Daraus entstehen <c>readOnlyHint</c> und die Byte-Grenze.</summary>
    public required bool IsReadOnly { get; init; }
    public required bool IsDestructive { get; init; }
    public required bool IsIdempotent { get; init; }
    public required bool IsOpenWorld { get; init; }

    /// <summary>Eingrenzungsargumente des Werkzeugs für die Meldung <c>response_too_large</c>.</summary>
    public IReadOnlyList<string> LimitHints { get; init; } = [];

    public required Func<JsonElement, CancellationToken, Task<object>> Handler { get; init; }

    private JsonSchemaValidator? _inputValidator;
    private JsonSchemaValidator? _outputValidator;

    public JsonSchemaValidator InputValidator => _inputValidator ??= new JsonSchemaValidator(InputSchema);
    public JsonSchemaValidator OutputValidator => _outputValidator ??= new JsonSchemaValidator(OutputSchema);

    public Tool ToProtocolTool() => new()
    {
        Name = Name,
        Title = Title,
        Description = Description,
        InputSchema = InputSchema,
        OutputSchema = OutputSchema,
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = IsReadOnly,
            DestructiveHint = IsDestructive,
            IdempotentHint = IsIdempotent,
            OpenWorldHint = IsOpenWorld,
        },
    };
}

/// <summary>
/// Die eine Registry der Werkzeuge. <c>tools/list</c>, Argumentprüfung, Dispatch, Ausgabevalidierung und
/// Byte-Grenze lesen alle dieselben Einträge. Protokollfehler (unbekanntes Werkzeug, Schemaverletzung) sind
/// JSON-RPC-Fehler; fachliche Ablehnungen sind Tool-Ergebnisse mit <c>isError</c> und Code.
/// </summary>
public static class ToolRegistry
{
    /// <summary>Serialisierung der Ergebnis-DTOs: <c>lowerCamelCase</c>, Nullwerte werden geschrieben (nullable Vertragsfelder).</summary>
    public static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    private static readonly Lazy<ToolDefinition[]> s_tools = new(() =>
        InvoiceTools.CreateDefinitions()
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray());

    public static IReadOnlyList<ToolDefinition> Tools => s_tools.Value;

    public static ToolDefinition? Find(string name) =>
        s_tools.Value.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    public static ListToolsResult ListTools() => new()
    {
        Tools = s_tools.Value.Select(t => t.ToProtocolTool()).ToList(),
    };

    public static ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        var name = context.Params?.Name ?? string.Empty;
        var arguments = ToElement(context.Params?.Arguments);
        return new ValueTask<CallToolResult>(CallToolAsync(name, arguments, context.JsonRpcRequest.Id, cancellationToken));
    }

    /// <summary>Gemeinsamer Wrapper aller Werkzeuge; ohne Prozess für Handler-Tests aufrufbar.</summary>
    public static async Task<CallToolResult> CallToolAsync(string name, JsonElement arguments, RequestId requestId, CancellationToken cancellationToken)
    {
        var tool = Find(name)
            ?? throw new McpProtocolException($"Unbekanntes Werkzeug: '{name}'", McpErrorCode.InvalidParams);

        var inputViolations = tool.InputValidator.Validate(arguments);
        if (inputViolations.Count > 0)
        {
            throw new McpProtocolException(
                $"Ungültige Argumente für {name}: {string.Join("; ", inputViolations.Select(v => v.ToString()))}",
                McpErrorCode.InvalidParams);
        }

        object result;
        try
        {
            result = await tool.Handler(arguments, cancellationToken);
        }
        catch (Csv2ZugferdException ex)
        {
            Log.Warning("Werkzeug {Tool} abgelehnt: {Code} {Message}", name, ex.Code, ex.Message);
            return ToolErrors.FromException(ex);
        }
        catch (McpProtocolException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unerwarteter Fehler im Werkzeug {Tool}", name);
            return ToolErrors.Create(ErrorCodes.InternalError, $"Unerwarteter Fehler in {name}: {ex.Message}");
        }

        var structured = JsonSerializer.SerializeToNode(result, result.GetType(), ResultJsonOptions) as JsonObject
            ?? throw new McpProtocolException($"{ErrorCodes.OutputSchemaInvalid}\nDas Ergebnis von {name} ist kein JSON-Objekt", McpErrorCode.InternalError);

        var structuredElement = SchemaBuilder.ToElement(structured);
        Mcp.OutputValidator.EnsureValid(name, tool.OutputValidator, structuredElement);

        var callResult = new CallToolResult
        {
            Content = [new TextContentBlock { Text = structured.ToJsonString(ResultJsonOptions) }],
            StructuredContent = structuredElement,
        };

        if (tool.IsReadOnly)
        {
            var bytes = ResponseLimit.MeasureResponseBytes(requestId, callResult);
            if (bytes > ResponseLimit.LimitBytes)
            {
                Log.Warning("Antwort von {Tool} mit {Bytes} Byte über der Grenze von {Limit} Byte", name, bytes, ResponseLimit.LimitBytes);
                return ResponseLimit.CreateTooLargeResult(name, bytes, tool.LimitHints);
            }
        }

        return callResult;
    }

    private static JsonElement ToElement(IDictionary<string, JsonElement>? arguments)
    {
        var node = new JsonObject();
        if (arguments != null)
        {
            foreach (var (key, value) in arguments)
                node[key] = JsonSerializer.SerializeToNode(value);
        }
        return SchemaBuilder.ToElement(node);
    }
}
