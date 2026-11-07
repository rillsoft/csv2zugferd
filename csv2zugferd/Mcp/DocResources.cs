using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Eingebettete Ressourcen (README und Beispielkonfigurationen). Ergänzung für Menschen und Clients,
/// die Ressourcen anzeigen; die verbindliche Quelle für das Modell ist <c>csv2zugferd_config_schema_get</c>.
/// </summary>
public static class DocResources
{
    private sealed record DocResource(string Uri, string Name, string Title, string Description, string MimeType, string LogicalName);

    private static readonly DocResource[] s_resources =
    [
        new("csv2zugferd://docs/config-reference", "config-reference", "Configuration reference (README)",
            "The csv2zugferd README with the YAML configuration reference, CLI usage and examples (German).",
            "text/markdown", "Csv2Zugferd.Docs.README.md"),
        new("csv2zugferd://examples/config-rows", "config-rows", "Example: rows mode",
            "Example YAML configuration for rows mode (one CSV row per line item), used by the test suite.",
            "application/yaml", "Csv2Zugferd.Docs.config_rows.yml"),
        new("csv2zugferd://examples/config-columns-suffix", "config-columns-suffix", "Example: columns mode (suffix)",
            "Example YAML configuration for columns mode with numbered suffix columns (Artikel_001, Artikel_002, ...), used by the test suite.",
            "application/yaml", "Csv2Zugferd.Docs.config_columns_suffix.yml"),
    ];

    public static ListResourcesResult List()
    {
        return new ListResourcesResult
        {
            Resources = s_resources
                .OrderBy(r => r.Uri, StringComparer.Ordinal)
                .Select(r => new Resource
                {
                    Uri = r.Uri,
                    Name = r.Name,
                    Title = r.Title,
                    Description = r.Description,
                    MimeType = r.MimeType,
                })
                .ToList(),
        };
    }

    public static ReadResourceResult Read(string? uri)
    {
        var resource = s_resources.FirstOrDefault(r => string.Equals(r.Uri, uri, StringComparison.Ordinal))
            ?? throw new McpProtocolException($"Unbekannte Ressource: '{uri}'", McpErrorCode.ResourceNotFound);

        return new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = resource.Uri,
                    MimeType = resource.MimeType,
                    Text = ReadEmbedded(resource.LogicalName),
                },
            ],
        };
    }

    public static string ReadEmbedded(string logicalName)
    {
        using var stream = typeof(DocResources).Assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Eingebettete Ressource fehlt: {logicalName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
