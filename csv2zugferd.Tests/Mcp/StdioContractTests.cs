using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Csv2Zugferd.Mcp;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests.Mcp;

/// <summary>Vertrags- und Protokollprüfungen gegen den gestarteten Prozess: Werkzeugliste, Schemata, Fehlerwege, Antwortgrenze.</summary>
[TestFixture]
public class StdioContractTests
{
    private McpStdioClient _client = null!;

    // Sperrliste deutscher Wörter für Titel und Beschreibungen des Vertrags (ganze Wörter, case-sensitiv).
    private static readonly string[] s_germanWords =
    [
        "und", "oder", "nicht", "der", "die", "das", "mit", "wird", "werden", "eine", "einer", "Datei", "Rechnung",
        "Konfiguration", "Spalte", "Spalten", "Prüfung", "Rechnungsnummer", "Wert", "Werte", "Pflicht", "Zeile",
    ];

    [SetUp]
    public void SetUp()
    {
        _client = new McpStdioClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
    }

    private static string TestData(string file) => McpStdioClient.TestDataPath(file);

    private static string ReadTestYaml(string file) => File.ReadAllText(TestData(file));

    private static IEnumerable<JsonObject> SchemaNodes(JsonNode? node)
    {
        if (node is not JsonObject obj) yield break;
        yield return obj;
        foreach (var key in new[] { "properties", "patternProperties", "$defs" })
        {
            if (obj[key] is JsonObject ns)
            {
                foreach (var (_, child) in ns)
                {
                    foreach (var n in SchemaNodes(child)) yield return n;
                }
            }
        }
        foreach (var key in new[] { "items", "additionalProperties", "not" })
        {
            foreach (var n in SchemaNodes(obj[key])) yield return n;
        }
        foreach (var key in new[] { "anyOf", "oneOf", "allOf", "prefixItems" })
        {
            if (obj[key] is JsonArray array)
            {
                foreach (var child in array)
                {
                    foreach (var n in SchemaNodes(child)) yield return n;
                }
            }
        }
    }

    private static bool IsObjectNode(JsonObject node) =>
        node["type"] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == "object"
        || node["type"] is JsonArray a && a.Any(t => t!.GetValue<string>() == "object")
        || node["properties"] != null;

    // --- tools/list ---

    [Test]
    public async Task ToolsList_IsDeterministicallySorted()
    {
        await _client.InitializeAsync();

        var first = (await _client.ListToolsAsync())["result"]!.ToJsonString();
        var second = (await _client.ListToolsAsync())["result"]!.ToJsonString();

        first.ShouldBe(second);
        var names = JsonNode.Parse(first)!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();
        names.ShouldBe(names.OrderBy(n => n, StringComparer.Ordinal).ToList());
        names.Count.ShouldBe(5);
        names.All(n => Regex.IsMatch(n, "^csv2zugferd_[a-z0-9_]+$")).ShouldBeTrue();
    }

    [Test]
    public async Task ToolsList_ObjectSchemasAreClosedAndDictionaryKeysAreExplicit()
    {
        await _client.InitializeAsync();
        var tools = (await _client.ListToolsAsync())["result"]!["tools"]!.AsArray();

        foreach (var tool in tools)
        {
            var name = tool!["name"]!.GetValue<string>();
            foreach (var schemaKey in new[] { "inputSchema", "outputSchema" })
            {
                var schema = tool[schemaKey].ShouldNotBeNull($"{name}.{schemaKey}");
                foreach (var node in SchemaNodes(schema))
                {
                    if (!IsObjectNode(node)) continue;
                    node["additionalProperties"].ShouldNotBeNull($"{name}.{schemaKey}: offenes Objekt {node.ToJsonString()[..Math.Min(120, node.ToJsonString().Length)]}");
                    node["additionalProperties"]!.GetValue<bool>().ShouldBeFalse($"{name}.{schemaKey}");
                    (node["properties"] != null || node["patternProperties"] != null).ShouldBeTrue($"{name}.{schemaKey}: Objekt ohne explizite Schlüssel");
                }
            }
        }
    }

    [Test]
    public async Task ToolsList_AllToolsHaveTitleAndFourAnnotations()
    {
        await _client.InitializeAsync();
        var tools = (await _client.ListToolsAsync())["result"]!["tools"]!.AsArray();

        foreach (var tool in tools)
        {
            var name = tool!["name"]!.GetValue<string>();
            tool["title"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace(name);
            tool["description"]!.GetValue<string>().Length.ShouldBeGreaterThan(80, name);
            var annotations = tool["annotations"]!.AsObject();
            foreach (var hint in new[] { "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint" })
                annotations[hint].ShouldNotBeNull($"{name}.{hint}");
        }
    }

    [Test]
    public async Task ToolsList_ContractProseIsAsciiAndEnglish()
    {
        await _client.InitializeAsync();
        var tools = (await _client.ListToolsAsync())["result"]!["tools"]!.AsArray();

        var texts = new List<(string Where, string Text)>();
        foreach (var tool in tools)
        {
            var name = tool!["name"]!.GetValue<string>();
            texts.Add(($"{name}.title", tool["title"]!.GetValue<string>()));
            texts.Add(($"{name}.description", tool["description"]!.GetValue<string>()));
            foreach (var schemaKey in new[] { "inputSchema", "outputSchema" })
            {
                foreach (var node in SchemaNodes(tool[schemaKey]))
                {
                    if (node["description"] is JsonValue d)
                        texts.Add(($"{name}.{schemaKey}", d.GetValue<string>()));
                }
            }
        }

        texts.Count.ShouldBeGreaterThan(50);
        foreach (var (where, text) in texts)
        {
            text.All(c => c < 128).ShouldBeTrue($"{where}: nicht ASCII: {text}");
            foreach (var word in s_germanWords)
                Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b").ShouldBeFalse($"{where}: deutsches Wort '{word}' in: {text}");
        }
    }

    [Test]
    public async Task ToolsList_DestructiveHintMatchesOverwriteProse()
    {
        await _client.InitializeAsync();
        var tools = (await _client.ListToolsAsync())["result"]!["tools"]!.AsArray();

        foreach (var tool in tools)
        {
            var name = tool!["name"]!.GetValue<string>();
            var description = tool["description"]!.GetValue<string>();
            var mentionsLoss = Regex.IsMatch(description, @"\b(overwrite|overwrites|overwriting|replace|replaces|replaced|lose|loses|discard|discards)\b", RegexOptions.IgnoreCase);
            tool["annotations"]!["destructiveHint"]!.GetValue<bool>().ShouldBe(mentionsLoss, name);
        }
    }

    // --- Fehlerwege ---

    [Test]
    public async Task ToolsCall_UnknownTool_ReturnsMinus32602()
    {
        await _client.InitializeAsync();

        var response = await _client.CallToolAsync("csv2zugferd_nope", new JsonObject());

        response["result"].ShouldBeNull();
        response["error"]!["code"]!.GetValue<int>().ShouldBe(-32602);
        response["error"]!["message"]!.GetValue<string>().ShouldContain("csv2zugferd_nope");
    }

    [Test]
    public async Task ToolsCall_SchemaViolation_ReturnsMinus32602()
    {
        await _client.InitializeAsync();

        var response = await _client.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject
        {
            ["csvPath"] = TestData("test_rows.csv"),
            ["sampleRows"] = 9,
            ["typo"] = true,
        });

        response["error"]!["code"]!.GetValue<int>().ShouldBe(-32602);
        var message = response["error"]!["message"]!.GetValue<string>();
        message.ShouldContain("sampleRows");
        message.ShouldContain("typo");
    }

    [Test]
    public async Task ToolsCall_MissingCsv_ReturnsIsErrorWithCsvFileNotFound()
    {
        await _client.InitializeAsync();

        var response = await _client.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject { ["csvPath"] = TestData("nope.csv") });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().ShouldBeTrue();
        result["structuredContent"].ShouldBeNull();
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        text.Split('\n')[0].ShouldBe(ErrorCodes.CsvFileNotFound);
        result["_meta"]!["errorCode"]!.GetValue<string>().ShouldBe(ErrorCodes.CsvFileNotFound);
    }

    [Test]
    public async Task ToolsCall_WarningProvokingConfig_KeepsStdoutJsonOnly()
    {
        await _client.InitializeAsync();
        var yaml = ReadTestYaml("config_rows.yml").Replace("profile: \"Extended\"", "profile: \"Komfort\"").Replace("value: \"EUR\"", "value: \"Taler\"");

        var response = await _client.CallToolAsync("csv2zugferd_invoice_get", new JsonObject
        {
            ["csvPath"] = TestData("test_rows.csv"),
            ["configYaml"] = yaml,
        });
        var exitCode = _client.Shutdown();

        exitCode.ShouldBe(0);
        var warnings = response["result"]!["structuredContent"]!["warnings"]!.AsArray().Select(w => w!["code"]!.GetValue<string>()).ToList();
        warnings.ShouldContain(DiagnosticCodes.ConfigValueFallback);
        _client.Stderr.ShouldContain("WRN");
        foreach (var line in _client.StdoutLines)
            Should.NotThrow(() => JsonNode.Parse(line)!.AsObject()["jsonrpc"]!.GetValue<string>().ShouldBe("2.0"));
    }

    [Test]
    public async Task ToolsCall_InvalidFinancialData_ReturnsStructuredDiagnostics()
    {
        await _client.InitializeAsync();
        var yaml = ReadTestYaml("config_rows.yml")
            .Replace("DE02 1203 0000 0000 2020 51", "DE02 1203 0000 0000 2020 52")
            .Replace("column: \"Rechnungsdatum\"", "value: \"gestern\"")
            .Replace("column: \"Einzelpreis_Netto\"", "value: \"teuer\"");

        var response = await _client.CallToolAsync("csv2zugferd_invoice_get", new JsonObject
        {
            ["csvPath"] = TestData("test_rows.csv"),
            ["configYaml"] = yaml,
        });

        var warnings = response["result"]!["structuredContent"]!["warnings"]!.AsArray();
        warnings.Select(w => w!["code"]!.GetValue<string>()).ShouldContain(DiagnosticCodes.InvalidIbanChecksum);
        warnings.Select(w => w!["code"]!.GetValue<string>()).ShouldContain(DiagnosticCodes.InvalidDate);
        warnings.Select(w => w!["code"]!.GetValue<string>()).ShouldContain(DiagnosticCodes.InvalidDecimal);
        warnings.Single(w => w!["code"]!.GetValue<string>() == DiagnosticCodes.InvalidIbanChecksum)!["path"]!.GetValue<string>().ShouldBe("mapping.paymentMeans.sellerAccounts[0].iban");
        foreach (var warning in warnings)
        {
            warning!["code"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            warning["path"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
            warning["message"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        }
    }

    // --- Ausgabeschemata über den echten Prozess ---

    [Test]
    public async Task ToolsCall_AllSuccessVariants_MatchAdvertisedOutputSchema()
    {
        await _client.InitializeAsync();
        var tools = (await _client.ListToolsAsync())["result"]!["tools"]!.AsArray()
            .ToDictionary(t => t!["name"]!.GetValue<string>(), t => new JsonSchemaValidator(JsonSerializer.SerializeToElement(t!["outputSchema"])));
        var tempDir = Path.Combine(Path.GetTempPath(), "csv2zugferd-contract-" + Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "config.yml");

        try
        {
            var variants = new (string Tool, JsonObject Args)[]
            {
                ("csv2zugferd_csv_header_get", new JsonObject { ["csvPath"] = TestData("test_rows.csv") }),
                ("csv2zugferd_csv_header_get", new JsonObject { ["csvPath"] = TestData("test_columns_prefix.csv"), ["sampleRows"] = 1 }),
                ("csv2zugferd_config_schema_get", new JsonObject()),
                ("csv2zugferd_config_schema_get", new JsonObject { ["section"] = "mapping.lineItems" }),
                ("csv2zugferd_config_get", new JsonObject { ["configPath"] = TestData("config_rows.yml") }),
                ("csv2zugferd_config_get", new JsonObject { ["configYaml"] = ReadTestYaml("config_columns_suffix.yml"), ["csvPath"] = TestData("test_columns_suffix.csv") }),
                ("csv2zugferd_invoice_get", new JsonObject { ["csvPath"] = TestData("test_rows.csv"), ["configPath"] = TestData("config_rows.yml") }),
                ("csv2zugferd_invoice_get", new JsonObject { ["csvPath"] = TestData("test_rows.csv"), ["configPath"] = TestData("config_rows.yml"), ["includeLineItems"] = true, ["includeParties"] = true }),
                ("csv2zugferd_invoice_get", new JsonObject { ["csvPath"] = TestData("test_rows.csv"), ["configPath"] = TestData("config_rows.yml"), ["pdfTemplatePath"] = TestData("template.pdf") }),
                ("csv2zugferd_invoice_get", new JsonObject { ["csvPath"] = TestData("test_creditnote.csv"), ["configYaml"] = ReadTestYaml("config_creditnote.yml").Replace("version: \"2.3\"", "version: \"9\"") }),
                ("csv2zugferd_config_file_create", new JsonObject { ["configYaml"] = ReadTestYaml("config_rows.yml"), ["configPath"] = configPath }),
                ("csv2zugferd_config_file_create", new JsonObject { ["configYaml"] = ReadTestYaml("config_rows.yml"), ["configPath"] = configPath, ["overwrite"] = true, ["csvPath"] = "a.csv" }),
            };

            foreach (var (tool, args) in variants)
            {
                var response = await _client.CallToolAsync(tool, args);
                response["error"].ShouldBeNull($"{tool}: {response}");
                var result = response["result"]!.AsObject();
                result["isError"]?.GetValue<bool>().ShouldBeFalse($"{tool}: {result["content"]![0]!["text"]}");
                var structured = result["structuredContent"]!;
                tools[tool].Validate(JsonSerializer.SerializeToElement(structured)).ShouldBeEmpty(tool);
                JsonNode.DeepEquals(JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>()), structured).ShouldBeTrue(tool);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ToolsCall_StructuredAndTextContent_AreEquivalent()
    {
        await _client.InitializeAsync();

        var response = await _client.CallToolAsync("csv2zugferd_config_get", new JsonObject { ["configPath"] = TestData("config_directdebit.yml"), ["csvPath"] = TestData("test_directdebit.csv") });

        var result = response["result"]!.AsObject();
        var content = result["content"]!.AsArray().Single()!;
        content["type"]!.GetValue<string>().ShouldBe("text");
        JsonNode.DeepEquals(JsonNode.Parse(content["text"]!.GetValue<string>()), result["structuredContent"]).ShouldBeTrue();
    }

    [Test]
    public async Task ToolsCall_ConfigSchemaGet_ReturnsSchemaUnderByteLimit()
    {
        await _client.InitializeAsync();

        var response = await _client.CallToolAsync("csv2zugferd_config_schema_get", new JsonObject());

        var line = _client.StdoutLines.Last();
        Encoding.UTF8.GetByteCount(line).ShouldBeLessThan(ResponseLimit.DefaultLimitBytes);
        var schema = response["result"]!["structuredContent"]!["schema"]!.AsObject();
        schema["$schema"]!.GetValue<string>().ShouldBe("https://json-schema.org/draft/2020-12/schema");
        schema["$defs"]!.AsObject().Count.ShouldBeGreaterThan(10);
        response["result"]!["structuredContent"]!["semantics"]!.AsArray().Count.ShouldBe(ConfigSemantics.All.Count);
    }

    // --- Antwortgrenze über den Prozess ---

    [Test]
    public async Task ToolsCall_ResponseLimit_CountsTextAndStructuredContentAndEnvelope()
    {
        await _client.InitializeAsync();
        var fullResponse = await _client.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject { ["csvPath"] = TestData("test_rows.csv"), ["sampleRows"] = 2 });
        var fullBytes = Encoding.UTF8.GetByteCount(_client.StdoutLines.Last());
        fullResponse["error"].ShouldBeNull();

        using var limited = new McpStdioClient(new Dictionary<string, string> { [ResponseLimit.LimitEnvironmentVariable] = "1500" });
        await limited.InitializeAsync();
        var response = await limited.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject { ["csvPath"] = TestData("test_rows.csv"), ["sampleRows"] = 2 });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().ShouldBeTrue();
        result["content"]![0]!["text"]!.GetValue<string>().ShouldStartWith(ErrorCodes.ResponseTooLarge);
        result["content"]![0]!["text"]!.GetValue<string>().ShouldContain("sampleRows");
        result["_meta"]!["limitBytes"]!.GetValue<int>().ShouldBe(1500);
        // Gemessen wird die ganze Nachricht: derselbe Aufruf (gleiche Request-Id) hat ohne Grenze genau diese Länge auf stdout
        result["_meta"]!["responseBytes"]!.GetValue<int>().ShouldBe(fullBytes);
        fullBytes.ShouldBeGreaterThan(1500);

        var small = await limited.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject { ["csvPath"] = TestData("test_empty.csv") });
        small["result"]!["isError"].ShouldBeNull();
    }

    // --- Ressourcen ---

    [Test]
    public async Task ResourcesRead_UnknownUri_ReturnsError()
    {
        await _client.InitializeAsync();

        var response = await _client.RequestAsync("resources/read", new JsonObject { ["uri"] = "csv2zugferd://docs/nope" });

        response["result"].ShouldBeNull();
        response["error"]!["code"]!.GetValue<int>().ShouldBeOneOf(-32002, -32602);
        response["error"]!["message"]!.GetValue<string>().ShouldContain("csv2zugferd://docs/nope");
    }

    // --- Fehlerdaten-Filter (ohne Prozess, da output_schema_invalid von außen nicht auslösbar ist) ---

    [Test]
    public void ErrorDataFilter_CodedMessage_SetsErrorDataCode()
    {
        var error = new JsonRpcError
        {
            Id = new RequestId(7),
            Error = new JsonRpcErrorDetail { Code = -32603, Message = ErrorCodes.OutputSchemaInvalid + "\nDie Antwort verletzt das Schema" },
        };

        ErrorDataFilter.Enrich(error).ShouldBeTrue();

        error.Error.Data.ShouldNotBeNull();
        JsonSerializer.SerializeToElement(error.Error.Data).GetProperty("code").GetString().ShouldBe(ErrorCodes.OutputSchemaInvalid);
        var json = JsonSerializer.Serialize<JsonRpcMessage>(error, ModelContextProtocol.McpJsonUtilities.DefaultOptions);
        json.ShouldContain("\"data\":{\"code\":\"output_schema_invalid\"}");
    }

    [Test]
    public void ErrorDataFilter_PlainMessage_LeavesErrorUntouched()
    {
        var error = new JsonRpcError
        {
            Id = new RequestId(7),
            Error = new JsonRpcErrorDetail { Code = -32602, Message = "Unbekanntes Werkzeug: 'x'" },
        };

        ErrorDataFilter.Enrich(error).ShouldBeFalse();

        error.Error.Data.ShouldBeNull();
    }
}
