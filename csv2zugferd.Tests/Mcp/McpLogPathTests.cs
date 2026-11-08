using Csv2Zugferd.Mcp;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests.Mcp;

/// <summary>Das Logverzeichnis des MCP-Servers hängt nicht vom Arbeitsverzeichnis des Clients ab.</summary>
[TestFixture]
[NonParallelizable]
public class McpLogPathTests
{
    private string? _previous;

    [SetUp]
    public void SetUp() => _previous = Environment.GetEnvironmentVariable(McpServerCommand.LogDirectoryEnvironmentVariable);

    [TearDown]
    public void TearDown() => Environment.SetEnvironmentVariable(McpServerCommand.LogDirectoryEnvironmentVariable, _previous);

    [Test]
    public void ResolveLogDirectory_Default_IsUnderLocalApplicationDataNotWorkingDirectory()
    {
        Environment.SetEnvironmentVariable(McpServerCommand.LogDirectoryEnvironmentVariable, null);

        var directory = McpServerCommand.ResolveLogDirectory();

        Path.IsPathRooted(directory).ShouldBeTrue();
        directory.ShouldEndWith(Path.Combine("csv2zugferd", "logs"));
        directory.ShouldNotStartWith(Path.Combine(Environment.CurrentDirectory, "logs"));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expectedRoot = string.IsNullOrEmpty(localAppData) ? Path.GetTempPath() : localAppData;
        directory.ShouldStartWith(expectedRoot);
    }

    [Test]
    public void ResolveLogDirectory_EnvironmentVariable_Wins()
    {
        var custom = Path.Combine(Path.GetTempPath(), "csv2zugferd-logtest");
        Environment.SetEnvironmentVariable(McpServerCommand.LogDirectoryEnvironmentVariable, custom);

        McpServerCommand.ResolveLogDirectory().ShouldBe(Path.GetFullPath(custom));
    }

    [Test]
    public void ResolveLogDirectory_BlankEnvironmentVariable_FallsBackToDefault()
    {
        Environment.SetEnvironmentVariable(McpServerCommand.LogDirectoryEnvironmentVariable, "   ");

        McpServerCommand.ResolveLogDirectory().ShouldEndWith(Path.Combine("csv2zugferd", "logs"));
    }
}
