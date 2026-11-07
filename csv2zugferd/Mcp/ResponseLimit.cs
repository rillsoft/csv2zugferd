using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Byte-Grenze für lesende Werkzeuge. Gemessen wird die vollständige JSON-RPC-Antwort (Hülle, <c>content</c>,
/// <c>structuredContent</c>) in UTF-8 an genau dieser Stelle. Über der Grenze tritt
/// <see cref="ErrorCodes.ResponseTooLarge"/> an die Stelle des Ergebnisses; es gibt keine stille Kürzung.
/// </summary>
public static class ResponseLimit
{
    public const int DefaultLimitBytes = 900_000;

    /// <summary>Grenze in Byte; für Tests veränderbar.</summary>
    public static int LimitBytes { get; set; } = DefaultLimitBytes;

    public static int MeasureResponseBytes(RequestId id, CallToolResult result)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = JsonSerializer.SerializeToNode(result, McpJsonUtilities.DefaultOptions),
        };
        return JsonSerializer.SerializeToUtf8Bytes<JsonRpcMessage>(response, McpJsonUtilities.DefaultOptions).Length;
    }

    public static CallToolResult CreateTooLargeResult(string toolName, int responseBytes, IReadOnlyList<string> limitHints)
    {
        var hints = limitHints.Count == 0
            ? "Das Werkzeug hat keine Eingrenzungsargumente."
            : "Eingrenzung: " + string.Join("; ", limitHints) + ".";
        var message = $"Die Antwort von {toolName} ist mit {responseBytes} Byte größer als die Grenze von {LimitBytes} Byte. {hints}";

        return ToolErrors.Create(ErrorCodes.ResponseTooLarge, message,
        [
            new KeyValuePair<string, JsonNode?>("responseBytes", responseBytes),
            new KeyValuePair<string, JsonNode?>("limitBytes", LimitBytes),
        ]);
    }
}
