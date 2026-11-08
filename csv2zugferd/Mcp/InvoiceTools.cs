using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Csv2Zugferd.Models;
using s2industries.ZUGFeRD;
using static Csv2Zugferd.Mcp.SchemaBuilder;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Die fünf Werkzeuge des Einrichtungsassistenten. Jedes Werkzeug ruft ausschließlich die vorhandenen Fachmethoden
/// (<see cref="CsvInvoiceReader"/>, <see cref="YamlConfigLoader"/>, <see cref="InvoiceMapper"/>, <see cref="InvoiceExporter"/>).
/// Vertragsprosa ist englisch; Fehlertexte sind deutsch.
/// </summary>
public static class InvoiceTools
{
    public const string CsvHeaderGet = "csv2zugferd_csv_header_get";
    public const string ConfigSchemaGet = "csv2zugferd_config_schema_get";
    public const string ConfigGet = "csv2zugferd_config_get";
    public const string InvoiceGet = "csv2zugferd_invoice_get";
    public const string ConfigFileCreate = "csv2zugferd_config_file_create";

    public static IEnumerable<ToolDefinition> CreateDefinitions()
    {
        yield return CreateCsvHeaderGet();
        yield return CreateConfigSchemaGet();
        yield return CreateConfigGet();
        yield return CreateInvoiceGet();
        yield return CreateConfigFileCreate();
    }

    // --- gemeinsame Schemabausteine ---

    private static JsonObject DiagnosticSchema(string description) =>
        Object(description,
        [
            Prop("code", String("Stable English diagnostic code, for example invalid_date or config_field_ignored.")),
            Prop("path", String("Configuration path the finding refers to, dotted with list indexes, for example mapping.invoice.invoiceDate.")),
            Prop("message", String("Human-readable explanation in German.")),
        ], "code", "path", "message");

    private static JsonObject ConfigSourceDescription(JsonObject input)
    {
        input["oneOf"] = new JsonArray(
            new JsonObject { ["required"] = new JsonArray("configPath") },
            new JsonObject { ["required"] = new JsonArray("configYaml") });
        return input;
    }

    private static KeyValuePair<string, JsonNode> ConfigPathProp() =>
        Prop("configPath", String("Path to an existing YAML configuration file. Use either configPath or configYaml, not both.", minLength: 1));

    private static KeyValuePair<string, JsonNode> ConfigYamlProp() =>
        Prop("configYaml", String("YAML configuration as text (the content of config.yml). Use either configPath or configYaml, not both.", minLength: 1));

    // =====================================================================
    // csv2zugferd_csv_header_get
    // =====================================================================

    private static ToolDefinition CreateCsvHeaderGet()
    {
        var input = Object("Arguments for csv2zugferd_csv_header_get.",
        [
            Prop("csvPath", String("Path to the CSV file on the local file system (absolute or relative to the server's working directory).", minLength: 1)),
            Prop("separator", String("Column separator used in the file. Use \"\\t\" for tab. Same meaning as csv.separator in the YAML configuration.", defaultValue: ";", minLength: 1)),
            Prop("encoding", String("Text encoding of the file, for example UTF-8 or windows-1252. Same meaning as csv.encoding in the YAML configuration.", defaultValue: "UTF-8", minLength: 1)),
            Prop("sampleRows", Integer("Number of data rows to return as samples, 0 to 3. Default 0 returns no cell values, only the header.", minimum: 0, maximum: 3, defaultValue: 0)),
        ], "csvPath");

        var output = Object("Header of the CSV file.",
        [
            Prop("csvPath", String("The csvPath argument as given.")),
            Prop("columns", Array("Column names from the header row in file order.", String("Column name exactly as written in the header."))),
            Prop("columnCount", Integer("Number of columns in the header row.", minimum: 0)),
            Prop("rowCount", Integer("Number of data rows after the header.", minimum: 0)),
            Prop("separator", String("The separator that was applied.")),
            Prop("encoding", String("The encoding that was applied.")),
            Prop("sampleRows", Array("Sample data rows, each as a list of cell values in column order. Empty unless sampleRows > 0.",
                Array("One data row.", String("Cell value as text.")))),
        ], "csvPath", "columns", "columnCount", "rowCount", "separator", "encoding", "sampleRows");

        return new ToolDefinition
        {
            Name = CsvHeaderGet,
            Title = "Read CSV header",
            Description =
                "Reads the header row of a CSV file and returns the column names in file order plus the number of data rows. " +
                "Optionally returns up to 3 sample rows (sampleRows, default 0) so the mapping can be drafted from real values. " +
                "Does not validate a configuration (use csv2zugferd_config_get) and does not map an invoice (use csv2zugferd_invoice_get). " +
                "Read-only, writes no files.",
            InputSchema = ToElement(input),
            OutputSchema = ToElement(output),
            IsReadOnly = true,
            IsDestructive = false,
            IsIdempotent = true,
            IsOpenWorld = true,
            LimitHints = ["sampleRows: 0 (keine Beispielzeilen)"],
            Handler = (args, _) => Task.FromResult<object>(ReadCsvHeader(args)),
        };
    }

    public static ToolResults.CsvHeader ReadCsvHeader(JsonElement args)
    {
        var csvPath = args.GetProperty("csvPath").GetString()!;
        var separator = GetString(args, "separator") ?? ";";
        var encoding = GetString(args, "encoding") ?? "UTF-8";
        var sampleRows = args.TryGetProperty("sampleRows", out var sampleElement) ? sampleElement.GetInt32() : 0;

        var csvConfig = new CsvConfig { Separator = separator, Encoding = encoding, HasHeader = true };
        var table = CsvInvoiceReader.ReadTable(csvPath, csvConfig);

        var samples = table.Rows
            .Take(sampleRows)
            .Select(row => (IReadOnlyList<string>)table.Headers.Select(h => row.GetValueOrDefault(h) ?? string.Empty).ToList())
            .ToList();

        return new ToolResults.CsvHeader(csvPath, table.Headers, table.Headers.Count, table.Rows.Count, separator, encoding, samples);
    }

    // =====================================================================
    // csv2zugferd_config_schema_get
    // =====================================================================

    private static ToolDefinition CreateConfigSchemaGet()
    {
        var input = Object("Arguments for csv2zugferd_config_schema_get. Without section the complete reference is returned (about 100 KB).",
        [
            Prop("section", Enum("Optional configuration path to return only that subtree with the definitions it uses, for example zugferd, mapping.lineItems or mapping.paymentMeans. Semantics are filtered to the same subtree.", ConfigSchemaBuilder.SectionPaths)),
        ]);

        // Rekursive Beschreibung des tatsaechlich erzeugten JSON-Schema-Teilumfangs; properties/$defs/patternProperties
        // sind explizite Namensraeume (Dictionaries), keine Schema-Knoten.
        var node = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "A JSON Schema node of the configuration schema (Draft 2020-12 subset).",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["$schema"] = String("Draft identifier, only on the root."),
                ["$ref"] = String("Local reference into $defs, for example #/$defs/fieldMapping."),
                ["title"] = String("Title, only on the root."),
                ["description"] = String("English description of the key: effect, mode dependency, resolution order, value set and fallback."),
                ["type"] = new JsonObject
                {
                    ["description"] = "JSON type or list of types.",
                    ["anyOf"] = new JsonArray(String("Single type."), Array("List of types.", String("Type name."))),
                },
                ["enum"] = Array("Canonical values.", String("Value.")),
                ["properties"] = Map("Named child schemas (namespace, keys are configuration keys).", Ref("jsonSchemaNode")),
                ["patternProperties"] = Map("Child schemas by key pattern (namespace).", Ref("jsonSchemaNode")),
                ["additionalProperties"] = Boolean("Always false on object nodes: unknown keys are not allowed."),
                ["required"] = Array("Required keys.", String("Key name.")),
                ["items"] = Ref("jsonSchemaNode", "Schema of array elements."),
                ["anyOf"] = Array("Alternatives, at least one must match.", Ref("jsonSchemaNode")),
                ["oneOf"] = Array("Alternatives, exactly one must match.", Ref("jsonSchemaNode")),
                ["pattern"] = String("Regular expression for string values."),
                ["default"] = new JsonObject { ["type"] = new JsonArray("string", "boolean", "integer"), ["description"] = "Fixed default applied when the key is absent." },
                ["deprecated"] = Boolean("True for keys that are accepted but ignored."),
                ["minimum"] = Integer("Lower bound of an integer."),
                ["maximum"] = Integer("Upper bound of an integer."),
                ["minLength"] = Integer("Minimum string length."),
                ["$defs"] = Map("Shared definitions (namespace), only on the root.", Ref("jsonSchemaNode")),
            },
        };

        var semantic = Object("Effect and resolution of one configuration path.",
        [
            Prop("path", String("Dotted configuration path; list paths use [], generic field mapping keys use the prefix fieldMapping.")),
            Prop("status", Enum("active: always effective; conditional: effective only in the listed modes or under the described condition; ignored: accepted but never evaluated.", ["active", "conditional", "ignored"])),
            Prop("modes", Array("Modes in which the key is effective (rows, columns for lineItems.mode; manual for totals.mode). Empty for mode-independent keys.", String("Mode name."))),
            Prop("sources", Array("Sources that are effective at this field in resolution order. Empty for structural and scalar keys.", Enum("Source.", ["value", "column", "rules", "default"]))),
            Prop("valueSet", NullableString("Name of the value set in $defs (<name>Value) that value, default and rule results must come from, or null for free text.")),
            Prop("defaultValue", NullableString("Fixed default applied when the key is absent, or null.")),
            Prop("invalidFallback", NullableString("Value used when an unknown value is configured, or null when an unknown value is an error.")),
            Prop("description", String("English explanation of the effect and special cases.")),
        ], "path", "status", "modes", "sources", "valueSet", "defaultValue", "invalidFallback", "description");

        var output = Object("Complete reference of the YAML configuration, or the requested subtree.",
        [
            Prop("section", NullableString("The section argument as given, or null for the complete reference.")),
            Prop("schema", Ref("jsonSchemaNode", "JSON Schema (Draft 2020-12) of the whole YAML configuration (or of the section) with descriptions, defaults and value sets; $defs holds the shared definitions used.")),
            Prop("semantics", Array("Effect, modes, resolution sources and value sets per configuration path (filtered to the section when given).", semantic)),
            Prop("schemaVersion", String("csv2zugferd version the schema was generated from.")),
        ], "section", "schema", "semantics", "schemaVersion");
        output["$defs"] = new JsonObject { ["jsonSchemaNode"] = node };

        return new ToolDefinition
        {
            Name = ConfigSchemaGet,
            Title = "Get configuration reference",
            Description =
                "Returns the reference of the csv2zugferd YAML configuration: a JSON Schema of every key with description, " +
                "type, default and allowed values, plus semantics per key (effect, mode dependency, resolution order value > column > rules > default, " +
                "fallback for unknown values). Use it before drafting a configuration; no README is needed. " +
                "The complete reference is about 100 KB; pass section (for example mapping.lineItems) to get one subtree with its definitions. " +
                "Does not check a concrete configuration (use csv2zugferd_config_get). Read-only.",
            InputSchema = ToElement(input),
            OutputSchema = ToElement(output),
            IsReadOnly = true,
            IsDestructive = false,
            IsIdempotent = true,
            IsOpenWorld = false,
            LimitHints = ["section: <Pfad> (nur einen Teilbaum abrufen, z. B. mapping.lineItems)"],
            Handler = (args, _) => Task.FromResult<object>(GetConfigSchema(GetString(args, "section"))),
        };
    }

    public static ToolResults.ConfigSchema GetConfigSchema(string? section = null)
    {
        var all = ConfigSemantics.All.AsEnumerable();
        if (section != null)
        {
            var inSection = ConfigSemantics.All
                .Where(s => s.Path == section || s.Path.StartsWith(section + ".", StringComparison.Ordinal) || s.Path.StartsWith(section + "[", StringComparison.Ordinal))
                .ToList();
            var hasFieldMappings = inSection.Any(s => s.Sources.Contains("rules"));
            all = hasFieldMappings
                ? inSection.Concat(ConfigSemantics.All.Where(s => s.Path.StartsWith("fieldMapping.", StringComparison.Ordinal)))
                : inSection;
        }

        var semantics = all
            .Select(s => new ToolResults.FieldSemanticResult(s.Path, ConfigSemantics.StatusName(s.Status), s.Modes, s.Sources,
                s.ValueSet == null ? null : ConfigSchemaBuilder.ValueSetDefinitionName(ConfigValues.FindSet(s.ValueSet)!),
                s.DefaultValue, s.InvalidFallback, s.Description))
            .ToList();
        var schema = section == null ? ConfigSchemaBuilder.Schema : ConfigSchemaBuilder.GetSection(section);
        return new ToolResults.ConfigSchema(section, schema, semantics, McpServerCommand.GetVersion());
    }

    // =====================================================================
    // csv2zugferd_config_get
    // =====================================================================

    private static ToolDefinition CreateConfigGet()
    {
        var input = ConfigSourceDescription(Object("Arguments for csv2zugferd_config_get. Exactly one of configPath or configYaml is required.",
        [
            ConfigPathProp(),
            ConfigYamlProp(),
            Prop("csvPath", String("Optional CSV file whose header is compared with the referenced columns (structural check, no cell values are read).", minLength: 1)),
        ]));

        var output = Object("Structural check of a configuration.",
        [
            Prop("source", Enum("Where the configuration came from.", ["path", "yaml"])),
            Prop("csv", Object("Effective CSV settings.",
            [
                Prop("separator", String("Column separator.")),
                Prop("encoding", String("Text encoding.")),
                Prop("hasHeader", Boolean("Header row expected.")),
                Prop("decimalSeparator", String("Decimal separator of amounts.")),
                Prop("dateFormat", String("Date format of date values.")),
            ], "separator", "encoding", "hasHeader", "decimalSeparator", "dateFormat")),
            Prop("zugferd", Object("Effective output settings after fallbacks (canonical spelling).",
            [
                Prop("version", Enum("ZUGFeRD version.", ["2.3", "2.0", "1.0"])),
                Prop("profile", Enum("ZUGFeRD profile as library name; a configured EN16931 is reported as Comfort (same profile).", ["Minimum", "BasicWL", "Basic", "Comfort", "Extended", "XRechnung"])),
                Prop("format", Enum("Embedded XML syntax (PDF only).", ["CII", "UBL"])),
            ], "version", "profile", "format")),
            Prop("lineItemsMode", Enum("Effective line item mode after fallback.", ["rows", "columns"])),
            Prop("totalsMode", Enum("Effective totals mode after fallback.", ["auto", "manual"])),
            Prop("mappedFields", Array("Every configured field mapping with its configured sources in resolution order.", Object("Configured field.",
            [
                Prop("field", String("Full path starting with mapping, list entries with index.")),
                Prop("sources", Array("Configured sources in resolution order.", Enum("Source.", ["value", "column", "rules", "default"]))),
            ], "field", "sources"))),
            Prop("unknownKeys", Array("Keys in the YAML that no configuration key matches (typos); they are silently ignored by the CLI.", String("Dotted path with list indexes."))),
            Prop("invalidValues", Array("Configured values outside their value set (version, profile, format, modes, currency, country, unit code, tax codes, type codes, schemes).", Object("Invalid value.",
            [
                Prop("path", String("Dotted path of the value, including .value, .default or .rules[i].value.")),
                Prop("value", String("The configured value.")),
                Prop("allowed", Array("Canonical allowed values.", String("Allowed value."))),
                Prop("fallback", NullableString("Value the CLI will use instead, or null when the value makes the run fail.")),
            ], "path", "value", "allowed", "fallback"))),
            Prop("warnings", Array("Structural findings: ignored or missing sections for the chosen modes, fallbacks of version, profile and format.", DiagnosticSchema("Finding."))),
            Prop("columnsReferenced", Array("CSV column names the configuration can read in the effective mode (direct names; in columns mode also the indexed names built from columnPattern).", String("Column name."))),
            Prop("columnsMissing", Array("Only with csvPath: referenced columns without any match in the header (case-insensitive), neither direct nor indexed.", String("Column name or fragment as configured."))),
            Prop("columnsUnused", Array("Only with csvPath: header columns no mapping refers to.", String("Column name."))),
        ], "source", "csv", "zugferd", "lineItemsMode", "totalsMode", "mappedFields", "unknownKeys", "invalidValues", "warnings", "columnsReferenced");

        return new ToolDefinition
        {
            Name = ConfigGet,
            Title = "Check configuration",
            Description =
                "Loads a YAML configuration from configPath or configYaml and reports its structure: effective settings and modes, every mapped field with its sources, " +
                "unknown keys (typos), values outside the allowed sets with the fallback the CLI would apply, and ignored or missing sections. " +
                "With csvPath the referenced columns are compared with the CSV header (missing and unused columns). " +
                "Does not read cell values and does not calculate totals; use csv2zugferd_invoice_get for a trial run. Read-only, writes no files.",
            InputSchema = ToElement(input),
            OutputSchema = ToElement(output),
            IsReadOnly = true,
            IsDestructive = false,
            IsIdempotent = true,
            IsOpenWorld = true,
            Handler = (args, _) => Task.FromResult<object>(GetConfig(args)),
        };
    }

    public static ToolResults.ConfigCheck GetConfig(JsonElement args)
    {
        var (config, source, yaml) = LoadConfig(args);
        var csvPath = GetString(args, "csvPath");

        var diagnostics = new List<MappingDiagnostic>(InvoiceMapper.CheckConfiguration(config));
        var version = InvoiceExporter.ResolveVersion(config.Zugferd, diagnostics);
        var profile = InvoiceExporter.ResolveProfile(config.Zugferd, diagnostics);
        var format = InvoiceExporter.ResolveFormat(config.Zugferd, diagnostics);

        var invalidValues = ConfigInspector.CollectInvalidValues(config);
        var invalidPaths = invalidValues.Select(v => v.Path).ToHashSet(StringComparer.Ordinal);
        var warnings = diagnostics
            .Where(d => !(d.Code == DiagnosticCodes.ConfigValueFallback && invalidPaths.Contains(d.Path)))
            .Select(ToolResults.Diagnostic.From)
            .ToList();

        IReadOnlyList<string>? headers = null;
        if (csvPath != null)
            headers = CsvInvoiceReader.ReadTable(csvPath, config.Csv).Headers;
        var columns = ConfigInspector.CheckColumns(config, headers);

        return new ToolResults.ConfigCheck(
            source,
            new ToolResults.CsvSettings(config.Csv.Separator, config.Csv.Encoding, config.Csv.HasHeader, config.Csv.DecimalSeparator, config.Csv.DateFormat),
            new ToolResults.ZugferdSettings(ConfigValues.CanonicalVersion(version), ConfigValues.CanonicalProfile(profile), ConfigValues.CanonicalFormat(format)),
            ConfigValues.ParseLineItemsMode(config.Mapping.LineItems?.Mode),
            config.Mapping.Totals == null ? ConfigValues.TotalsModeAuto : ConfigValues.ParseTotalsMode(config.Mapping.Totals.Mode),
            ConfigInspector.CollectMappedFields(config),
            YamlConfigLoader.FindUnknownKeys(yaml),
            invalidValues,
            warnings,
            columns.Referenced,
            columns.Missing,
            columns.Unused);
    }

    // =====================================================================
    // csv2zugferd_invoice_get
    // =====================================================================

    private static ToolDefinition CreateInvoiceGet()
    {
        var input = ConfigSourceDescription(Object("Arguments for csv2zugferd_invoice_get. Exactly one of configPath or configYaml is required.",
        [
            Prop("csvPath", String("Path to the CSV file with the invoice data.", minLength: 1)),
            ConfigPathProp(),
            ConfigYamlProp(),
            Prop("includeLineItems", Boolean("True adds the mapped line items (name, quantity, unit, price, line total, tax rate) to the result.", defaultValue: false)),
            Prop("includeParties", Boolean("True adds seller and buyer (name, city, country) to the result.", defaultValue: false)),
            Prop("pdfTemplatePath", String("Optional PDF template: the trial run also embeds the XML into a copy of this PDF in the temp directory (deleted afterwards) and reports its size, which checks zugferd.format and the template.", minLength: 1)),
        ], "csvPath"));

        var party = new JsonObject
        {
            ["description"] = "Party or null when not mapped.",
            ["anyOf"] = new JsonArray(
                Object("Party.",
                [
                    Prop("name", String("Party name.")),
                    Prop("city", String("City.")),
                    Prop("country", String("Country code.")),
                ], "name", "city", "country"),
                new JsonObject { ["type"] = "null" }),
        };

        var output = Object("Result of the trial run (same code path as the CLI up to writing the XML).",
        [
            Prop("invoiceNumber", String("Invoice number (BT-1) as mapped.")),
            Prop("invoiceDate", String("Invoice date (BT-2) as yyyy-MM-dd; 0001-01-01 when the date could not be parsed.", pattern: "^[0-9]{4}-[0-9]{2}-[0-9]{2}$")),
            Prop("typeCode", String("Invoice type code (BT-3), for example 380.")),
            Prop("currency", String("Currency code (BT-5).")),
            Prop("profile", Enum("Effective ZUGFeRD profile as library name; a configured EN16931 is reported as Comfort (same profile).", ["Minimum", "BasicWL", "Basic", "Comfort", "Extended", "XRechnung"])),
            Prop("version", Enum("Effective ZUGFeRD version.", ["2.3", "2.0", "1.0"])),
            Prop("format", Enum("Effective XML syntax (applied in the PDF only).", ["CII", "UBL"])),
            Prop("rowCount", Integer("Data rows read from the CSV.", minimum: 0)),
            Prop("lineItemCount", Integer("Line items in the invoice.", minimum: 0)),
            Prop("totals", Object("Document totals (BG-22).",
            [
                Prop("lineTotal", Number("Sum of line net amounts (BT-106).")),
                Prop("allowanceTotal", Number("Document allowances (BT-107).")),
                Prop("chargeTotal", Number("Document charges (BT-108).")),
                Prop("taxBasisTotal", Number("Total without VAT (BT-109).")),
                Prop("taxTotal", Number("Total VAT (BT-110).")),
                Prop("grandTotal", Number("Total with VAT (BT-112).")),
                Prop("duePayable", Number("Amount due for payment (BT-115).")),
            ], "lineTotal", "allowanceTotal", "chargeTotal", "taxBasisTotal", "taxTotal", "grandTotal", "duePayable")),
            Prop("taxBreakdown", Array("VAT breakdown per rate and category (BG-23).", Object("Tax group.",
            [
                Prop("percent", Number("VAT rate in percent.")),
                Prop("categoryCode", String("VAT category code, for example S.")),
                Prop("basisAmount", Number("Taxable amount of the group.")),
                Prop("taxAmount", Number("VAT amount of the group.")),
            ], "percent", "categoryCode", "basisAmount", "taxAmount"))),
            Prop("warnings", Array("All findings of the run: document checks, fallbacks, unparsable values, ignored or missing sections.", DiagnosticSchema("Finding."))),
            Prop("xmlBytes", Integer("Size of the generated XML in bytes.", minimum: 0)),
            Prop("pdfBytes", Integer("Size of the generated PDF in bytes; only with pdfTemplatePath.", minimum: 0)),
            Prop("lineItems", Array("Mapped line items; only with includeLineItems.", Object("Line item.",
            [
                Prop("name", String("Item name (BT-153).")),
                Prop("quantity", Number("Billed quantity (BT-129).")),
                Prop("unitCode", String("Unit code (BT-130).")),
                Prop("netUnitPrice", Number("Net unit price after item discount (BT-146).")),
                Prop("lineTotal", Number("Line net amount (BT-131).")),
                Prop("taxPercent", Number("VAT rate (BT-152).")),
            ], "name", "quantity", "unitCode", "netUnitPrice", "lineTotal", "taxPercent"))),
            Prop("parties", Object("Seller and buyer; only with includeParties.",
            [
                Prop("seller", party),
                Prop("buyer", party.DeepClone()),
            ], "seller", "buyer")),
        ], "invoiceNumber", "invoiceDate", "typeCode", "currency", "profile", "version", "format", "rowCount", "lineItemCount",
           "totals", "taxBreakdown", "warnings", "xmlBytes");

        return new ToolDefinition
        {
            Name = InvoiceGet,
            Title = "Trial run invoice mapping",
            Description =
                "Runs the real csv2zugferd mapping for one CSV file with the given configuration (configPath or configYaml) without writing any output file: " +
                "reads the CSV, maps header, parties, line items and totals, builds the ZUGFeRD XML in memory and returns invoice number, type, totals, tax breakdown, " +
                "warnings with codes and paths, and the XML size. includeLineItems and includeParties (default false) add details; pdfTemplatePath also embeds the XML into a temporary PDF copy to check format and template. " +
                "Does not create the XML or PDF for production; that is done by the csv2zugferd CLI (csv2zugferd_config_file_create returns the command). " +
                "Read-only for the user's files; a temporary PDF is created and deleted only with pdfTemplatePath.",
            InputSchema = ToElement(input),
            OutputSchema = ToElement(output),
            IsReadOnly = true,
            IsDestructive = false,
            IsIdempotent = true,
            IsOpenWorld = true,
            LimitHints = ["includeLineItems: false", "includeParties: false"],
            Handler = GetInvoiceAsync,
        };
    }

    public static async Task<object> GetInvoiceAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var csvPath = args.GetProperty("csvPath").GetString()!;
        var (config, _, _) = LoadConfig(args);
        var includeLineItems = GetBool(args, "includeLineItems");
        var includeParties = GetBool(args, "includeParties");
        var pdfTemplatePath = GetString(args, "pdfTemplatePath");

        var rows = CsvInvoiceReader.Read(csvPath, config.Csv);
        if (rows.Count == 0)
            throw new Csv2ZugferdException(ErrorCodes.CsvEmpty, $"CSV-Datei enthält keine Datenzeilen: {csvPath}");

        var diagnostics = new List<MappingDiagnostic>();
        InvoiceDescriptor desc;
        try
        {
            desc = InvoiceMapper.Map(rows, config, diagnostics);
        }
        catch (Csv2ZugferdException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Csv2ZugferdException(ErrorCodes.MappingFailed, $"Mapping fehlgeschlagen: {ex.Message}", ex);
        }

        long xmlBytes;
        using (var stream = new MemoryStream())
        {
            InvoiceExporter.WriteXml(desc, stream, config.Zugferd, diagnostics);
            xmlBytes = stream.Length;
        }

        long? pdfBytes = null;
        if (pdfTemplatePath != null)
        {
            if (!File.Exists(pdfTemplatePath))
                throw new Csv2ZugferdException(ErrorCodes.PdfTemplateNotFound, $"PDF-Vorlage nicht gefunden: {pdfTemplatePath}");

            // readOnlyHint bleibt true: Die PDF entsteht nur als temporaere Datei im Temp-Verzeichnis des Benutzers
            // und wird im finally geloescht; der Bestand des Anwenders bleibt unveraendert.
            var tempPath = Path.GetTempFileName();
            try
            {
                await InvoiceExporter.SavePdfAsync(desc, tempPath, pdfTemplatePath, config.Zugferd, diagnostics);
                pdfBytes = new FileInfo(tempPath).Length;
            }
            catch (Exception ex)
            {
                throw new Csv2ZugferdException(ErrorCodes.PdfCreateFailed, $"PDF konnte nicht erzeugt werden: {ex.Message}", ex);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        var totals = new ToolResults.Totals(
            desc.LineTotalAmount ?? 0m,
            desc.AllowanceTotalAmount ?? 0m,
            desc.ChargeTotalAmount ?? 0m,
            desc.TaxBasisAmount ?? 0m,
            desc.TaxTotalAmount ?? 0m,
            desc.GrandTotalAmount ?? 0m,
            desc.DuePayableAmount ?? 0m);

        var taxBreakdown = desc.GetApplicableTradeTaxes()
            .Select(t => new ToolResults.TaxBreakdownEntry(t.Percent, t.CategoryCode?.ToString() ?? string.Empty, t.BasisAmount, t.TaxAmount))
            .ToList();

        var lineItems = includeLineItems
            ? desc.TradeLineItems.Select(i => new ToolResults.LineItem(
                i.Name ?? string.Empty, i.BilledQuantity, i.UnitCode?.ToString() ?? string.Empty, i.NetUnitPrice,
                i.LineTotalAmount ?? Math.Round(i.NetUnitPrice * i.BilledQuantity, 2, MidpointRounding.AwayFromZero), i.TaxPercent)).ToList()
            : null;

        var parties = includeParties
            ? new ToolResults.Parties(ToParty(desc.Seller), ToParty(desc.Buyer))
            : null;

        return new ToolResults.Invoice(
            desc.InvoiceNo ?? string.Empty,
            (desc.InvoiceDate ?? DateTime.MinValue).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ConfigValues.GetInvoiceTypeCode(desc.Type),
            desc.Currency.ToString(),
            ConfigValues.CanonicalProfile(InvoiceExporter.ResolveProfile(config.Zugferd, null)),
            ConfigValues.CanonicalVersion(InvoiceExporter.ResolveVersion(config.Zugferd, null)),
            ConfigValues.CanonicalFormat(InvoiceExporter.ResolveFormat(config.Zugferd, null)),
            rows.Count,
            desc.TradeLineItems.Count,
            totals,
            taxBreakdown,
            diagnostics.Select(ToolResults.Diagnostic.From).ToList(),
            xmlBytes,
            pdfBytes,
            lineItems,
            parties);
    }

    private static ToolResults.Party? ToParty(Party? party) =>
        party == null ? null : new ToolResults.Party(party.Name ?? string.Empty, party.City ?? string.Empty, $"{party.Country}");

    // =====================================================================
    // csv2zugferd_config_file_create
    // =====================================================================

    private static ToolDefinition CreateConfigFileCreate()
    {
        var input = Object("Arguments for csv2zugferd_config_file_create.",
        [
            Prop("configYaml", String("The YAML configuration to save (content of the file).", minLength: 1)),
            Prop("configPath", String("Target file path, for example C:/invoices/config.yml. The directory must exist.", minLength: 1)),
            Prop("overwrite", Boolean("True replaces an existing file at configPath. Default false: an existing file is never touched (file_exists).", defaultValue: false)),
            Prop("csvPath", String("Optional CSV path inserted into the returned CLI command instead of a placeholder.", minLength: 1)),
        ], "configYaml", "configPath");

        var output = Object("Saved configuration file.",
        [
            Prop("configPath", String("Full path of the written file.")),
            Prop("bytes", Integer("Size of the written file in bytes (UTF-8 without BOM).", minimum: 0)),
            Prop("cliCommand", String("Complete csv2zugferd CLI command for a production run with this configuration; placeholders in angle brackets mark paths that were not given.")),
        ], "configPath", "bytes", "cliCommand");

        return new ToolDefinition
        {
            Name = ConfigFileCreate,
            Title = "Save configuration file",
            Description =
                "Validates configYaml (it must deserialize as a csv2zugferd configuration) and writes it to configPath as UTF-8 without BOM. " +
                "An existing file is replaced only with overwrite: true; otherwise the call is rejected with file_exists and nothing is written. " +
                "Returns the full path, the size and the csv2zugferd CLI command that uses the saved file for production runs. " +
                "Does not run the mapping (use csv2zugferd_invoice_get first) and does not create XML or PDF files.",
            InputSchema = ToElement(input),
            OutputSchema = ToElement(output),
            IsReadOnly = false,
            IsDestructive = true,
            IsIdempotent = true,
            IsOpenWorld = true,
            Handler = (args, _) => Task.FromResult<object>(CreateConfigFile(args)),
        };
    }

    public static ToolResults.ConfigFile CreateConfigFile(JsonElement args)
    {
        var configYaml = args.GetProperty("configYaml").GetString()!;
        var configPath = args.GetProperty("configPath").GetString()!;
        var overwrite = GetBool(args, "overwrite");
        var csvPath = GetString(args, "csvPath");

        // Pruefung vor Wirkung: ungueltiges YAML erzeugt keine Datei
        YamlConfigLoader.LoadFromString(configYaml);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(configPath);
        }
        catch (Exception ex)
        {
            throw new Csv2ZugferdException(ErrorCodes.OutputPathInvalid, $"Ungültiger Zielpfad '{configPath}': {ex.Message}", ex);
        }

        if (File.Exists(fullPath) && !overwrite)
            throw new Csv2ZugferdException(ErrorCodes.FileExists, $"Datei existiert bereits und overwrite ist nicht gesetzt: {fullPath}");

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new Csv2ZugferdException(ErrorCodes.OutputPathInvalid, $"Zielverzeichnis existiert nicht: {directory}");

        try
        {
            File.WriteAllText(fullPath, configYaml, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Csv2ZugferdException(ErrorCodes.OutputPathInvalid, $"Datei konnte nicht geschrieben werden: {ex.Message}", ex);
        }

        var bytes = new FileInfo(fullPath).Length;
        var cliCommand = $"csv2zugferd --csv \"{csvPath ?? "<csv-file>"}\" --pdf \"<pdf-template>\" --config \"{fullPath}\" --output \"<output-directory>\"";
        return new ToolResults.ConfigFile(fullPath, bytes, cliCommand);
    }

    // --- Helfer ---

    /// <summary>Lädt die Konfiguration aus genau einer Quelle; liefert auch den YAML-Text für die Schlüsselprüfung.</summary>
    private static (AppConfig Config, string Source, string Yaml) LoadConfig(JsonElement args)
    {
        var configPath = GetString(args, "configPath");
        var configYaml = GetString(args, "configYaml");

        if (configPath != null && configYaml != null)
            throw new Csv2ZugferdException(ErrorCodes.ConfigSourceAmbiguous, "configPath und configYaml sind beide angegeben; genau eine Quelle ist zulässig");
        if (configPath == null && configYaml == null)
            throw new Csv2ZugferdException(ErrorCodes.ConfigSourceMissing, "Weder configPath noch configYaml ist angegeben");

        if (configPath != null)
        {
            var config = YamlConfigLoader.Load(configPath);
            return (config, "path", File.ReadAllText(configPath));
        }

        return (YamlConfigLoader.LoadFromString(configYaml!), "yaml", configYaml!);
    }

    private static string? GetString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
