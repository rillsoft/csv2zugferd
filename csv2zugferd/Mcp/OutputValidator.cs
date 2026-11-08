using System.Text.Json;
using ModelContextProtocol;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Prüft jedes erfolgreiche <c>structuredContent</c> gegen das veröffentlichte <c>outputSchema</c>.
/// Ein Verstoß ist JSON-RPC <c>-32603</c> mit dem Code <see cref="ErrorCodes.OutputSchemaInvalid"/>;
/// das fehlerhafte Ergebnis wird nicht ausgeliefert.
/// </summary>
public static class OutputValidator
{
    public static void EnsureValid(string toolName, JsonSchemaValidator validator, JsonElement structuredContent)
    {
        var violations = validator.Validate(structuredContent);
        if (violations.Count == 0)
            return;

        var details = string.Join("; ", violations.Select(v => v.ToString()));
        throw new McpProtocolException(
            $"{ErrorCodes.OutputSchemaInvalid}\nDie Antwort von {toolName} verletzt das veröffentlichte outputSchema: {details}",
            McpErrorCode.InternalError);
    }
}
