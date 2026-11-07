using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Fachliche Ablehnung als Tool-Ergebnis: <c>isError: true</c>, erste Zeile der Code, danach der deutsche Text,
/// zusätzlich <c>_meta.errorCode</c>. Protokollfehler laufen nicht hier, sondern als JSON-RPC-Fehler.
/// </summary>
public static class ToolErrors
{
    public static CallToolResult Create(string code, string message, IEnumerable<KeyValuePair<string, JsonNode?>>? meta = null)
    {
        if (!ErrorCodes.All.Contains(code))
            throw new ArgumentException($"Unbekannter Fehlercode: {code}", nameof(code));

        var metaObject = new JsonObject { ["errorCode"] = code };
        if (meta != null)
        {
            foreach (var (key, value) in meta)
                metaObject[key] = value;
        }

        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = $"{code}\n{message}" }],
            Meta = metaObject,
        };
    }

    public static CallToolResult FromException(Csv2ZugferdException exception)
        => Create(exception.Code, exception.Message);
}
