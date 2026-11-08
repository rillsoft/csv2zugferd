using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Ausgehender Nachrichtenfilter: Trägt bei JSON-RPC-Fehlern, deren Meldung mit einem Code aus
/// <see cref="ErrorCodes"/> beginnt (erste Zeile), diesen Code zusätzlich als <c>error.data.code</c> ein.
/// Das SDK selbst setzt bei <c>McpProtocolException</c> nur <c>code</c> und <c>message</c>.
/// </summary>
public static class ErrorDataFilter
{
    public static McpMessageHandler Apply(McpMessageHandler next) =>
        async (context, cancellationToken) =>
        {
            Enrich(context.JsonRpcMessage);
            await next(context, cancellationToken);
        };

    /// <summary>Ergänzt <c>error.data.code</c>, wenn die Meldung mit einem bekannten Code beginnt; liefert true, wenn etwas gesetzt wurde.</summary>
    public static bool Enrich(JsonRpcMessage message)
    {
        if (message is not JsonRpcError { Error: { Data: null } detail } || !TryExtractCode(detail.Message, out var code))
            return false;

        detail.Data = JsonSerializer.SerializeToElement(new JsonObject { ["code"] = code });
        return true;
    }

    private static bool TryExtractCode(string? message, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrEmpty(message)) return false;
        var newline = message.IndexOf('\n');
        var firstLine = (newline >= 0 ? message[..newline] : message).Trim();
        if (!ErrorCodes.All.Contains(firstLine)) return false;
        code = firstLine;
        return true;
    }
}
