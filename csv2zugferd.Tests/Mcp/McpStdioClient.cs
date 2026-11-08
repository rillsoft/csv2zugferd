using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Csv2Zugferd.Tests.Mcp;

/// <summary>
/// Minimaler JSON-RPC-Client über stdio gegen den echten Prozess <c>csv2zugferd mcp</c>.
/// Sammelt alle stdout-Zeilen, damit Tests prüfen können, dass dort ausschließlich JSON-RPC steht.
/// </summary>
public sealed class McpStdioClient : IDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly List<string> _stdoutLines = [];
    private readonly StringBuilder _stderr = new();
    private readonly object _lock = new();
    private int _nextId = 1;

    public IReadOnlyList<string> StdoutLines
    {
        get
        {
            lock (_lock)
                return _stdoutLines.ToList();
        }
    }

    public string Stderr
    {
        get
        {
            lock (_lock)
                return _stderr.ToString();
        }
    }

    public McpStdioClient(params string[] extraArguments)
        : this(null, extraArguments)
    {
    }

    public McpStdioClient(IReadOnlyDictionary<string, string>? environment, params string[] extraArguments)
    {
        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var exe = Path.Combine(testDirectory, OperatingSystem.IsWindows() ? "csv2zugferd.exe" : "csv2zugferd");
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = testDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        if (File.Exists(exe))
        {
            startInfo.FileName = exe;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(Path.Combine(testDirectory, "csv2zugferd.dll"));
        }
        startInfo.ArgumentList.Add("mcp");
        foreach (var argument in extraArguments)
            startInfo.ArgumentList.Add(argument);
        if (environment != null)
        {
            foreach (var (key, value) in environment)
                startInfo.Environment[key] = value;
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Prozess konnte nicht gestartet werden");
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_lock)
                _stderr.AppendLine(e.Data);
        };
        _process.BeginErrorReadLine();
    }

    public static string TestDataPath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", fileName);

    public async Task<JsonObject> InitializeAsync()
    {
        var response = await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "csv2zugferd.Tests", ["version"] = "1.0" },
        });
        await NotifyAsync("notifications/initialized");
        return response;
    }

    public Task<JsonObject> ListToolsAsync() => RequestAsync("tools/list", new JsonObject());

    public Task<JsonObject> CallToolAsync(string name, JsonObject arguments)
        => RequestAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });

    /// <summary>Sendet einen Request und liefert die vollständige Antwortnachricht (mit <c>result</c> oder <c>error</c>).</summary>
    public async Task<JsonObject> RequestAsync(string method, JsonNode? parameters)
    {
        var id = _nextId++;
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters != null)
            request["params"] = parameters;

        await SendAsync(request);

        while (true)
        {
            var line = await ReadLineAsync();
            var message = JsonNode.Parse(line)!.AsObject();
            if (message["id"] is { } messageId && messageId.GetValueKind() == JsonValueKind.Number && messageId.GetValue<int>() == id)
                return message;
        }
    }

    public async Task SendRawAsync(string line)
    {
        await _process.StandardInput.WriteAsync(line + "\n");
        await _process.StandardInput.FlushAsync();
    }

    public Task NotifyAsync(string method)
        => SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method });

    /// <summary>Liest die nächste stdout-Zeile (mit Zeitlimit) und protokolliert sie.</summary>
    public async Task<string> ReadLineAsync()
    {
        var line = await _process.StandardOutput.ReadLineAsync().WaitAsync(ResponseTimeout)
            ?? throw new InvalidOperationException($"Server hat stdout geschlossen. stderr:{Environment.NewLine}{Stderr}");
        lock (_lock)
            _stdoutLines.Add(line);
        return line;
    }

    /// <summary>Schließt stdin und wartet auf das Prozessende; liefert den Exit-Code.</summary>
    public int Shutdown()
    {
        if (!_process.HasExited)
        {
            _process.StandardInput.Close();
            if (!_process.WaitForExit(10_000))
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5_000);
            }
        }

        // Restliche stdout-Zeilen einsammeln
        string? rest;
        while ((rest = _process.StandardOutput.ReadLine()) != null)
        {
            lock (_lock)
                _stdoutLines.Add(rest);
        }

        return _process.ExitCode;
    }

    private async Task SendAsync(JsonObject message)
    {
        await _process.StandardInput.WriteAsync(message.ToJsonString() + "\n");
        await _process.StandardInput.FlushAsync();
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5_000);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }
}
