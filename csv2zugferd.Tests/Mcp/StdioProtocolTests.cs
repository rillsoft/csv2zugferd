using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests.Mcp;

/// <summary>Protokolltests gegen den gestarteten Prozess <c>csv2zugferd mcp</c> (stdio), nicht gegen einen In-Memory-Transport.</summary>
[TestFixture]
public class StdioProtocolTests
{
    private McpStdioClient _client = null!;

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

    [Test]
    public async Task Initialize_ReturnsServerInfoAndCapabilities()
    {
        var response = await _client.InitializeAsync();

        response["error"].ShouldBeNull();
        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().ShouldBe("2025-06-18");
        result["serverInfo"]!["name"]!.GetValue<string>().ShouldBe("csv2zugferd");
        result["serverInfo"]!["version"]!.GetValue<string>().ShouldMatch(@"^\d+\.\d+\.\d+$");
        result["capabilities"]!["tools"].ShouldNotBeNull();
        result["instructions"]!.GetValue<string>().ShouldContain("csv2zugferd_csv_header_get");
    }

    [Test]
    public async Task ToolsList_ContainsCsvHeaderGet_WithSchemaAndAnnotations()
    {
        await _client.InitializeAsync();

        var response = await _client.ListToolsAsync();

        response["error"].ShouldBeNull();
        var tools = response["result"]!["tools"]!.AsArray();
        var tool = tools.Single(t => t!["name"]!.GetValue<string>() == "csv2zugferd_csv_header_get")!.AsObject();

        tool["title"]!.GetValue<string>().ShouldBe("Read CSV header");
        tool["description"]!.GetValue<string>().ShouldContain("sampleRows");

        var inputSchema = tool["inputSchema"]!.AsObject();
        inputSchema["type"]!.GetValue<string>().ShouldBe("object");
        inputSchema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
        inputSchema["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["csvPath"]);
        inputSchema["properties"]!["sampleRows"]!["maximum"]!.GetValue<int>().ShouldBe(3);
        inputSchema["properties"]!["separator"]!["default"]!.GetValue<string>().ShouldBe(";");

        var outputSchema = tool["outputSchema"]!.AsObject();
        outputSchema["properties"]!["columns"]!["type"]!.GetValue<string>().ShouldBe("array");
        outputSchema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();

        var annotations = tool["annotations"]!.AsObject();
        annotations["readOnlyHint"]!.GetValue<bool>().ShouldBeTrue();
        annotations["destructiveHint"]!.GetValue<bool>().ShouldBeFalse();
        annotations["idempotentHint"]!.GetValue<bool>().ShouldBeTrue();
        annotations["openWorldHint"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Test]
    public async Task ToolsCall_CsvHeaderGet_RowsCsv_ReturnsColumns()
    {
        await _client.InitializeAsync();

        var response = await _client.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject
        {
            ["csvPath"] = McpStdioClient.TestDataPath("test_rows.csv"),
        });

        response["error"].ShouldBeNull();
        var result = response["result"]!.AsObject();
        result["isError"]?.GetValue<bool>().ShouldBeFalse();

        var structured = result["structuredContent"]!.AsObject();
        var columns = structured["columns"]!.AsArray().Select(c => c!.GetValue<string>()).ToList();
        columns.Count.ShouldBe(34);
        columns[0].ShouldBe("Rechnungsnummer");
        columns.ShouldContain("Käufer_Name");
        structured["columnCount"]!.GetValue<int>().ShouldBe(34);
        structured["rowCount"]!.GetValue<int>().ShouldBe(2);
        structured["separator"]!.GetValue<string>().ShouldBe(";");
        structured["encoding"]!.GetValue<string>().ShouldBe("UTF-8");
        structured["sampleRows"]!.AsArray().Count.ShouldBe(0);

        // Textinhalt ist dieselbe JSON wie structuredContent
        var text = result["content"]!.AsArray().Single()!["text"]!.GetValue<string>();
        JsonNode.DeepEquals(JsonNode.Parse(text), structured).ShouldBeTrue();
    }

    [Test]
    public async Task ResourcesList_ContainsThreeResources()
    {
        var init = await _client.InitializeAsync();
        init["result"]!["capabilities"]!["resources"].ShouldNotBeNull();

        var response = await _client.RequestAsync("resources/list", new JsonObject());

        response["error"].ShouldBeNull();
        var resources = response["result"]!["resources"]!.AsArray();
        resources.Count.ShouldBe(3);
        resources.Select(r => r!["uri"]!.GetValue<string>()).ShouldContain("csv2zugferd://docs/config-reference");
        resources.All(r => r!["mimeType"] != null && r["name"] != null && r["description"] != null).ShouldBeTrue();
    }

    [Test]
    public async Task ResourcesRead_ConfigReference_StartsWithReadmeHeading()
    {
        await _client.InitializeAsync();

        var response = await _client.RequestAsync("resources/read", new JsonObject { ["uri"] = "csv2zugferd://docs/config-reference" });

        response["error"].ShouldBeNull();
        var contents = response["result"]!["contents"]!.AsArray().Single()!;
        contents["mimeType"]!.GetValue<string>().ShouldBe("text/markdown");
        contents["text"]!.GetValue<string>().TrimStart((char)0xFEFF).ShouldStartWith("# csv2zugferd Dokumentation");
    }

    [Test]
    public async Task Stdout_ContainsOnlyJsonRpcLines()
    {
        await _client.InitializeAsync();
        await _client.ListToolsAsync();
        await _client.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject
        {
            ["csvPath"] = McpStdioClient.TestDataPath("test_rows.csv"),
            ["sampleRows"] = 1,
        });
        // Fachliche Ablehnung erzeugt eine Warnung im Log; auch sie darf nicht auf stdout landen.
        await _client.CallToolAsync("csv2zugferd_csv_header_get", new JsonObject
        {
            ["csvPath"] = McpStdioClient.TestDataPath("gibt_es_nicht.csv"),
        });

        var exitCode = _client.Shutdown();
        var lines = _client.StdoutLines;

        exitCode.ShouldBe(0);
        lines.Count.ShouldBe(4);
        foreach (var line in lines)
        {
            var message = Should.NotThrow(() => JsonNode.Parse(line)!.AsObject());
            message["jsonrpc"]!.GetValue<string>().ShouldBe("2.0");
            message["id"]!.GetValueKind().ShouldBe(JsonValueKind.Number);
        }
    }
}
