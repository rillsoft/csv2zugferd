using System.Text.Json;
using System.Text.Json.Nodes;
using Csv2Zugferd.Mcp;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests.Mcp;

/// <summary>Handler-Tests ohne Prozess: Werkzeuge über den gemeinsamen Wrapper <see cref="ToolRegistry.CallToolAsync(string, JsonElement, RequestId, CancellationToken)"/>.</summary>
[TestFixture]
public class InvoiceToolsTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "csv2zugferd-tests-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        ResponseLimit.LimitBytes = ResponseLimit.DefaultLimitBytes;
    }

    [TearDown]
    public void TearDown()
    {
        ResponseLimit.LimitBytes = ResponseLimit.DefaultLimitBytes;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- Helfer ---

    private static string TestData(string file) => TestHelper.GetTestDataPath(file);

    private static string ReadTestYaml(string file) => File.ReadAllText(TestData(file));

    private static async Task<CallToolResult> CallAsync(string tool, object arguments)
    {
        var element = JsonSerializer.SerializeToElement(arguments);
        return await ToolRegistry.CallToolAsync(tool, element, new RequestId(1), CancellationToken.None);
    }

    private static async Task<JsonObject> CallOkAsync(string tool, object arguments)
    {
        var result = await CallAsync(tool, arguments);
        result.IsError.ShouldNotBe(true, ((TextContentBlock)result.Content[0]).Text);
        result.StructuredContent.ShouldNotBeNull();
        return JsonNode.Parse(result.StructuredContent.Value.GetRawText())!.AsObject();
    }

    private static async Task<string> CallErrorAsync(string tool, object arguments)
    {
        var result = await CallAsync(tool, arguments);
        result.IsError.ShouldBe(true);
        var text = ((TextContentBlock)result.Content[0]).Text;
        var code = text.Split('\n')[0];
        result.Meta!["errorCode"]!.GetValue<string>().ShouldBe(code);
        ErrorCodes.All.ShouldContain(code);
        return code;
    }

    private static List<string> Strings(JsonNode? node) => node!.AsArray().Select(n => n!.GetValue<string>()).ToList();

    // --- csv_header_get ---

    [Test]
    public async Task CsvHeaderGet_SampleRowsDefault_ReturnsNoRowData()
    {
        var result = await CallOkAsync(InvoiceTools.CsvHeaderGet, new { csvPath = TestData("test_rows.csv") });

        result["columnCount"]!.GetValue<int>().ShouldBe(34);
        result["rowCount"]!.GetValue<int>().ShouldBe(2);
        result["sampleRows"]!.AsArray().ShouldBeEmpty();
        result.ToJsonString().ShouldNotContain("Kunden Mitte AG");
    }

    [Test]
    public async Task CsvHeaderGet_SampleRows2_ReturnsTwoRows()
    {
        var result = await CallOkAsync(InvoiceTools.CsvHeaderGet, new { csvPath = TestData("test_rows.csv"), sampleRows = 2 });

        var rows = result["sampleRows"]!.AsArray();
        rows.Count.ShouldBe(2);
        Strings(rows[0])[0].ShouldBe("471102");
        Strings(rows[0]).Count.ShouldBe(34);
    }

    // --- config_schema_get ---

    [Test]
    public async Task ConfigSchemaGet_CoversEveryPublicModelProperty()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });
        var schema = result["schema"]!.AsObject();

        var schemaPaths = new List<string>();
        CollectSchemaPaths(schema, string.Empty, schemaPaths);

        var modelPaths = ConfigSchemaBuilder.DescribedPaths();
        modelPaths.Count.ShouldBe(114 - 5 - 2 - 2); // ohne FieldMapping/FieldRule/FieldRuleCondition-Teilschluessel (in $defs)
        var expected = modelPaths.Concat(YamlConfigLoader.EffectiveFixedFieldKeys.Select(k => "mapping.lineItems.fixedFields." + k)).ToList();
        foreach (var path in expected)
            schemaPaths.ShouldContain(path);
        foreach (var path in schemaPaths)
            expected.ShouldContain(path);
    }

    [Test]
    public async Task ConfigSchemaGet_EveryPropertyHasNonEmptyDescription()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });

        var missing = new List<string>();
        CheckDescriptions(result["schema"]!.AsObject(), "schema", missing);
        missing.ShouldBeEmpty();
    }

    [Test]
    public async Task ConfigSchemaGet_EnumsEqualConfigValues()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });
        var defs = result["schema"]!["$defs"]!.AsObject();

        foreach (var set in ConfigValues.All)
        {
            var def = defs[ConfigSchemaBuilder.ValueSetDefinitionName(set)].ShouldNotBeNull(set.Name).AsObject();
            var canonical = Strings(def["anyOf"]![0]!["enum"]);
            canonical.ShouldBe(set.Canonical, ignoreOrder: false, set.Name);
        }
    }

    [Test]
    public async Task ConfigSchemaGet_FieldSpecificValues_MatchSemanticCatalog()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });
        var schema = result["schema"]!.AsObject();

        schema["properties"]!["mapping"]!["properties"]!["invoice"]!["properties"]!["currency"]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/currencyFieldMapping");
        schema["properties"]!["mapping"]!["properties"]!["lineItems"]!["properties"]!["mode"]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/lineItemsModeValue");
        schema["properties"]!["zugferd"]!["properties"]!["profile"]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/profileValue");
        schema["properties"]!["mapping"]!["properties"]!["buyer"]!["properties"]!["electronicAddress"]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/electronicAddressMapping");

        var semantics = result["semantics"]!.AsArray();
        var currency = semantics.Single(s => s!["path"]!.GetValue<string>() == "mapping.invoice.currency")!;
        currency["valueSet"]!.GetValue<string>().ShouldBe("currencyValue");
        currency["invalidFallback"]!.GetValue<string>().ShouldBe("EUR");
        Strings(currency["sources"]).ShouldBe(["value", "column", "rules", "default"]);
        var ignored = semantics.Single(s => s!["path"]!.GetValue<string>() == "mapping.lineItems.columnsFields")!;
        ignored["status"]!.GetValue<string>().ShouldBe("ignored");
        ignored["valueSet"].ShouldBeNull();
    }

    [Test]
    public async Task ConfigSchemaGet_FixedFields_AllowsKnownKeysAndRejectsTypo()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });
        var validator = new JsonSchemaValidator(JsonSerializer.SerializeToElement(result["schema"]));

        var valid = JsonSerializer.SerializeToElement(new { mapping = new { lineItems = new { mode = "columns", fixedFields = new { unitCode = new { value = "C62" } } } } });
        validator.Validate(valid).ShouldBeEmpty();

        var typo = JsonSerializer.SerializeToElement(new { mapping = new { lineItems = new { mode = "columns", fixedFields = new { unitcode = new { value = "C62" } } } } });
        validator.Validate(typo).ShouldHaveSingleItem().Path.ShouldBe("mapping.lineItems.fixedFields.unitcode");
    }

    [Test]
    public async Task ConfigSchemaGet_ColumnsFields_IsExplicitlyIgnored()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });
        var node = result["schema"]!["properties"]!["mapping"]!["properties"]!["lineItems"]!["properties"]!["columnsFields"]!.AsObject();

        node["deprecated"]!.GetValue<bool>().ShouldBeTrue();
        node["description"]!.GetValue<string>().ShouldContain("IGNORED");
        node["patternProperties"].ShouldNotBeNull();
        node["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
    }

    [TestCase("config_rows.yml")]
    [TestCase("config_columns_suffix.yml")]
    [TestCase("config_columns_prefix.yml")]
    [TestCase("config_creditnote.yml")]
    [TestCase("config_directdebit.yml")]
    public async Task ConfigSchemaGet_AllValidTestdataConfigsValidateAgainstSchema(string configFile)
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { });
        var validator = new JsonSchemaValidator(JsonSerializer.SerializeToElement(result["schema"]));

        var json = YamlToJson(ReadTestYaml(configFile));

        validator.Validate(json).ShouldBeEmpty(configFile);
    }

    [Test]
    public async Task ConfigSchemaGet_Section_ReturnsSubtreeWithReferencedDefs()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { section = "mapping.lineItems" });

        result["section"]!.GetValue<string>().ShouldBe("mapping.lineItems");
        var schema = result["schema"]!.AsObject();
        schema["$schema"]!.GetValue<string>().ShouldBe("https://json-schema.org/draft/2020-12/schema");
        schema["properties"]!["mode"].ShouldNotBeNull();
        schema["properties"]!["fixedFields"]!["properties"]!["unitCode"].ShouldNotBeNull();
        var defs = schema["$defs"]!.AsObject();
        defs["lineItemsModeValue"].ShouldNotBeNull();
        defs["unitCodeFieldMapping"].ShouldNotBeNull();
        defs["unitCodeValue"].ShouldNotBeNull();
        defs["fieldRule"].ShouldNotBeNull();
        defs["paymentMeansTypeValue"].ShouldBeNull();
        defs["currencyValue"].ShouldBeNull();

        var paths = result["semantics"]!.AsArray().Select(s => s!["path"]!.GetValue<string>()).ToList();
        paths.ShouldContain("mapping.lineItems");
        paths.ShouldContain("mapping.lineItems.fields.name");
        paths.ShouldContain("fieldMapping.value");
        paths.ShouldNotContain("mapping.invoice.currency");
        paths.ShouldNotContain("zugferd.profile");

        var leaf = await CallOkAsync(InvoiceTools.ConfigSchemaGet, new { section = "zugferd.profile" });
        leaf["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/$defs/profileValue");
        leaf["schema"]!["$defs"]!["profileValue"].ShouldNotBeNull();
        leaf["semantics"]!.AsArray().Count.ShouldBe(1);
    }

    [Test]
    public void ConfigSchemaGet_UnknownSection_IsProtocolError()
    {
        var element = JsonSerializer.SerializeToElement(new { section = "mapping.nope" });

        var exception = Should.Throw<ModelContextProtocol.McpProtocolException>(() => ToolRegistry.CallToolAsync(InvoiceTools.ConfigSchemaGet, element, new RequestId(1), CancellationToken.None));

        exception.ErrorCode.ShouldBe(ModelContextProtocol.McpErrorCode.InvalidParams);
        exception.Message.ShouldContain("section");
    }

    [Test]
    public async Task ConfigSchemaGet_SectionResponse_IsCompact()
    {
        var full = await CallAsync(InvoiceTools.ConfigSchemaGet, new { });
        var section = await CallAsync(InvoiceTools.ConfigSchemaGet, new { section = "mapping.lineItems" });

        var fullBytes = ResponseLimit.MeasureResponseBytes(new RequestId(1), full);
        var sectionBytes = ResponseLimit.MeasureResponseBytes(new RequestId(1), section);
        TestContext.Out.WriteLine($"full {fullBytes} Byte, section {sectionBytes} Byte");
        sectionBytes.ShouldBeLessThan(fullBytes / 2);
    }

    [Test]
    public async Task ConfigSchemaGet_SchemaIsUnderByteLimitAndDeclaresDraft()
    {
        var result = await CallAsync(InvoiceTools.ConfigSchemaGet, new { });

        result.IsError.ShouldNotBe(true);
        var bytes = ResponseLimit.MeasureResponseBytes(new RequestId(1), result);
        bytes.ShouldBeLessThan(ResponseLimit.DefaultLimitBytes);
        TestContext.Out.WriteLine($"config_schema_get: {bytes} Byte");
        var schema = result.StructuredContent!.Value.GetProperty("schema");
        schema.GetProperty("$schema").GetString().ShouldBe("https://json-schema.org/draft/2020-12/schema");
        schema.TryGetProperty("$defs", out _).ShouldBeTrue();
    }

    // --- config_get ---

    [Test]
    public async Task ConfigGet_Yaml_ReturnsMappedFieldsAndModes()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configYaml = ReadTestYaml("config_rows.yml") });

        result["source"]!.GetValue<string>().ShouldBe("yaml");
        result["lineItemsMode"]!.GetValue<string>().ShouldBe("rows");
        result["totalsMode"]!.GetValue<string>().ShouldBe("auto");
        result["zugferd"]!["profile"]!.GetValue<string>().ShouldBe("Extended");
        result["csv"]!["separator"]!.GetValue<string>().ShouldBe(";");
        var fields = result["mappedFields"]!.AsArray();
        var invoiceNumber = fields.Single(f => f!["field"]!.GetValue<string>() == "mapping.invoice.invoiceNumber")!;
        Strings(invoiceNumber["sources"]).ShouldBe(["column"]);
        fields.Single(f => f!["field"]!.GetValue<string>() == "mapping.seller.taxRegistrations[0]")!["sources"]!.AsArray().Count.ShouldBe(1);
        result["unknownKeys"]!.AsArray().ShouldBeEmpty();
        result["invalidValues"]!.AsArray().ShouldBeEmpty();
        result["columnsMissing"].ShouldBeNull();
    }

    [Test]
    public async Task ConfigGet_WithCsv_ReportsMissingAndUnusedColumns()
    {
        var yaml = ReadTestYaml("config_rows.yml").Replace("column: \"Bemerkung\"", "column: \"Bemerkungen\"");

        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configYaml = yaml, csvPath = TestData("test_rows.csv") });

        Strings(result["columnsMissing"]).ShouldBe(["Bemerkungen"]);
        Strings(result["columnsUnused"]).ShouldBe(["Bemerkung"]);
        Strings(result["columnsReferenced"]).ShouldContain("Rechnungsnummer");
    }

    [Test]
    public async Task ConfigGet_ColumnsPrefixAndSuffix_ResolvesConcreteColumnNames()
    {
        var suffix = await CallOkAsync(InvoiceTools.ConfigGet, new { configPath = TestData("config_columns_suffix.yml"), csvPath = TestData("test_columns_suffix.csv") });
        var prefix = await CallOkAsync(InvoiceTools.ConfigGet, new { configPath = TestData("config_columns_prefix.yml"), csvPath = TestData("test_columns_prefix.csv") });

        Strings(suffix["columnsReferenced"]).ShouldContain("Artikel_001");
        Strings(suffix["columnsReferenced"]).ShouldContain("Artikel_002");
        Strings(suffix["columnsMissing"]).ShouldBeEmpty();
        Strings(prefix["columnsReferenced"]).ShouldContain("Pos1_Artikel");
        Strings(prefix["columnsReferenced"]).ShouldContain("Pos2_Menge");
        Strings(prefix["columnsMissing"]).ShouldBeEmpty();
        // RabattGrund wird vom ignorierten Feld discountReason referenziert und zaehlt deshalb als unbenutzt
        Strings(prefix["columnsUnused"]).ShouldBe(["Pos1_RabattGrund", "Pos2_RabattGrund"]);
    }

    [Test]
    public async Task ConfigGet_RulesAndTaxRegistrations_IncludesColumnReferences()
    {
        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configPath = TestData("config_directdebit.yml"), csvPath = TestData("test_directdebit.csv") });

        var referenced = Strings(result["columnsReferenced"]);
        referenced.ShouldContain("Zahlart");                 // rules[].when.column
        referenced.ShouldContain("Verkäufer_Steuernummer");  // taxRegistrations[].column
        referenced.ShouldContain("Kunden_IBAN");             // directDebit
        Strings(result["columnsMissing"]).ShouldBeEmpty();
        Strings(result["columnsUnused"]).ShouldBeEmpty();
    }

    [Test]
    public async Task ConfigGet_DirectOrIndexedColumn_DoesNotReportFalseMissing()
    {
        const string yaml = """
            csv:
              separator: ","
              decimalSeparator: "."
            mapping:
              invoice:
                invoiceNumber:
                  column: "INVOICE_NO"
              lineItems:
                mode: "columns"
                columnPattern:
                  style: "suffix"
                  separator: "_"
                  startIndex: 0
                fields:
                  name:
                    column: "PRODUCT_NAME"
                  netUnitPrice:
                    column: "PRODUCT_PRICE"
                  billedQuantity:
                    column: "PRODUCT_QUANTITY"
                  taxPercent:
                    column: "INVOICE_TAX"
            """;
        var csvPath = Path.Combine(_tempDir, "direct.csv");
        File.WriteAllText(csvPath, "INVOICE_NO,INVOICE_TAX,PRODUCT_NAME_0,PRODUCT_PRICE_0,PRODUCT_QUANTITY_0,PRODUCT_NAME_1,PRODUCT_PRICE_1,PRODUCT_QUANTITY_1\n1,19,A,1,1,B,2,2\n");

        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configYaml = yaml, csvPath });

        Strings(result["columnsMissing"]).ShouldBeEmpty();
        Strings(result["columnsUnused"]).ShouldBeEmpty();
        var referenced = Strings(result["columnsReferenced"]);
        referenced.ShouldContain("INVOICE_TAX");
        referenced.ShouldContain("PRODUCT_NAME_1");
        referenced.ShouldContain("Beschreibung_0"); // Standardfragment ohne Konfiguration
    }

    [Test]
    public async Task ConfigGet_CaseVariantHeader_MatchesReader()
    {
        var csvPath = Path.Combine(_tempDir, "case.csv");
        var header = File.ReadLines(TestData("test_rows.csv")).First().ToUpperInvariant();
        File.WriteAllText(csvPath, header + "\n");

        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configPath = TestData("config_rows.yml"), csvPath });

        Strings(result["columnsMissing"]).ShouldBeEmpty();
    }

    [Test]
    public async Task ConfigGet_IgnoredAndConditionalFields_ReturnsDiagnostics()
    {
        var yaml = ReadTestYaml("config_rows.yml").Replace("    mode: \"rows\"", "    mode: \"rows\"\n    columnPattern:\n      style: \"suffix\"");

        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configYaml = yaml });

        var warnings = result["warnings"]!.AsArray();
        warnings.ShouldContain(w => w!["code"]!.GetValue<string>() == DiagnosticCodes.ConfigFieldIgnored && w["path"]!.GetValue<string>() == "mapping.lineItems.columnPattern");
    }

    [Test]
    public async Task ConfigGet_InvalidYaml_RejectsWithConfigInvalidYaml()
    {
        (await CallErrorAsync(InvoiceTools.ConfigGet, new { configYaml = "csv: [" })).ShouldBe(ErrorCodes.ConfigInvalidYaml);
    }

    [Test]
    public async Task ConfigGet_PathAndYaml_RejectsWithConfigSourceAmbiguous()
    {
        // Der Wrapper lehnt die Kombination schon per Schema ab (-32602); der Handler selbst prueft ebenfalls.
        var element = JsonSerializer.SerializeToElement(new { configPath = TestData("config_rows.yml"), configYaml = "csv: {}" });
        Should.Throw<ModelContextProtocol.McpProtocolException>(() => ToolRegistry.CallToolAsync(InvoiceTools.ConfigGet, element, new RequestId(1), CancellationToken.None));
        Should.Throw<Csv2ZugferdException>(() => InvoiceTools.GetConfig(element)).Code.ShouldBe(ErrorCodes.ConfigSourceAmbiguous);
        Should.Throw<Csv2ZugferdException>(() => InvoiceTools.GetConfig(JsonSerializer.SerializeToElement(new { }))).Code.ShouldBe(ErrorCodes.ConfigSourceMissing);
    }

    [Test]
    public async Task ConfigGet_MissingConfigFile_RejectsWithConfigNotFound()
    {
        (await CallErrorAsync(InvoiceTools.ConfigGet, new { configPath = Path.Combine(_tempDir, "nope.yml") })).ShouldBe(ErrorCodes.ConfigNotFound);
    }

    [Test]
    public async Task ConfigGet_MisspelledKey_ReportsUnknownKey()
    {
        var yaml = ReadTestYaml("config_rows.yml").Replace("    invoiceNumber:", "    number:");

        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configYaml = yaml });

        Strings(result["unknownKeys"]).ShouldBe(["mapping.invoice.number"]);
    }

    [Test]
    public async Task ConfigGet_UnknownProfile_ReportsInvalidValueWithAllowedList()
    {
        var yaml = ReadTestYaml("config_rows.yml").Replace("profile: \"Extended\"", "profile: \"Komfort\"").Replace("value: \"EUR\"", "value: \"Euro\"");

        var result = await CallOkAsync(InvoiceTools.ConfigGet, new { configYaml = yaml });

        var invalid = result["invalidValues"]!.AsArray();
        var profile = invalid.Single(v => v!["path"]!.GetValue<string>() == "zugferd.profile")!;
        profile["value"]!.GetValue<string>().ShouldBe("Komfort");
        profile["fallback"]!.GetValue<string>().ShouldBe("Extended");
        Strings(profile["allowed"]).ShouldContain("XRechnung");
        var currency = invalid.Single(v => v!["path"]!.GetValue<string>() == "mapping.invoice.currency.value")!;
        currency["fallback"]!.GetValue<string>().ShouldBe("EUR");
        result["zugferd"]!["profile"]!.GetValue<string>().ShouldBe("Extended");
    }

    // --- invoice_get ---

    [Test]
    public async Task InvoiceGet_RowsConfig_TotalsEqualMapperResult()
    {
        var expected = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml") });

        result["invoiceNumber"]!.GetValue<string>().ShouldBe("471102");
        result["invoiceDate"]!.GetValue<string>().ShouldBe("2013-06-05");
        result["typeCode"]!.GetValue<string>().ShouldBe("380");
        result["currency"]!.GetValue<string>().ShouldBe("EUR");
        result["rowCount"]!.GetValue<int>().ShouldBe(2);
        result["lineItemCount"]!.GetValue<int>().ShouldBe(2);
        result["totals"]!["grandTotal"]!.GetValue<decimal>().ShouldBe(expected.GrandTotalAmount!.Value);
        result["totals"]!["lineTotal"]!.GetValue<decimal>().ShouldBe(expected.LineTotalAmount!.Value);
        result["taxBreakdown"]!.AsArray().Count.ShouldBe(1);
        result["taxBreakdown"]![0]!["percent"]!.GetValue<decimal>().ShouldBe(19m);
        result["xmlBytes"]!.GetValue<long>().ShouldBeGreaterThan(1000);
        result["pdfBytes"].ShouldBeNull();
    }

    [Test]
    public async Task InvoiceGet_Default_ContainsNoPartyOrLineItemFields()
    {
        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml") });

        result["lineItems"].ShouldBeNull();
        result["parties"].ShouldBeNull();
        var json = result.ToJsonString();
        json.ShouldNotContain("Kunden Mitte AG");
        json.ShouldNotContain("Lieferant GmbH");
        json.ShouldNotContain("Test1");
    }

    [Test]
    public async Task InvoiceGet_IncludeLineItems_ReturnsLineItems()
    {
        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), includeLineItems = true, includeParties = true });

        var items = result["lineItems"]!.AsArray();
        items.Count.ShouldBe(2);
        items[0]!["name"]!.GetValue<string>().ShouldBe("Test1");
        items[0]!["unitCode"]!.GetValue<string>().ShouldBe("C62");
        result["parties"]!["seller"]!["name"]!.GetValue<string>().ShouldBe("Lieferant GmbH");
        result["parties"]!["buyer"]!["country"]!.GetValue<string>().ShouldBe("DE");
    }

    [Test]
    public async Task InvoiceGet_CreditNoteConfig_ReturnsWarningsAndTypeCode381()
    {
        var yaml = ReadTestYaml("config_creditnote.yml").Replace("    precedingInvoices:", "    precedingInvoicesX:");

        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_creditnote.csv"), configYaml = yaml });

        result["typeCode"]!.GetValue<string>().ShouldBe("381");
        result["warnings"]!.AsArray().ShouldContain(w => w!["code"]!.GetValue<string>() == DiagnosticCodes.PrecedingInvoiceMissing);
    }

    [Test]
    public async Task InvoiceGet_XmlBytes_EqualsExporterOutputLength()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        using var stream = new MemoryStream();
        InvoiceExporter.WriteXml(desc, stream, config.Zugferd, null);

        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml") });

        result["xmlBytes"]!.GetValue<long>().ShouldBe(stream.Length);
    }

    [Test]
    public async Task InvoiceGet_WithPdfTemplate_ReturnsPdfBytesAndLeavesNoTempFile()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "tmp*.tmp").Length;

        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), pdfTemplatePath = TestData("template.pdf") });

        result["pdfBytes"]!.GetValue<long>().ShouldBeGreaterThan(result["xmlBytes"]!.GetValue<long>());
        Directory.GetFiles(Path.GetTempPath(), "tmp*.tmp").Length.ShouldBe(before);
    }

    [Test]
    public async Task InvoiceGet_MissingPdfTemplate_RejectsWithPdfTemplateNotFound()
    {
        (await CallErrorAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), pdfTemplatePath = Path.Combine(_tempDir, "nope.pdf") }))
            .ShouldBe(ErrorCodes.PdfTemplateNotFound);
    }

    [Test]
    public async Task InvoiceGet_InvalidSellerIban_DiagnosticContainsCodeAndPath()
    {
        var yaml = ReadTestYaml("config_rows.yml").Replace("DE02 1203 0000 0000 2020 51", "DE02 1203 0000 0000 2020 52");

        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configYaml = yaml });

        var warning = result["warnings"]!.AsArray().Single(w => w!["code"]!.GetValue<string>() == DiagnosticCodes.InvalidIbanChecksum)!;
        warning["path"]!.GetValue<string>().ShouldBe("mapping.paymentMeans.sellerAccounts[0].iban");
    }

    [Test]
    public async Task InvoiceGet_EmptyCsv_RejectsWithCsvEmpty()
    {
        (await CallErrorAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_empty.csv"), configPath = TestData("config_rows.yml") })).ShouldBe(ErrorCodes.CsvEmpty);
    }

    [Test]
    public async Task ToolErrors_InvalidYamlAndCellValues_ReturnExpectedCodes()
    {
        (await CallErrorAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configYaml = "zugferd: [" })).ShouldBe(ErrorCodes.ConfigInvalidYaml);
        (await CallErrorAsync(InvoiceTools.InvoiceGet, new { csvPath = Path.Combine(_tempDir, "nope.csv"), configPath = TestData("config_rows.yml") })).ShouldBe(ErrorCodes.CsvFileNotFound);

        var yaml = ReadTestYaml("config_rows.yml").Replace("column: \"Rechnungsdatum\"", "value: \"gestern\"").Replace("column: \"Einzelpreis_Netto\"", "value: \"teuer\"");
        var result = await CallOkAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configYaml = yaml });
        var codes = result["warnings"]!.AsArray().Select(w => w!["code"]!.GetValue<string>()).ToList();
        codes.ShouldContain(DiagnosticCodes.InvalidDate);
        codes.ShouldContain(DiagnosticCodes.InvalidDecimal);

        var typeYaml = ReadTestYaml("config_rows.yml").Replace("    invoiceNumber:", "    typeCode:\n      value: \"999\"\n    invoiceNumber:");
        (await CallErrorAsync(InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configYaml = typeYaml })).ShouldBe(ErrorCodes.InvoiceTypeUnknown);
    }

    // --- config_file_create ---

    [Test]
    public async Task ConfigFileCreate_NewFile_WritesFileAndCliCommand()
    {
        var path = Path.Combine(_tempDir, "config.yml");
        var yaml = ReadTestYaml("config_rows.yml");

        var result = await CallOkAsync(InvoiceTools.ConfigFileCreate, new { configYaml = yaml, configPath = path, csvPath = "C:/daten/rechnung.csv" });

        File.Exists(path).ShouldBeTrue();
        var bytes = File.ReadAllBytes(path);
        bytes.Length.ShouldBe((int)result["bytes"]!.GetValue<long>());
        bytes.Take(3).ShouldNotBe([(byte)0xEF, (byte)0xBB, (byte)0xBF]);
        result["configPath"]!.GetValue<string>().ShouldBe(Path.GetFullPath(path));
        var command = result["cliCommand"]!.GetValue<string>();
        command.ShouldStartWith("csv2zugferd --csv \"C:/daten/rechnung.csv\"");
        command.ShouldContain($"--config \"{Path.GetFullPath(path)}\"");
        command.ShouldContain("<pdf-template>");
        command.ShouldContain("<output-directory>");
    }

    [Test]
    public async Task ConfigFileCreate_ExistingFile_RejectsWithFileExists()
    {
        var path = Path.Combine(_tempDir, "config.yml");
        File.WriteAllText(path, "alt");

        (await CallErrorAsync(InvoiceTools.ConfigFileCreate, new { configYaml = "csv: {}", configPath = path })).ShouldBe(ErrorCodes.FileExists);

        File.ReadAllText(path).ShouldBe("alt");
    }

    [Test]
    public async Task ConfigFileCreate_Overwrite_ReplacesFile()
    {
        var path = Path.Combine(_tempDir, "config.yml");
        File.WriteAllText(path, "alt");

        await CallOkAsync(InvoiceTools.ConfigFileCreate, new { configYaml = "csv:\n  separator: \",\"\n", configPath = path, overwrite = true });

        File.ReadAllText(path).ShouldContain("separator");
    }

    [Test]
    public async Task ConfigFileCreate_InvalidYaml_RejectsAndWritesNothing()
    {
        var path = Path.Combine(_tempDir, "config.yml");

        (await CallErrorAsync(InvoiceTools.ConfigFileCreate, new { configYaml = "csv: [", configPath = path })).ShouldBe(ErrorCodes.ConfigInvalidYaml);

        File.Exists(path).ShouldBeFalse();
    }

    [Test]
    public async Task ConfigFileCreate_MissingDirectory_RejectsWithOutputPathInvalid()
    {
        var path = Path.Combine(_tempDir, "gibt-es-nicht", "config.yml");

        (await CallErrorAsync(InvoiceTools.ConfigFileCreate, new { configYaml = "csv: {}", configPath = path })).ShouldBe(ErrorCodes.OutputPathInvalid);
    }

    // --- Wrapper ---

    [Test]
    public async Task ReadTool_OverLimit_RejectsWithResponseTooLarge()
    {
        ResponseLimit.LimitBytes = 500;

        var result = await CallAsync(InvoiceTools.CsvHeaderGet, new { csvPath = TestData("test_rows.csv"), sampleRows = 2 });

        result.IsError.ShouldBe(true);
        ((TextContentBlock)result.Content[0]).Text.ShouldStartWith(ErrorCodes.ResponseTooLarge);
        result.Meta!["responseBytes"]!.GetValue<int>().ShouldBeGreaterThan(500);
        result.Meta!["limitBytes"]!.GetValue<int>().ShouldBe(500);
        ((TextContentBlock)result.Content[0]).Text.ShouldContain("sampleRows");
    }

    [Test]
    public async Task WriteTool_OverLimit_IsNotRejected()
    {
        ResponseLimit.LimitBytes = 10;
        var path = Path.Combine(_tempDir, "config.yml");

        var result = await CallAsync(InvoiceTools.ConfigFileCreate, new { configYaml = "csv: {}", configPath = path });

        result.IsError.ShouldNotBe(true);
    }

    [Test]
    public async Task AllTools_SuccessVariants_ValidateAgainstPublishedOutputSchema()
    {
        var path = Path.Combine(_tempDir, "config.yml");
        var variants = new (string Tool, object Args)[]
        {
            (InvoiceTools.CsvHeaderGet, new { csvPath = TestData("test_rows.csv") }),
            (InvoiceTools.CsvHeaderGet, new { csvPath = TestData("test_columns_suffix.csv"), sampleRows = 3, separator = ";" }),
            (InvoiceTools.ConfigSchemaGet, new { }),
            (InvoiceTools.ConfigSchemaGet, new { section = "mapping.paymentMeans" }),
            (InvoiceTools.ConfigSchemaGet, new { section = "csv.separator" }),
            (InvoiceTools.ConfigGet, new { configPath = TestData("config_rows.yml") }),
            (InvoiceTools.ConfigGet, new { configYaml = ReadTestYaml("config_columns_prefix.yml"), csvPath = TestData("test_columns_prefix.csv") }),
            (InvoiceTools.ConfigGet, new { configYaml = ReadTestYaml("config_rows.yml").Replace("profile: \"Extended\"", "profile: \"Komfort\"") }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml") }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), includeLineItems = true }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), includeParties = true }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), includeLineItems = true, includeParties = true }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_rows.csv"), configPath = TestData("config_rows.yml"), pdfTemplatePath = TestData("template.pdf") }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_directdebit.csv"), configYaml = ReadTestYaml("config_directdebit.yml").Replace("version: \"2.3\"", "version: \"9\"") }),
            (InvoiceTools.InvoiceGet, new { csvPath = TestData("test_creditnote.csv"), configPath = TestData("config_creditnote.yml"), includeParties = true }),
            (InvoiceTools.ConfigFileCreate, new { configYaml = ReadTestYaml("config_rows.yml"), configPath = path }),
            (InvoiceTools.ConfigFileCreate, new { configYaml = ReadTestYaml("config_rows.yml"), configPath = path, overwrite = true, csvPath = "x.csv" }),
        };

        foreach (var (tool, args) in variants)
        {
            var result = await CallAsync(tool, args);
            result.IsError.ShouldNotBe(true, $"{tool}: {((TextContentBlock)result.Content[0]).Text}");
            var definition = ToolRegistry.Find(tool)!;
            definition.OutputValidator.Validate(result.StructuredContent!.Value).ShouldBeEmpty(tool);
            JsonNode.DeepEquals(JsonNode.Parse(((TextContentBlock)result.Content[0]).Text), JsonNode.Parse(result.StructuredContent.Value.GetRawText())).ShouldBeTrue(tool);
        }
    }

    [Test]
    public void OutputValidator_InvalidResult_ReturnsMinus32603WithoutPayload()
    {
        var validator = ToolRegistry.Find(InvoiceTools.CsvHeaderGet)!.OutputValidator;
        var invalid = JsonSerializer.SerializeToElement(new { csvPath = "x", columns = new[] { 1 } });

        var exception = Should.Throw<ModelContextProtocol.McpProtocolException>(() => OutputValidator.EnsureValid(InvoiceTools.CsvHeaderGet, validator, invalid));

        exception.ErrorCode.ShouldBe(ModelContextProtocol.McpErrorCode.InternalError);
        exception.Message.ShouldStartWith(ErrorCodes.OutputSchemaInvalid);
        exception.Message.ShouldContain("columns[0]");
    }

    [Test]
    public void ToolRegistry_IsSortedAndClosed()
    {
        var names = ToolRegistry.Tools.Select(t => t.Name).ToList();
        names.ShouldBe(names.OrderBy(n => n, StringComparer.Ordinal).ToList());
        names.Count.ShouldBe(5);
        foreach (var tool in ToolRegistry.Tools)
        {
            tool.Title.ShouldNotBeNullOrWhiteSpace();
            tool.Description.ShouldNotBeNullOrWhiteSpace();
            tool.InputSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse(tool.Name);
            tool.OutputSchema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse(tool.Name);
            Should.NotThrow(() => tool.InputValidator, tool.Name);
            Should.NotThrow(() => tool.OutputValidator, tool.Name);
        }
    }

    // --- Ressourcen ---

    [Test]
    public void ResourcesList_ContainsThreeResources()
    {
        var resources = DocResources.List().Resources;

        resources.Count.ShouldBe(3);
        resources.Select(r => r.Uri).ShouldBe(
        [
            "csv2zugferd://docs/config-reference",
            "csv2zugferd://examples/config-columns-suffix",
            "csv2zugferd://examples/config-rows",
        ]);
    }

    [Test]
    public void ResourcesRead_ConfigReference_StartsWithReadmeHeading()
    {
        var result = DocResources.Read("csv2zugferd://docs/config-reference");

        var text = ((TextResourceContents)result.Contents[0]).Text;
        text.TrimStart('\uFEFF').ShouldStartWith("# csv2zugferd Dokumentation");
        ((TextResourceContents)DocResources.Read("csv2zugferd://examples/config-rows").Contents[0]).Text.ShouldContain("mode: \"rows\"");
    }

    // --- Helfer fuer Schema-Durchlauf ---

    /// <summary>YAML nach JSON mit typisierten Skalaren: unquotierte true/false und Ganzzahlen wie der Deserializer, alles andere Text.</summary>
    private static JsonElement YamlToJson(string yaml)
    {
        var stream = new YamlDotNet.RepresentationModel.YamlStream();
        stream.Load(new StringReader(yaml));
        return JsonSerializer.SerializeToElement(Convert(stream.Documents[0].RootNode));

        static JsonNode? Convert(YamlDotNet.RepresentationModel.YamlNode node) => node switch
        {
            YamlDotNet.RepresentationModel.YamlMappingNode mapping => new JsonObject(mapping.Children.Select(kv =>
                new KeyValuePair<string, JsonNode?>(((YamlDotNet.RepresentationModel.YamlScalarNode)kv.Key).Value!, Convert(kv.Value)))),
            YamlDotNet.RepresentationModel.YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(Convert).ToArray()),
            YamlDotNet.RepresentationModel.YamlScalarNode scalar when scalar.Style == YamlDotNet.Core.ScalarStyle.Plain && bool.TryParse(scalar.Value, out var b) => b,
            YamlDotNet.RepresentationModel.YamlScalarNode scalar when scalar.Style == YamlDotNet.Core.ScalarStyle.Plain && int.TryParse(scalar.Value, out var i) => i,
            YamlDotNet.RepresentationModel.YamlScalarNode scalar => scalar.Value,
            _ => throw new NotSupportedException(node.GetType().Name),
        };
    }

    private static void CollectSchemaPaths(JsonObject node, string prefix, List<string> paths)
    {
        if (node["properties"] is not JsonObject properties) return;
        foreach (var (name, child) in properties)
        {
            var path = prefix.Length == 0 ? name : $"{prefix}.{name}";
            paths.Add(path);
            var childObject = child!.AsObject();
            if (childObject["items"] is JsonObject items && items["properties"] != null)
                CollectSchemaPaths(items, path + "[]", paths);
            else if (childObject["$ref"] == null && childObject["patternProperties"] == null)
                CollectSchemaPaths(childObject, path, paths);
        }
    }

    private static void CheckDescriptions(JsonObject node, string path, List<string> missing)
    {
        if (node["$ref"] == null && node["anyOf"] == null && string.IsNullOrWhiteSpace(node["description"]?.GetValue<string>()))
            missing.Add(path);
        foreach (var key in new[] { "properties", "$defs", "patternProperties" })
        {
            if (node[key] is JsonObject container)
            {
                foreach (var (name, child) in container)
                    CheckDescriptions(child!.AsObject(), $"{path}.{name}", missing);
            }
        }
        if (node["items"] is JsonObject items)
            CheckDescriptions(items, path + "[]", missing);
    }
}
